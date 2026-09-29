using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TodoApp.Services;
using TodoApp.Models;
using TodoApp.Views;

namespace TodoApp.ViewModels;
public sealed class DashboardViewModel : TaskListViewModel
{
    public CalendarViewModel Calendar { get; }
    public ScheduleViewModel Schedules { get; }
    public ICommand OpenCalendarEntryCommand { get; }
    private readonly SoundTestService _soundTest = new();
    private NotificationWindow? _previewWindow;
    private bool _disposed;
    public ICommand TestSoundCommand { get; }
    public ICommand StopTestSoundCommand { get; }
    public ICommand PreviewReminderCommand { get; }
    private bool _startup, _alwaysOnTop, _loading;
    private double _volume;
    private string _language = "id";
    public string Language { get => _language; set { if (SetProperty(ref _language, value) && !_loading) SaveSettings(); } }
    private string _defaultSound = "", _message = "";
    public string Message { get => _message; set { SetProperty(ref _message, value); if (!string.IsNullOrEmpty(value)) ShowToast(value); } }
    public bool Startup { get => _startup; set { if (SetProperty(ref _startup, value) && !_loading) { StartupService.SetStartup(value); SaveSettings(); } } }
    public bool AlwaysOnTop { get => _alwaysOnTop; set { if (SetProperty(ref _alwaysOnTop, value) && !_loading) SaveSettings(); } }
    public double Volume { get => _volume; set { if (SetProperty(ref _volume, value) && !_loading) SaveSettings(); } }
    public string DefaultSound { get => _defaultSound; set { if (SetProperty(ref _defaultSound, value)) { OnPropertyChanged(nameof(DefaultSoundName)); if (!_loading) SaveSettings(); } } }
    public string DataLocation => System.IO.Path.Combine(Database.DataDirectory, "todo.db");
    public string DefaultSoundName => System.IO.Path.GetFileName(DefaultSound);
    public ICommand BrowseDefaultSoundCommand { get; }
    public ICommand BackupDatabaseCommand { get; }
    public ICommand RestoreDatabaseCommand { get; }
    public ICommand ShowWidgetCommand { get; }
    public DashboardViewModel(DatabaseService database, ReminderService reminder) : base(database, reminder)
    {
        SyncSettings();
        Calendar = new CalendarViewModel(database, reminder);
        Schedules = new ScheduleViewModel(database, reminder, schedule =>
            AskConfirmation(Localizer.Text("Akhiri jadwal ini?"), schedule.Title,
                Localizer.Text("Pengingat akan berhenti. Jadwal beserta rentang tanggalnya dipindahkan ke Selesai."),
                Localizer.Text("Akhiri jadwal"), () => {
                    Database.ArchiveSchedule(schedule.Id, DateTime.Now);
                    Reminder.NotifyDatabaseChanged();
                    ShowToast(Localizer.Text("Jadwal dipindahkan ke Selesai."));
                }), ShowToast);
        OpenCalendarEntryCommand = new RelayCommand(p => {
            if(p is CalendarEntry entry && entry.Schedule is { } occurrence)
            {
                if(occurrence.Schedule.IsCompleted) { SelectedFilter="Completed"; return; }
                SelectedFilter="Schedule"; Schedules.Edit(occurrence.Schedule);
            }
            else if(p is CalendarEntry taskEntry && taskEntry.Task is { } todo) EditTodoCommand.Execute(todo);
        });
        _soundTest.Feedback += SoundFeedback;
        Reminder.OnReminderShown += StopSoundTest;
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SelectedFilter) && !IsSettings) StopSoundTest(); };
        TestSoundCommand = new RelayCommand(() => Run(() => _soundTest.Play(DefaultSound, Volume)));
        StopTestSoundCommand = new RelayCommand(StopSoundTest);
        PreviewReminderCommand = new RelayCommand(() => {
            if (_previewWindow == null)
            {
                _previewWindow = CreateReminderPreview();
                _previewWindow.Closed += (_, _) => _previewWindow = null;
            }
            _previewWindow.Show(); _previewWindow.Activate();
        });
        Database.SettingsChanged += SyncSettings;
        ShowWidgetCommand = new RelayCommand(() => { StopSoundTest(); (Application.Current as App)?.ShowWidget(); });
        BrowseDefaultSoundCommand = new RelayCommand(() => {
            var picker = new OpenFileDialog { Filter = "Audio MP3 (*.mp3)|*.mp3" };
            if (picker.ShowDialog() == true) DefaultSound = picker.FileName;
        });
        BackupDatabaseCommand = new RelayCommand(() => {
            var picker = new SaveFileDialog { Filter = "Database G-list (*.db)|*.db", FileName = "G-list-backup.db" };
            if (picker.ShowDialog() == true) Run(() => { Database.BackupDatabase(picker.FileName); Message = Localizer.Text("Backup berhasil disimpan."); });
        });
        RestoreDatabaseCommand = new RelayCommand(() => {
            var picker = new OpenFileDialog { Filter = "Database G-list (*.db)|*.db" };
            if (picker.ShowDialog() == true)
                AskConfirmation(Localizer.Text("Pulihkan backup?"), System.IO.Path.GetFileName(picker.FileName),
                    Localizer.Text("Data saat ini akan diganti. Salinan data sebelumnya akan disimpan otomatis."), Localizer.Text("Pulihkan"),
                    () => { Database.RestoreDatabase(picker.FileName); Reminder.NotifyDatabaseChanged(); Message = Localizer.Text("Tugas berhasil dipulihkan."); });
        });
    }
    private void Run(Action action) { try { action(); } catch (Exception ex) { Message = Localizer.Text("Belum berhasil: ") + ex.Message; } }
    private void SyncSettings()
    {
        _loading = true;
        var s = Database.LoadSettings();
        Language = s.Language;
        Localizer.Instance.SetLanguage(s.Language);
        Startup = StartupService.IsStartupEnabled(); AlwaysOnTop = s.AlwaysOnTop; Volume = s.Volume; DefaultSound = s.DefaultSound;
        _loading = false;
    }
    private void SaveSettings()
    {
        Run(() => { var s = Database.LoadSettings(); s.Language = Language; s.Startup = Startup; s.AlwaysOnTop = AlwaysOnTop; s.Volume = Volume; s.DefaultSound = DefaultSound; Database.SaveSettings(s); ShowToast(Localizer.Text("Pengaturan tersimpan.")); });
    }
    public NotificationWindow CreateReminderPreview() => new(new Todo {
        Title = Localizer.Text("Contoh: Siapkan kegiatan berikutnya"),
        Notes = Localizer.Text("Ini hanya preview. Tombol di sini tidak mengubah tugas atau jadwal pengingat."),
        Deadline = DateTime.Now.AddMinutes(30), Priority = "Heavy"
    }, Reminder, isPreview: true);
    private void SoundFeedback(string message) => ShowToast(message);
    private void StopSoundTest() => _soundTest.Stop();
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Database.SettingsChanged -= SyncSettings;
        Reminder.OnReminderShown -= StopSoundTest;
        _soundTest.Feedback -= SoundFeedback; _soundTest.Dispose();
        _previewWindow?.Close(); Calendar.Dispose(); Schedules.Dispose(); base.Dispose();
    }
}
