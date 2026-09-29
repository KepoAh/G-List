using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Win32;
using TodoApp.Models;
using TodoApp.Services;

namespace TodoApp.ViewModels;
// WPF Selector tracks items by identity. Records include the mutable event delegate
// in their generated hash, which changes when label bindings subscribe/unsubscribe.
public sealed class PriorityOption(string value, string originalLabel) : System.ComponentModel.INotifyPropertyChanged
{
    public string Value { get; } = value;
    public string OriginalLabel { get; } = originalLabel;
    public string Label => Localizer.Text(Value switch { "Heavy" => "Heavy · 1–7 hari", "Medium" => "Medium · 8–14 hari", _ => "Light · Lebih dari 14 hari" });
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Label)));
    public override string ToString() => Label;
}
public sealed class ReminderOption(int minutes, string originalLabel) : System.ComponentModel.INotifyPropertyChanged
{
    public int Minutes { get; } = minutes;
    public string OriginalLabel { get; } = originalLabel;
    public string Label => Minutes switch { -1 => Localizer.Text("Tanpa pengingat"), 0 => Localizer.Text(OriginalLabel=="Schedule" ? "Saat kegiatan dimulai" : "Saat deadline"), _ => Localizer.Format("{0} menit sebelumnya", Minutes) };
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Label)));
    public override string ToString() => Label;
}
public sealed class TaskEditorViewModel : ViewModelBase
{
    private readonly DatabaseService _database;
    private readonly ReminderService _reminder;
    private string _title = "", _notes = "", _priority = "Heavy", _time = "17:00", _error = "", _soundPath = "";
    private DateTime? _date = DateTime.Today.AddDays(7);
    private bool _expanded, _useDeadline = true, _loading;
    private int _id, _reminderMinutes;
    public event Action? Saved;
    private bool _confirmationRequired;
    public bool ConfirmationRequired { get => _confirmationRequired; set => SetProperty(ref _confirmationRequired, value); }
    public string ConfirmationText => Localizer.Format("Tanggal yang dipilih masuk kategori {0}. Tetap gunakan {1} secara manual, atau ikuti kategori otomatis?", SuggestedPriority, Priority);
    public string SuggestedPriority => Date.HasValue ? Todo.CategoryFor(Date.Value, DateTime.Today) : Priority;
    public ICommand OpenCommand { get; }
    public ICommand KeepPriorityCommand { get; }
    public ICommand AutoPriorityCommand { get; }
    public ICommand BackCommand { get; }
    public PriorityOption[] PriorityOptions { get; } = {
        new("Heavy", Localizer.Text("Heavy · 1–7 hari")), new("Medium", Localizer.Text("Medium · 8–14 hari")), new("Light", Localizer.Text("Light · Lebih dari 14 hari")) };
    public ObservableCollection<ReminderOption> ReminderOptions { get; } = new() {
        new(0, Localizer.Text("Saat deadline")), new(15, Localizer.Text("15 menit sebelumnya")), new(30, Localizer.Text("30 menit sebelumnya")), new(60, Localizer.Text("1 jam sebelumnya")), new(-1, Localizer.Text("Tanpa pengingat")) };
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string Priority { get => _priority; set { if (SetProperty(ref _priority, value) && !_loading) Date = PresetDate(value, DateTime.Today); } }
    public DateTime? Date { get => _date; set => SetProperty(ref _date, value); }
    public string Time { get => _time; set => SetProperty(ref _time, value); }
    public bool UseDeadline { get => _useDeadline; set => SetProperty(ref _useDeadline, value); }
    public int ReminderMinutes { get => _reminderMinutes; set => SetProperty(ref _reminderMinutes, value); }
    public bool IsExpanded { get => _expanded; set => SetProperty(ref _expanded, value); }
    public bool IsEditing => _id != 0;
    public string SaveLabel => IsEditing ? Localizer.Text("Simpan perubahan") : Localizer.Text("Tambah tugas");
    public string Error { get => _error; set => SetProperty(ref _error, value); }
    public string SoundLabel => string.IsNullOrEmpty(_soundPath) ? Localizer.Text("Suara default") : System.IO.Path.GetFileName(_soundPath);
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseSoundCommand { get; }

