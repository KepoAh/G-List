using System.Collections.ObjectModel;
using System.Windows.Input;
using TodoApp.Models;
using TodoApp.Services;
namespace TodoApp.ViewModels;
public sealed record CalendarDay(DateTime Date, bool InMonth, IReadOnlyList<Todo> Tasks)
{
    public IReadOnlyList<ScheduleOccurrence> Schedules { get; init; } = Array.Empty<ScheduleOccurrence>();
    public IEnumerable<CalendarEntry> Entries => Tasks.Select(t=>new CalendarEntry(t,null))
        .Concat(Schedules.Select(s=>new CalendarEntry(null,s))).OrderBy(e=>e.Start);
    public string Day => Date.Day.ToString();
    public bool IsToday => Date.Date == DateTime.Today;
    public string DateLabel => Date.ToString("D", Localizer.Instance.Culture);
}
public sealed class CalendarViewModel : ViewModelBase, IDisposable
{
    private readonly DatabaseService _database;
    private readonly ReminderService _reminder;
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private List<Todo> _tasks = new();
    private List<WeeklySchedule> _schedules = new();
    private ObservableCollection<CalendarDay> _days = new();
    public DateTime Month => _month;
    public string MonthLabel => _month.ToString("MMMM yyyy", Localizer.Instance.Culture);
    public string[] Weekdays => Enumerable.Range(0, 7).Select(i => Localizer.Instance.Culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)i)).ToArray();
    public ObservableCollection<CalendarDay> Days { get => _days; private set => SetProperty(ref _days, value); }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand TodayCommand { get; }
    public CalendarViewModel(DatabaseService database, ReminderService reminder)
    {
        _database = database; _reminder = reminder;
        PreviousCommand = new RelayCommand(() => Move(-1));
        NextCommand = new RelayCommand(() => Move(1));
        TodayCommand = new RelayCommand(() => ShowMonth(DateTime.Today));
        _reminder.OnDatabaseChanged += Reload;
        _reminder.OnClockTick += Refresh;
        Localizer.Instance.Changed += Refresh;
        Reload();
    }
    public void ShowMonth(DateTime date)
    {
        if (date.Year < 2 || date.Year > 9998) return;
        _month = new(date.Year, date.Month, 1); Refresh();
    }
    private void Move(int amount)
    {
        if (amount < 0 && _month <= new DateTime(2,1,1) || amount > 0 && _month >= new DateTime(9998,12,1)) return;
        ShowMonth(_month.AddMonths(amount));
    }
    private void Reload() { _tasks = _database.GetActiveTodos().Concat(_database.GetCompletedTodos()).ToList(); _schedules=_database.GetSchedules(); Refresh(); }
    private void Refresh()
    {
        var first = _month.AddDays(-(int)_month.DayOfWeek);
        var occurrences = _schedules.SelectMany(s=>s.Between(first,first.AddDays(41)))
            .GroupBy(o=>o.Start.Date).ToDictionary(g=>g.Key,g=>(IReadOnlyList<ScheduleOccurrence>)g.OrderBy(o=>o.Start).ToList());
        var grouped = _tasks.Where(t => t.Deadline.HasValue).GroupBy(t => t.Deadline!.Value.Date)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Todo>)g.OrderBy(t => t.Deadline).ThenBy(t => t.Title).ToList());
        Days = new(Enumerable.Range(0,42).Select(i => {
            var date = first.AddDays(i);
            return new CalendarDay(date, date.Month == _month.Month, grouped.GetValueOrDefault(date) ?? Array.Empty<Todo>()) {
                Schedules = occurrences.GetValueOrDefault(date) ?? Array.Empty<ScheduleOccurrence>() };
        }));
        OnPropertyChanged(nameof(Month)); OnPropertyChanged(nameof(MonthLabel)); OnPropertyChanged(nameof(Weekdays));
    }
    public void Dispose()
    {
        _reminder.OnDatabaseChanged -= Reload; _reminder.OnClockTick -= Refresh;
        Localizer.Instance.Changed -= Refresh;
    }
}
