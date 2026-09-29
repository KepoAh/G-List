using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TodoApp.Models;
using TodoApp.Services;

namespace TodoApp.ViewModels;
public class TaskListViewModel : ViewModelBase, IDisposable
{
    protected readonly DatabaseService Database;
    protected readonly ReminderService Reminder;
    private List<Todo> _active = new(), _completed = new();
    private ObservableCollection<Todo> _todos = new();
    private List<WeeklySchedule> _schedules = new();
    public ObservableCollection<object> DisplayItems { get; private set; } = new();
    private string _search = "", _filter = "All";
    public TaskEditorViewModel Editor { get; }
    public ObservableCollection<Todo> Todos { get => _todos; private set => SetProperty(ref _todos, value); }
    public string SearchQuery { get => _search; set { if (SetProperty(ref _search, value)) RefreshLists(); } }
    public string SelectedFilter { get => _filter; set { if (SetProperty(ref _filter, value)) { RefreshLists(); OnPropertyChanged(nameof(IsSettings)); OnPropertyChanged(nameof(IsTaskView)); OnPropertyChanged(nameof(SectionTitle)); OnPropertyChanged(nameof(SectionDescription)); } } }
    public bool IsSettings => SelectedFilter == "Settings";
    public bool IsCalendar => SelectedFilter == "Calendar";
    public bool IsSchedule => SelectedFilter == "Schedule";
    public bool IsTaskView => !IsSettings && !IsCalendar && !IsSchedule;
    public bool IsEmpty => DisplayItems.Count == 0;
    public int HeavyCount => _active.Count(t => t.EffectivePriority == "Heavy");
    public int MediumCount => _active.Count(t => t.EffectivePriority == "Medium");
    public int LightCount => _active.Count(t => t.EffectivePriority == "Light");
    public int OverdueCount => _active.Count(t => t.IsOverdue);
    public int ActiveCount => _active.Count;
    public int CompletedCount => _completed.Count + _schedules.Count(s=>s.IsCompleted);
    public string Summary => Localizer.Format("{0} tugas aktif · deadline terdekat lebih dulu", ActiveCount);
    public string TodayLabel => DateTime.Now.ToString("dddd, dd MMMM", Localizer.Instance.Culture);
    public string SectionTitle => SelectedFilter switch { "Schedule" => "Schedule", "Calendar" => Localizer.Text("Kalender"), "All" => Localizer.Text("Semua tugas"), "Completed" => Localizer.Text("Tugas selesai"), "Overdue" => Localizer.Text("Terlambat"), "Settings" => Localizer.Text("Pengaturan"), _ => SelectedFilter };
    public string EmptyMessage => string.IsNullOrWhiteSpace(SearchQuery) ? Localizer.Text("Belum ada tugas di daftar ini.") : Localizer.Text("Tidak ada tugas yang cocok dengan pencarian.");
    public string ActiveSummary => Localizer.Format("{0} tugas aktif", ActiveCount);
    public string VisibleSummary => Localizer.Format("{0} tugas", DisplayItems.Count);
    public string ClockLabel => DateTime.Now.ToString("HH:mm");
    public string SectionDescription => Localizer.Text(IsSchedule ? "Pilih hari dan kelola kegiatan rutin mingguanmu." : IsCalendar ? "Lihat jadwal bulananmu. Klik tugas untuk mengedit." : IsSettings ? "Atur pengalaman G-list sesuai kebutuhanmu." : "Fokus pada langkah berikutnya. Tugas dengan deadline terdekat muncul lebih dulu.");
    private readonly System.Windows.Threading.DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private string _toast = "";
    public string Toast { get => _toast; private set { SetProperty(ref _toast, value); OnPropertyChanged(nameof(HasToast)); } }
    public bool HasToast => Toast.Length > 0;
    protected void ShowToast(string message) { Toast = message; _toastTimer.Stop(); _toastTimer.Start(); }
    private void EditorSaved() => ShowToast(Localizer.Text("Tugas tersimpan."));
    public ICommand ClearSearchCommand { get; }
    public ICommand StartTodoCommand { get; }
    public ICommand SelectFilterCommand { get; }
    public ICommand EditTodoCommand { get; }
    public ICommand DeleteTodoCommand { get; }
    public ICommand ToggleCompleteCommand { get; }
    public ICommand OpenDashboardCommand { get; }

    private Action? _confirmedAction;
    private bool _isConfirmationOpen;
    private string _confirmationError = "";
    public bool IsConfirmationOpen { get => _isConfirmationOpen; private set => SetProperty(ref _isConfirmationOpen, value); }
    public string ConfirmationTitle { get; private set; } = "";
    public string ConfirmationItem { get; private set; } = "";
    public string ConfirmationDescription { get; private set; } = "";
    public string ConfirmationActionLabel { get; private set; } = "";
    public string ConfirmationError { get => _confirmationError; private set => SetProperty(ref _confirmationError, value); }
    public ICommand CancelConfirmationCommand { get; }
    public ICommand ConfirmActionCommand { get; }
    protected void AskConfirmation(string title, string item, string description, string actionLabel, Action action)
    {
        ConfirmationTitle = title; ConfirmationItem = item; ConfirmationDescription = description;
        ConfirmationActionLabel = actionLabel; ConfirmationError = ""; _confirmedAction = action;
        foreach (var name in new[] { nameof(ConfirmationTitle), nameof(ConfirmationItem), nameof(ConfirmationDescription), nameof(ConfirmationActionLabel) }) OnPropertyChanged(name);
        IsConfirmationOpen = true;
    }