    public TaskEditorViewModel(DatabaseService database, ReminderService reminder)
    {
        _database = database; _reminder = reminder;
        PropertyChanged += (_, e) => {
            if (e.PropertyName is nameof(Date) or nameof(Time) or nameof(UseDeadline) or nameof(ReminderMinutes))
                OnPropertyChanged(nameof(ScheduleSummary));
        };
        SaveCommand = new RelayCommand(() => Save());
        OpenCommand = new RelayCommand(() => IsExpanded = true);
        KeepPriorityCommand = new RelayCommand(() => Save(true));
        AutoPriorityCommand = new RelayCommand(() => Save(false));
        BackCommand = new RelayCommand(() => ConfirmationRequired = false);
        CancelCommand = new RelayCommand(() => { if (IsEditing) Reset(); else IsExpanded = false; Error = ""; });
        BrowseSoundCommand = new RelayCommand(() => {
            var dialog = new OpenFileDialog { Filter = "Audio MP3 (*.mp3)|*.mp3" };
            if (dialog.ShowDialog() == true) { _soundPath = dialog.FileName; OnPropertyChanged(nameof(SoundLabel)); }
        });
    }
    public void RefreshLanguage()
    {
        foreach (var option in PriorityOptions) option.Refresh();
        foreach (var option in ReminderOptions) option.Refresh();
        foreach (var name in new[] { nameof(SaveLabel), nameof(SoundLabel), nameof(ConfirmationText) }) OnPropertyChanged(name);
        OnPropertyChanged(nameof(ScheduleSummary));
    }
    public string ScheduleSummary => !UseDeadline ? Localizer.Text("Tanpa deadline") :
        Localizer.Format("Deadline: {0} · {1}. Pengingat: {2}.",
            Date?.ToString("dd MMM yyyy", Localizer.Instance.Culture) ?? "—", Time,
            ReminderOptions.FirstOrDefault(o => o.Minutes == ReminderMinutes)?.Label ?? "—");
    public static DateTime PresetDate(string priority, DateTime today) => priority switch
    { "Light" => today.Date.AddMonths(1), "Medium" => today.Date.AddDays(14), _ => today.Date.AddDays(7) };
    public void Edit(Todo todo)
    {
        _loading = true;
        _id = todo.Id; Title = todo.Title; Notes = todo.Notes; Priority = todo.EffectivePriority;
        Date = todo.Deadline?.Date ?? PresetDate(Priority, DateTime.Today);
        Time = todo.Deadline?.ToString("HH:mm") ?? "17:00"; UseDeadline = todo.Deadline.HasValue;
        var minutes = todo.NotificationTime.HasValue && todo.Deadline.HasValue
            ? Math.Max(0, (int)(todo.Deadline.Value - todo.NotificationTime.Value).TotalMinutes) : -1;
        if (!ReminderOptions.Any(o => o.Minutes == minutes)) ReminderOptions.Add(new(minutes, Localizer.Format("{0} menit sebelumnya", minutes)));
        ReminderMinutes = minutes; _soundPath = todo.SoundPath;
        _loading = false; Error = ""; IsExpanded = true;
        OnPropertyChanged(nameof(IsEditing)); OnPropertyChanged(nameof(SaveLabel)); OnPropertyChanged(nameof(SoundLabel));
    }
    public void LoadDraft(Settings settings)
    {
        _loading = true;
        Title = settings.DraftTitle; Notes = settings.DraftNotes;
        Priority = PriorityOptions.Any(p => p.Value == settings.DraftPriority) ? settings.DraftPriority : "Heavy";
        Date = DateTime.TryParse(settings.DraftDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : PresetDate(Priority, DateTime.Today);
        Time = settings.DraftTime; ReminderMinutes = settings.DraftReminder; UseDeadline = settings.DraftUseDeadline;
        _loading = false; IsExpanded = false;
    }
    public Settings Draft() => new() { DraftTitle = Title, DraftNotes = Notes, DraftPriority = Priority,
        DraftDate = Date?.ToString("yyyy-MM-dd") ?? "", DraftTime = Time, DraftReminder = ReminderMinutes, DraftUseDeadline = UseDeadline };
    public void Reset()
    {
        _loading = true;
        ConfirmationRequired = false; _id = 0; Title = ""; Notes = ""; _soundPath = "";
        Date = PresetDate(Priority, DateTime.Today); UseDeadline = true;
        _loading = false; Error = ""; IsExpanded = false;
        OnPropertyChanged(nameof(IsEditing)); OnPropertyChanged(nameof(SaveLabel)); OnPropertyChanged(nameof(SoundLabel));
    }
    private void Save(bool? keepManual = null)
    {
        Error = "";
        if (string.IsNullOrWhiteSpace(Title)) { Error = Localizer.Text("Isi judul tugas terlebih dahulu."); IsExpanded = true; return; }
        DateTime? deadline = null;
        if (UseDeadline)
        {
            if (!Date.HasValue || !DateTime.TryParseExact(Time.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            { Error = Localizer.Text("Pilih tanggal dan isi jam 24 jam, misalnya 17:30."); IsExpanded = true; return; }
            deadline = Date.Value.Date.Add(parsed.TimeOfDay);
        }
        if (deadline.HasValue && SuggestedPriority != Priority && keepManual == null)
        {
            OnPropertyChanged(nameof(ConfirmationText)); ConfirmationRequired = true; return;
        }
        ConfirmationRequired = false;
        try
        {
            var todo = _id == 0 ? new Todo() : _database.GetTodo(_id);
            if (todo == null) { Error = Localizer.Text("Tugas sudah dihapus. Tutup form untuk membuat tugas baru."); return; }
            DateTime? notification = deadline.HasValue && ReminderMinutes >= 0 ? deadline.Value.AddMinutes(-ReminderMinutes) : null;
            if (todo.NotificationTime != notification) { todo.LastRemindedAt = null; todo.SnoozeUntil = null; }
            todo.Title = Title.Trim(); todo.Notes = Notes.Trim(); todo.Priority = keepManual == false && deadline.HasValue ? SuggestedPriority : Priority;
            todo.ManualPriority = keepManual == true || !deadline.HasValue;
            todo.Deadline = deadline; todo.NotificationTime = notification; todo.SoundPath = _soundPath;
            _database.SaveTodo(todo); _reminder.NotifyDatabaseChanged();
            Reset(); Saved?.Invoke();
        }
        catch (Exception ex) { Error = Localizer.Text("Tugas belum tersimpan: ") + ex.Message; }
    }
}

