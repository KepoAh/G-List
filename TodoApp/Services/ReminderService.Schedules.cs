using System.Windows;
using System.Windows.Threading;
using TodoApp.Models;
using TodoApp.Views;
namespace TodoApp.Services;
public sealed partial class ReminderService
{
    private ScheduleNotificationWindow? _scheduleNotification;
    private ScheduleOccurrence? _shownSchedule;
    public IReadOnlyList<ScheduleOccurrence> GetDueSchedules(DateTime now) =>
        _database.GetSchedules().Where(s => !s.IsCompletedAt(now) && s.ReminderMinutes >= 0)
        .SelectMany(s => s.Between(now.Date.AddDays(-1),now.Date.AddDays(1)))
        .Where(o => o.ReminderAt <= now && now < o.End && !_database.WasScheduleReminded(o))
        .OrderBy(o => o.ReminderAt).ToList();
    private bool CheckScheduleReminders(DateTime now)
    {
        foreach(var occurrence in GetDueSchedules(now))
        {
            if(!_database.TryClaimScheduleReminder(occurrence,now)) continue;
            _shownSchedule=occurrence;
            var window = new ScheduleNotificationWindow(occurrence,this);
            _scheduleNotification=window;
            window.Closed += (_,_) => {
                _shownSchedule=null; _scheduleNotification=null; StopSound();
                if(!_shutdown) Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(CheckReminders));
            };
            OnReminderShown?.Invoke(); window.Show();
            Play(new Todo()); // Uses the shared default sound and volume.
            return true;
        }
        return false;
    }
    private void CloseInvalidScheduleReminder()
    {
        if(_shownSchedule is not { } shown) return;
        var current=_database.GetSchedule(shown.Schedule.Id);
        if(current==null || current.IsCompleted || current.Revision!=shown.Schedule.Revision || shown.End<=DateTime.Now)
            _scheduleNotification?.Close();
    }
}