    public TaskListViewModel(DatabaseService database, ReminderService reminder)
    {
        Database = database; Reminder = reminder; Editor = new(database, reminder);
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = ""; };
        Editor.Saved += EditorSaved;
        Localizer.Instance.Changed += LanguageChanged;
        ClearSearchCommand = new RelayCommand(() => SearchQuery = "");
        StartTodoCommand = new RelayCommand(p => { if (p is Todo todo && !todo.IsCompleted) { Reminder.StartWorking(todo); ShowToast(Localizer.Text("Status tugas diperbarui.")); } });
        SelectFilterCommand = new RelayCommand(p => SelectedFilter = p?.ToString() ?? "All");
        EditTodoCommand = new RelayCommand(p => { if (p is Todo todo) Editor.Edit(todo); });
        ToggleCompleteCommand = new RelayCommand(p => { if (p is Todo todo) { Reminder.ToggleComplete(todo); ShowToast(Localizer.Text("Status tugas diperbarui.")); } });
        DeleteTodoCommand = new RelayCommand(p => {
            if (p is Todo todo) AskConfirmation(Localizer.Text("Hapus tugas ini?"), todo.Title,
                Localizer.Text("Tugas akan dihapus dari daftar. Tindakan ini tidak bisa dibatalkan."), Localizer.Text("Hapus tugas"),
                () => { Database.DeleteTodo(todo.Id); Reminder.NotifyDatabaseChanged(); ShowToast(Localizer.Text("Tugas dihapus.")); });
        });
        CancelConfirmationCommand = new RelayCommand(() => { IsConfirmationOpen = false; _confirmedAction = null; });
        ConfirmActionCommand = new RelayCommand(() => {
            if (!IsConfirmationOpen || _confirmedAction == null) return;
            try { _confirmedAction(); IsConfirmationOpen = false; _confirmedAction = null; }
            catch (Exception ex) { ConfirmationError = Localizer.Text("Belum berhasil: ") + ex.Message; }
        });
        OpenDashboardCommand = new RelayCommand(() => (Application.Current as App)?.ShowDashboard());
        Reminder.OnDatabaseChanged += Reload;
        Reminder.OnClockTick += RefreshLists;
        Reload();
    }
    protected void Reload()
    {
        _active = Database.GetActiveTodos(); _completed = Database.GetCompletedTodos();
        _schedules = Database.GetSchedules();
        RefreshLists();
    }
    private void LanguageChanged()
    {
        Editor.RefreshLanguage();
        Toast = "";
        RefreshLists();
    }
    public void RefreshLists()
    {
        OnPropertyChanged(nameof(IsCalendar));
        OnPropertyChanged(nameof(IsSchedule));
        IEnumerable<Todo> tasks = SelectedFilter == "Completed" ? _completed : _active;
        if (SelectedFilter is "Heavy" or "Medium" or "Light") tasks = tasks.Where(t => t.EffectivePriority == SelectedFilter);
        if (SelectedFilter == "Overdue") tasks = tasks.Where(t => t.IsOverdue);
        var query = SearchQuery.Trim();
        if (query.Length > 0) tasks = tasks.Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || t.Notes.Contains(query, StringComparison.OrdinalIgnoreCase));
        tasks = SelectedFilter == "Completed" ? tasks.OrderByDescending(t => t.CompletedAt) : tasks.OrderBy(t => t.Deadline ?? DateTime.MaxValue).ThenByDescending(t => t.CreatedAt);
        Todos = new(tasks);
        var archived = SelectedFilter=="Completed" ? _schedules.Where(s=>s.IsCompleted &&
            (query.Length==0 || s.Title.Contains(query,StringComparison.OrdinalIgnoreCase) || s.Notes.Contains(query,StringComparison.OrdinalIgnoreCase))) : Enumerable.Empty<WeeklySchedule>();
        DisplayItems = new(Todos.Cast<object>().Concat(archived.OrderByDescending(s=>s.ArchivedAt??s.EndDate)));
        OnPropertyChanged(nameof(DisplayItems));
        foreach (var name in new[] { nameof(SectionTitle), nameof(SectionDescription), nameof(ActiveSummary), nameof(VisibleSummary), nameof(ClockLabel), nameof(HeavyCount), nameof(MediumCount), nameof(LightCount), nameof(OverdueCount), nameof(IsEmpty), nameof(ActiveCount), nameof(CompletedCount), nameof(Summary), nameof(TodayLabel), nameof(EmptyMessage) }) OnPropertyChanged(name);
    }
    public virtual void Dispose()
    {
        Localizer.Instance.Changed -= LanguageChanged; Editor.Saved -= EditorSaved; _toastTimer.Stop();
        Reminder.OnDatabaseChanged -= Reload; Reminder.OnClockTick -= RefreshLists;
    }
}
