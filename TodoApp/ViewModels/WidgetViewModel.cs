using System.Windows.Threading;
using System.Windows.Input;
using TodoApp.Services;

namespace TodoApp.ViewModels;
public sealed class WidgetViewModel : TaskListViewModel
{
    private string _widgetMode="Tasks";
    public string WidgetMode { get=>_widgetMode;set { if(SetProperty(ref _widgetMode,value)) {OnPropertyChanged(nameof(ShowWidgetTasks));OnPropertyChanged(nameof(ShowWidgetSchedules));} } }
    public bool ShowWidgetTasks => WidgetMode=="Tasks";
    public bool ShowWidgetSchedules => WidgetMode=="Schedule";
    public IReadOnlyList<TodoApp.Models.ScheduleOccurrence> UpcomingSchedules { get; private set; } = Array.Empty<TodoApp.Models.ScheduleOccurrence>();
    public bool HasNoSchedules => UpcomingSchedules.Count==0;
    public ICommand OpenScheduleDashboardCommand { get; }
    private readonly DispatcherTimer _draftTimer;
    private bool _alwaysOnTop;
    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set { if (SetProperty(ref _alwaysOnTop, value)) { var settings = Database.LoadSettings(); settings.AlwaysOnTop = value; Database.SaveSettings(settings); } }
    }
    public ICommand ToggleAlwaysOnTopCommand { get; }
    public WidgetViewModel(DatabaseService database, ReminderService reminder) : base(database, reminder)
    {
        OpenScheduleDashboardCommand=new RelayCommand(()=> (System.Windows.Application.Current as App)?.ShowScheduleDashboard());
        Reminder.OnDatabaseChanged+=RefreshSchedules; Reminder.OnClockTick+=RefreshSchedules;
        Localizer.Instance.Changed+=RefreshSchedules;RefreshSchedules();
        var settings = Database.LoadSettings();
        _alwaysOnTop = settings.AlwaysOnTop;
        Editor.LoadDraft(settings);
        _draftTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _draftTimer.Tick += (_, _) => SaveDraft();
        Editor.PropertyChanged += (_, e) => {
            if (!Editor.IsEditing && e.PropertyName is "Title" or "Notes" or "Priority" or "Date" or "Time" or "ReminderMinutes" or "UseDeadline")
            { _draftTimer.Stop(); _draftTimer.Start(); }
        };
        Editor.Saved += SaveDraft;
        ToggleAlwaysOnTopCommand = new RelayCommand(() => AlwaysOnTop = !AlwaysOnTop);
        Database.SettingsChanged += SyncSettings;
    }
    private void SyncSettings() { _alwaysOnTop = Database.LoadSettings().AlwaysOnTop; OnPropertyChanged(nameof(AlwaysOnTop)); }
    public void SaveDraft()
    {
        _draftTimer.Stop();
        if (!Editor.IsEditing)
        {
            try { Database.SaveDraft(Editor.Draft()); }
            catch (Exception ex) { Editor.Error = "Draft belum tersimpan: " + ex.Message; }
        }
    }
    private void RefreshSchedules()
    {
        var today=DateTime.Today;
        UpcomingSchedules=Database.GetSchedules().Where(s=>!s.IsCompletedAt(today))
            .SelectMany(s=>s.Between(today,today)).OrderBy(o=>o.Start).ToList();
        OnPropertyChanged(nameof(UpcomingSchedules));OnPropertyChanged(nameof(HasNoSchedules));
    }
    public override void Dispose() {
        SaveDraft(); Database.SettingsChanged -= SyncSettings;
        Reminder.OnDatabaseChanged-=RefreshSchedules;Reminder.OnClockTick-=RefreshSchedules;Localizer.Instance.Changed-=RefreshSchedules;
        base.Dispose();
    }
}
