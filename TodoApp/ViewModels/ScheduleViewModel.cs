using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using TodoApp.Models;
using TodoApp.Services;
namespace TodoApp.ViewModels;
public sealed class ScheduleDayOption(int value) : ViewModelBase
{
    public int Value { get; } = value;
    private bool _isSelected;
    public bool IsSelected { get=>_isSelected; set=>SetProperty(ref _isSelected,value); }
    public string Label => Localizer.Instance.Culture.DateTimeFormat.GetDayName((DayOfWeek)Value);
    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}
public sealed class ScheduleViewModel : ViewModelBase, IDisposable
{
    private readonly DatabaseService _database;
    private readonly ReminderService _reminder;
    private readonly Action<WeeklySchedule> _archive;
    private readonly Action<string> _feedback;
    private int _selectedDay=-1, _id, _revision=1, _reminderMinutes=15;
    private string _title="",_notes="",_startTime="08:00",_endTime="09:00",_error="";
    private DateTime? _startDate,_endDate;
    private DateTime _createdAt=DateTime.Now;
    private ObservableCollection<WeeklySchedule> _items=new();
    private List<WeeklySchedule> _all=new();
    public ScheduleDayOption[] Days { get; } = Enumerable.Range(0,7).Select(d=>new ScheduleDayOption(d)).ToArray();
    public int SelectedDay { get=>_selectedDay; set { if(SetProperty(ref _selectedDay,value)) Refresh(); } }
    public bool HasDay => SelectedDay>=0;
    public string SelectedDayLabel => HasDay ? Localizer.Instance.Culture.DateTimeFormat.GetDayName((DayOfWeek)SelectedDay) : "";
    public ObservableCollection<WeeklySchedule> Items { get=>_items; private set=>SetProperty(ref _items,value); }
    public bool IsEmpty => Items.Count==0;
    public int ActiveCount => _all.Count(s=>!s.IsCompleted);
    public string Title { get=>_title;set=>SetProperty(ref _title,value); }
    public string Notes { get=>_notes;set=>SetProperty(ref _notes,value); }
    public string StartTime { get=>_startTime;set=>SetProperty(ref _startTime,value); }
    public string EndTime { get=>_endTime;set=>SetProperty(ref _endTime,value); }
    public DateTime? StartDate { get=>_startDate;set=>SetProperty(ref _startDate,value); }
    public DateTime? EndDate { get=>_endDate;set=>SetProperty(ref _endDate,value); }
    public int ReminderMinutes { get=>_reminderMinutes;set=>SetProperty(ref _reminderMinutes,value); }
    public ReminderOption[] ReminderOptions { get; } = { new(0,"Schedule"),new(15,""),new(30,""),new(60,""),new(-1,"") };
    public string Error { get=>_error;set=>SetProperty(ref _error,value); }
    public bool IsEditing => _id!=0;
    public string FormTitle => Localizer.Text(IsEditing?"Ubah jadwal":"Tambah jadwal");
    public ICommand SelectDayCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand ArchiveCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ClearDatesCommand { get; }
    public ScheduleViewModel(DatabaseService database,ReminderService reminder,Action<WeeklySchedule> archive,Action<string> feedback)
    {
        _database=database;_reminder=reminder;_archive=archive;_feedback=feedback;
        SelectDayCommand=new RelayCommand(p=> { if(int.TryParse(p?.ToString(),out var day)) SelectedDay=day; });
        EditCommand=new RelayCommand(p=> { if(p is WeeklySchedule schedule) Edit(schedule); });
        ArchiveCommand=new RelayCommand(p=> { if(p is WeeklySchedule schedule) _archive(schedule); });
        SaveCommand=new RelayCommand(Save);
        CancelCommand=new RelayCommand(Reset);
        ClearDatesCommand=new RelayCommand(()=> {StartDate=null;EndDate=null;});
        _reminder.OnDatabaseChanged+=Reload;_reminder.OnClockTick+=Refresh;Localizer.Instance.Changed+=RefreshLanguage;
        Reload();
    }
    private void Reload()
    {
        _all=_database.GetSchedules();
        if(_id!=0 && _all.FirstOrDefault(s=>s.Id==_id)?.ArchivedAt!=null) Reset();
        Refresh();
    }
    private void Refresh()
    {
        foreach(var day in Days) day.IsSelected=day.Value==SelectedDay;
        Items=new(_all.Where(s=>!s.IsCompleted && (int)s.Weekday==SelectedDay).OrderBy(s=>s.StartMinute));
        foreach(var name in new[]{nameof(HasDay),nameof(SelectedDayLabel),nameof(IsEmpty),nameof(ActiveCount)}) OnPropertyChanged(name);
    }
    private void RefreshLanguage()
    {
        foreach(var option in ReminderOptions) option.Refresh();
        foreach(var day in Days) day.RefreshLabel();
        OnPropertyChanged(nameof(Days));OnPropertyChanged(nameof(FormTitle));Refresh();
    }
    public void Edit(WeeklySchedule schedule)
    {
        var current=_database.GetSchedule(schedule.Id);
        if(current==null || current.IsCompleted) { _feedback(Localizer.Text("Jadwal ini sudah selesai."));return; }
        _id=current.Id;_revision=current.Revision;_createdAt=current.CreatedAt;
        SelectedDay=(int)current.Weekday;Title=current.Title;Notes=current.Notes;
        StartTime=TimeSpan.FromMinutes(current.StartMinute).ToString(@"hh\:mm");
        EndTime=TimeSpan.FromMinutes(current.EndMinute).ToString(@"hh\:mm");
        StartDate=current.StartDate;EndDate=current.EndDate;ReminderMinutes=current.ReminderMinutes;
        Error="";OnPropertyChanged(nameof(IsEditing));OnPropertyChanged(nameof(FormTitle));
    }
    public void Reset()
    {
        _id=0;_revision=1;_createdAt=DateTime.Now;Title="";Notes="";StartDate=null;EndDate=null;Error="";
        OnPropertyChanged(nameof(IsEditing));OnPropertyChanged(nameof(FormTitle));
    }
    private void Save()
    {
        Error="";
        if (_id==0) _createdAt=DateTime.Now;
        if(!HasDay || string.IsNullOrWhiteSpace(Title)) {Error=Localizer.Text("Pilih hari dan isi nama kegiatan.");return;}
        if(!DateTime.TryParseExact(StartTime.Trim(),new[]{"H:mm","HH:mm"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var start) ||
           !DateTime.TryParseExact(EndTime.Trim(),new[]{"H:mm","HH:mm"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var end))
        {Error=Localizer.Text("Gunakan format jam 24 jam, misalnya 08:30.");return;}
        if(end.TimeOfDay<=start.TimeOfDay) {Error=Localizer.Text("Jam selesai harus setelah jam mulai pada hari yang sama.");return;}
        if(EndDate?.Date < (StartDate??_createdAt).Date) {Error=Localizer.Text("Tanggal akhir harus sama atau setelah tanggal mulai.");return;}
        var schedule=new WeeklySchedule {Id=_id,Revision=_revision,CreatedAt=_createdAt,Title=Title,Notes=Notes,
            Weekday=(DayOfWeek)SelectedDay,StartMinute=(int)start.TimeOfDay.TotalMinutes,EndMinute=(int)end.TimeOfDay.TotalMinutes,
            ReminderMinutes=ReminderMinutes,StartDate=StartDate?.Date,EndDate=EndDate?.Date};
        try { _database.SaveSchedule(schedule);Reset();_reminder.NotifyDatabaseChanged();_feedback(Localizer.Text("Jadwal tersimpan dan kalender diperbarui.")); }
        catch(Exception ex) {Error=ex.Message;}
    }
    public void Dispose() { _reminder.OnDatabaseChanged-=Reload;_reminder.OnClockTick-=Refresh;Localizer.Instance.Changed-=RefreshLanguage; }
}
