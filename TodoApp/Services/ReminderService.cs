using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TodoApp.Models;
using TodoApp.Views;

namespace TodoApp.Services;

public sealed partial class ReminderService
{
    private readonly DatabaseService _database;
    private readonly DispatcherTimer _timer;
    private readonly MediaPlayer _player = new();
    private readonly bool _enabled;
    private List<Todo> _cache = new();
    private NotificationWindow? _notification;
    private Todo? _shownTodo;
    private int _plays;
    private bool _shutdown;
    public event Action? OnDatabaseChanged;
    public event Action? OnClockTick;
    public event Action? OnReminderShown;
    public event Action<bool>? OnTrayBlinkStateChanged;

    public ReminderService(DatabaseService database, bool startTimers = true)
    {
        _database = database; _enabled = startTimers;
        RefreshCache();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => { OnClockTick?.Invoke(); CloseInvalidScheduleReminder(); CheckReminders(); };
        _player.MediaEnded += (_, _) =>
        {
            if (++_plays < 2) { _player.Position = TimeSpan.Zero; _player.Play(); }
            else { StopSound(); OnTrayBlinkStateChanged?.Invoke(true); }
        };
        _player.MediaFailed += (_, _) => { StopSound(); if (_enabled && _database.LoadSettings().Volume > 0) System.Media.SystemSounds.Exclamation.Play(); };
        if (startTimers)
        {
            _timer.Start();
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(CheckReminders));
        }
    }
    public void RefreshCache() => _cache = _database.GetPendingReminders();
    public void NotifyDatabaseChanged()
    {
        RefreshCache();
        CloseInvalidScheduleReminder();
        if (_shownTodo is { } shown)
        {
            var current = _database.GetTodo(shown.Id);
            if (current == null || current.Status != shown.Status || current.SnoozeUntil != shown.SnoozeUntil ||
                current.Deadline != shown.Deadline || current.NotificationTime != shown.NotificationTime ||
                current.Title != shown.Title || current.Notes != shown.Notes || current.SoundPath != shown.SoundPath)
                _notification?.Close();
        }
        OnDatabaseChanged?.Invoke();
    }
    public static bool IsDue(Todo todo, DateTime now)
    {
        if (todo.IsCompleted || !todo.NotificationTime.HasValue) return false;
        if (todo.SnoozeUntil.HasValue) return todo.SnoozeUntil.Value <= now;
        if (todo.NotificationTime > now) return false;
        if (!todo.LastRemindedAt.HasValue || todo.LastRemindedAt < todo.NotificationTime) return true;
        return todo.Status == "Not Started" && todo.LastRemindedAt.Value.AddMinutes(30) <= now;
    }
    private void CheckReminders()
    {
        if (_shutdown || !_enabled || _notification != null || _scheduleNotification != null) return;
        if (CheckScheduleReminders(DateTime.Now)) return;
        var todo = _cache.Where(t => IsDue(t, DateTime.Now)).OrderBy(t => t.NotificationTime).FirstOrDefault();
        if (todo == null) return;
        todo = _database.GetTodo(todo.Id);
        if (todo == null || !IsDue(todo, DateTime.Now)) return;
        todo.SnoozeUntil = null; todo.LastRemindedAt = DateTime.Now;
        _database.SaveTodo(todo);
        _shownTodo = todo;
        var window = new NotificationWindow(todo, this);
        _notification = window;
        window.Closed += (_, _) =>
        {
            _notification = null; _shownTodo = null; StopSound();
            if (_shutdown) return;
            var current = _database.GetTodo(todo.Id);
            if (current is { Status: "Not Started", SnoozeUntil: null })
            {
                current.LastRemindedAt = DateTime.Now;
                _database.SaveTodo(current);
            }
            RefreshCache();
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(CheckReminders));
        };
        OnReminderShown?.Invoke();
        window.Show();
        NotifyDatabaseChanged();
        Play(todo);
    }
    private void Play(Todo todo)
    {
        StopSound();
        var settings = _database.LoadSettings();
        _player.Volume = Math.Clamp(settings.Volume, 0, 1);
        string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        var path = string.IsNullOrWhiteSpace(todo.SoundPath) ? "" : Resolve(todo.SoundPath);
        if (!File.Exists(path)) path = Resolve(settings.DefaultSound);
        if (File.Exists(path)) { _player.Open(new Uri(path)); _player.Play(); }
        else if (settings.Volume > 0) System.Media.SystemSounds.Exclamation.Play();
    }
    public void StopSound() { _player.Stop(); _plays = 0; OnTrayBlinkStateChanged?.Invoke(false); }
    public void MuteSound() => StopSound();
    public void StartWorking(Todo todo) => ChangeActive(todo.Id, t => t.Status = "In Progress");
    public void CompleteTodo(Todo todo) => ChangeActive(todo.Id, t => { t.Status = "Completed"; t.CompletedAt = DateTime.Now; });
    public void SnoozeTodo(Todo todo, int minutes) => ChangeActive(todo.Id, t => t.SnoozeUntil = DateTime.Now.AddMinutes(minutes));
    private void ChangeActive(int id, Action<Todo> change)
    {
        var current = _database.GetTodo(id);
        if (current == null || current.IsCompleted) { NotifyDatabaseChanged(); return; }
        current.SnoozeUntil = null; current.LastRemindedAt = DateTime.Now;
        change(current);
        _database.SaveTodo(current);
        NotifyDatabaseChanged();
    }
    public void ToggleComplete(Todo todo)
    {
        var current = _database.GetTodo(todo.Id);
        if (current == null) return;
        if (!current.IsCompleted) { CompleteTodo(current); return; }
        current.Status = "Not Started"; current.CompletedAt = null;
        current.SnoozeUntil = null; current.LastRemindedAt = DateTime.Now;
        _database.SaveTodo(current); NotifyDatabaseChanged();
    }
    public void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true; _timer.Stop(); _notification?.Close(); _scheduleNotification?.Close(); StopSound(); _player.Close();
    }
}
