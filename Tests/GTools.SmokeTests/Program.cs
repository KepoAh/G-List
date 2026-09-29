using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using TodoApp.Models;
using TodoApp.Services;
using TodoApp.ViewModels;
using TodoApp.Views;

internal static class Program
{
    // Application queues startup even when tests pump Dispatcher without calling Run().
    // Never execute the production startup path, tray, mutex, or user database in tests.
    private static int _assertions;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAILED: " + description);
        Console.WriteLine("PASS: " + description);
        _assertions++;
    }
    [STAThread]
    private static void Main(string[] args) { try { Run(args); } catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; } }
    private static void Run(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "smoke-results"));
        Directory.CreateDirectory(output);
        var data = Path.Combine(output, "data-" + Guid.NewGuid().ToString("N"));
        var db = new DatabaseService(data);
        var app = new TodoApp.App(suppressStartup: true);
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        using var bindingLog = new StringWriter();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(bindingLog));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var reminder = new ReminderService(db, startTimers: false);
        var editor = new TaskEditorViewModel(db, reminder);

        Check(editor.Date == DateTime.Today.AddDays(7), "Heavy preset = H+7");
        editor.Priority = "Medium";
        Check(editor.Date == DateTime.Today.AddDays(14), "Medium preset = H+14");
        editor.Priority = "Light";
        Check(editor.Date == DateTime.Today.AddMonths(1), "Light preset = one calendar month");
        Check(TaskEditorViewModel.PresetDate("Light", new DateTime(2026, 1, 31)) == new DateTime(2026, 2, 28), "Month-end preset is a valid calendar date");
        editor.Title = "Rapikan portfolio developer";
        editor.Notes = "Pilih proyek HRIS dan tulis kontribusi.";
        editor.Time = "18:45";
        editor.ReminderMinutes = 15;
        editor.SaveCommand.Execute(null);
        var task = db.GetActiveTodos().Single();
        Check(task.Notes.Contains("HRIS") && task.Deadline!.Value.TimeOfDay == new TimeSpan(18,45,0), "Title, description and deadline time persist");
        Check(task.NotificationTime == task.Deadline!.Value.AddMinutes(-15), "Reminder is relative to selected deadline");
        var restarted = new DatabaseService(data);
        Check(restarted.GetTodo(task.Id)?.Title == task.Title, "Data survives a new database connection");

        using var widgetVm = new WidgetViewModel(db, reminder);
        using var dashboardVm = new DashboardViewModel(db, reminder);
        Check(widgetVm.Todos.Any(t => t.Id == task.Id) && dashboardVm.Todos.Any(t => t.Id == task.Id), "Light task beyond one month visible in widget and dashboard");
        dashboardVm.SearchQuery = "HRIS";
        Check(dashboardVm.Todos.Count == 1, "Search includes descriptions beyond seven days");
        dashboardVm.SelectedFilter = "Heavy";
        Check(dashboardVm.Todos.Count == 0, "Priority filter isolates matching tasks");
        dashboardVm.SearchQuery = ""; dashboardVm.SelectedFilter = "All";
        editor.Title = "Invalid time"; editor.Time = "25:90";
        editor.SaveCommand.Execute(null);
        Check(db.GetActiveTodos().Count == 1 && editor.Error.Length > 0, "Invalid time is rejected without saving");
        editor.Edit(task); editor.Date = task.Deadline!.Value.Date.AddDays(5); editor.SaveCommand.Execute(null);
        task = db.GetTodo(task.Id)!;
        Check(task.NotificationTime == task.Deadline!.Value.AddMinutes(-15), "Editing deadline shifts reminder");

        db.SaveDraft(new Settings { DraftTitle = "Draft utuh", DraftNotes = "Deskripsi draft", DraftPriority = "Light", DraftDate = "2026-12-20", DraftTime = "20:30", DraftUseDeadline = false });
        dashboardVm.Volume = .37;
        var draft = db.LoadSettings();
        Check(draft.DraftTitle == "Draft utuh" && draft.DraftNotes == "Deskripsi draft" && !draft.DraftUseDeadline, "Changing volume preserves full draft");
        widgetVm.AlwaysOnTop = true;
        Check(dashboardVm.AlwaysOnTop && db.LoadSettings().AlwaysOnTop, "Pin setting synchronizes between views");

        var stale = db.GetTodo(task.Id)!;
        reminder.CompleteTodo(task);
        reminder.StartWorking(stale);
        Check(db.GetTodo(task.Id)!.IsCompleted, "An old popup cannot reopen a completed task");
        dashboardVm.SelectedFilter = "Completed";
        Check(dashboardVm.Todos.Count == 1 && widgetVm.Todos.Count == 0, "Completion updates all task lists");
        reminder.ToggleComplete(stale);
        Check(db.GetTodo(task.Id)!.Status == "Not Started", "Completed task can be reopened");
        var fresh = db.GetTodo(task.Id)!;
        fresh.Notes = "Catatan terbaru"; db.SaveTodo(fresh);
        reminder.SnoozeTodo(stale, 15);
        Check(db.GetTodo(task.Id)!.Notes == "Catatan terbaru", "Snooze preserves newer task edits");
        var now = DateTime.Now;
        var scheduled = new Todo { NotificationTime = now.AddMinutes(-1), SnoozeUntil = now.AddMinutes(10) };
        Check(!ReminderService.IsDue(scheduled, now), "Snooze blocks reminders until selected time");
        scheduled.SnoozeUntil = now.AddSeconds(-1);
        Check(ReminderService.IsDue(scheduled, now), "Expired snooze becomes due");
        scheduled.SnoozeUntil = null; scheduled.LastRemindedAt = now;
        Check(!ReminderService.IsDue(scheduled, now.AddMinutes(1)) && ReminderService.IsDue(scheduled, now.AddMinutes(31)), "Dismissed reminder waits 30 minutes");
        scheduled.NotificationTime = null;
        Check(!ReminderService.IsDue(scheduled, now.AddHours(2)), "Disabled reminder remains silent");

        var backup = Path.Combine(output, "backup-" + Guid.NewGuid().ToString("N") + ".db");
        db.BackupDatabase(backup);
        var invalid = Path.Combine(output, "invalid-" + Guid.NewGuid().ToString("N") + ".db");
        File.WriteAllText(invalid, "not a SQLite file");
        bool rejected = false;
        try { db.RestoreDatabase(invalid); } catch { rejected = true; }
        Check(rejected && db.GetTodo(task.Id)!.Notes == "Catatan terbaru", "Invalid restore preserves current database");
        db.SaveTodo(new Todo { Title = "Added after backup" });
        db.RestoreDatabase(backup); reminder.NotifyDatabaseChanged();
        Check(db.GetActiveTodos().Count == 1, "Valid restore returns to saved snapshot");
        bool selfRejected = false;
        try { db.RestoreDatabase(Path.Combine(data, "todo.db")); } catch (InvalidOperationException) { selfRejected = true; }
        Check(selfRejected, "Restore cannot target active database itself");

        // Validate an upgrade from the original settings schema.
        var oldDir = Path.Combine(output, "legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(oldDir);
        using (var conn = new SqliteConnection("Data Source=" + Path.Combine(oldDir, "todo.db")))
        {
            conn.Open();
            using var command = conn.CreateCommand();
            command.CommandText = "CREATE TABLE settings(id INTEGER PRIMARY KEY,startup INTEGER,always_on_top INTEGER,theme TEXT,volume REAL,default_sound TEXT,draft_title TEXT,draft_priority TEXT); INSERT INTO settings VALUES(1,0,0,'Dark',0.5,'Assets/default_alarm.mp3','Draft lama','Medium');";
            command.ExecuteNonQuery();
        }
        var migrated = new DatabaseService(oldDir).LoadSettings();
        Check(migrated.DraftTitle == "Draft lama" && migrated.DraftUseDeadline, "Schema upgrade preserves old drafts");

        foreach (var sample in new[] { (-1,"Heavy"), (0,"Heavy"), (7,"Heavy"), (8,"Medium"), (14,"Medium"), (15,"Light"), (30,"Light") })
            Check(Todo.CategoryFor(DateTime.Today.AddDays(sample.Item1), DateTime.Today) == sample.Item2, $"Category boundary H+{sample.Item1}");
        var future = DateTime.Today.AddDays(30);
        Check(Todo.CategoryFor(future, DateTime.Today.AddDays(16)) == "Medium" && Todo.CategoryFor(future, DateTime.Today.AddDays(23)) == "Heavy", "Category advances as calendar days pass");
        editor.Reset(); editor.Title = "Manual override"; editor.Priority = "Heavy"; editor.Date = DateTime.Today.AddDays(8);
        var before = db.GetActiveTodos().Count;
        editor.SaveCommand.Execute(null);
        Check(editor.ConfirmationRequired && db.GetActiveTodos().Count == before, "Mismatched date requests confirmation before writing");
        editor.BackCommand.Execute(null);
        Check(!editor.ConfirmationRequired && editor.Title == "Manual override", "Back preserves editor contents");
        editor.SaveCommand.Execute(null); editor.KeepPriorityCommand.Execute(null);
        var manual = db.GetActiveTodos().Single(t => t.Title == "Manual override");
        Check(manual.ManualPriority && manual.EffectivePriority == "Heavy" && new DatabaseService(data).GetTodo(manual.Id)!.ManualPriority, "Manual category persists across reload");
        editor.Edit(manual); editor.SaveCommand.Execute(null); editor.AutoPriorityCommand.Execute(null);
        var automatic = db.GetTodo(manual.Id)!;
        Check(!automatic.ManualPriority && automatic.EffectivePriority == "Medium", "Automatic choice removes manual override without changing date");
        automatic.Deadline = DateTime.Today.AddDays(7); db.SaveTodo(automatic); reminder.NotifyDatabaseChanged();
        dashboardVm.SelectedFilter = "Heavy";
        Check(dashboardVm.Todos.Any(t => t.Id == manual.Id), "Filters use current automatic category");
        db.DeleteTodo(manual.Id); reminder.NotifyDatabaseChanged();

        db.SaveTodo(new Todo { Title = "Revisi proposal skripsi", Notes = "Rapikan rumusan masalah dan catatan pembimbing.", Priority = "Heavy", Deadline = DateTime.Today.AddDays(7).AddHours(17) });
        db.SaveTodo(new Todo { Title = "Selesaikan fitur autentikasi MERN", Notes = "Login, validasi form, dan penanganan error.", Priority = "Medium", Deadline = DateTime.Today.AddDays(14).AddHours(20) });
        db.SaveTodo(new Todo { Title = "Tugas tanpa tanggal", Notes = "Tetap terlihat pada tampilan utama.", Priority = "Light" });
        reminder.NotifyDatabaseChanged();
        Check(dashboardVm.HeavyCount == 1 && dashboardVm.MediumCount == 1 && dashboardVm.LightCount == 2 && dashboardVm.ActiveCount == 4, "Sidebar counts all active tasks by current category");
        dashboardVm.SearchQuery = "tidak ditemukan";
        Check(dashboardVm.HeavyCount == 1 && dashboardVm.ActiveCount == 4, "Sidebar counts remain totals when searching");
        dashboardVm.SearchQuery = "";
        var dashboard = new DashboardWindow(db, reminder);
        var widget = new MainWindow(db, reminder);
        var renderedWidgetVm = (WidgetViewModel)widget.DataContext;
        renderedWidgetVm.Editor.Reset();
        Render(widget, 410, 680, Path.Combine(output, "widget.png"));
        var baseList = Descendants((DependencyObject)widget.Content).OfType<ListView>().Single();
        var baseY = baseList.TranslatePoint(new Point(), (UIElement)widget.Content).Y;
        renderedWidgetVm.Editor.Title = "Siapkan materi livestream";
        renderedWidgetVm.Editor.Notes = "Cek rundown, audio, dan materi pembicara.";
        renderedWidgetVm.Editor.IsExpanded = true;
        Render(widget, 410, 680, Path.Combine(output, "widget-expanded.png"));
        Check(Math.Abs(baseList.TranslatePoint(new Point(), (UIElement)widget.Content).Y - baseY) < 1, "Opening overlay does not move task list");
        Render(widget, 360, 480, Path.Combine(output, "widget-small.png"));
        var smallEditor = Descendants((DependencyObject)widget.Content).OfType<TaskEditor>().Single();
        Check(smallEditor.ActualWidth <= 336 && smallEditor.ActualHeight <= 456, "Overlay fits minimum widget size");
        Render(dashboard, 964, 700, Path.Combine(output, "dashboard.png"));
        var renderedDashboardVm = (DashboardViewModel)dashboard.DataContext;
        renderedDashboardVm.Editor.IsExpanded = true;
        Render(dashboard, 964, 700, Path.Combine(output, "dashboard-expanded.png"));
        var combos = Descendants((DependencyObject)dashboard.Content).OfType<ComboBox>().ToList();
        Check(combos.Any(c => c.Items.Count == 3 && c.SelectedValue?.ToString() == "Heavy"), "Priority dropdown has three presets with selected default");
        var picker = Descendants((DependencyObject)dashboard.Content).OfType<TaskEditor>().SelectMany(Descendants).OfType<DatePicker>().Single();
        Check(picker.SelectedDate == DateTime.Today.AddDays(7), "Date picker binds to selected deadline");
        picker.SelectedDate = DateTime.Today.AddDays(6);
        Drain();
        Check(renderedDashboardVm.Editor.Date == DateTime.Today.AddDays(6), "Date picker updates editor");
        renderedDashboardVm.Editor.IsExpanded = false;
        var deleteTarget = db.GetActiveTodos().First();
        renderedDashboardVm.DeleteTodoCommand.Execute(deleteTarget);
        Check(renderedDashboardVm.IsConfirmationOpen && db.GetTodo(deleteTarget.Id) != null, "Delete asks before changing data");
        Render(dashboard, 964, 700, Path.Combine(output, "delete-dashboard.png"));
        renderedDashboardVm.CancelConfirmationCommand.Execute(null);
        Check(!renderedDashboardVm.IsConfirmationOpen && db.GetTodo(deleteTarget.Id) != null, "Cancel deletion preserves task");
        renderedWidgetVm.Editor.IsExpanded = false;
        renderedWidgetVm.DeleteTodoCommand.Execute(deleteTarget);
        Render(widget, 360, 480, Path.Combine(output, "delete-widget-small.png"));
        var deleteOverlay = Descendants((DependencyObject)widget.Content).OfType<ConfirmationOverlay>().Single();
        Check(deleteOverlay.ActualWidth <= 360 && deleteOverlay.ActualHeight <= 480, "Delete overlay stays inside minimum widget bounds");
        renderedWidgetVm.CancelConfirmationCommand.Execute(null);
        renderedDashboardVm.DeleteTodoCommand.Execute(deleteTarget);
        renderedDashboardVm.ConfirmActionCommand.Execute(null);
        Check(db.GetTodo(deleteTarget.Id) == null && !renderedDashboardVm.IsConfirmationOpen && !renderedWidgetVm.Todos.Any(t => t.Id == deleteTarget.Id), "Confirm deletes task and refreshes both views");
        renderedDashboardVm.SelectedFilter = "Settings";
        Render(dashboard, 964, 700, Path.Combine(output, "settings.png"));
        var notification = new NotificationWindow(db.GetActiveTodos().First(), reminder);
        Render(notification, 420, 350, Path.Combine(output, "notification.png"));

        Check(System.Windows.Shell.WindowChrome.GetWindowChrome(dashboard)?.CaptionHeight == 42 && dashboard.WindowStyle == WindowStyle.None,
            "Dashboard uses native WindowChrome resize and caption behavior");
        Check(db.LoadSettings().Language == "id", "Existing databases default to Indonesian");
        renderedDashboardVm.Editor.Priority = "Medium";
        renderedDashboardVm.Editor.Date = DateTime.Today.AddDays(10);
        renderedDashboardVm.Editor.ReminderMinutes = 30;
        renderedDashboardVm.Language = "en";
        Drain();
        Check(new DatabaseService(data).LoadSettings().Language == "en", "Language selection persists after reconnect");
        Check(renderedWidgetVm.SectionTitle == "All tasks" && Localizer.Text("Batal") == "Cancel", "Language updates shared widget and labels");
        Check(renderedDashboardVm.Editor.Priority == "Medium" && renderedDashboardVm.Editor.Date == DateTime.Today.AddDays(10) && renderedDashboardVm.Editor.ReminderMinutes == 30,
            "Changing language preserves editor category, deadline and reminder");
        Check(renderedDashboardVm.Editor.PriorityOptions[1].Label.Contains("days"), "Priority options use selected language");
        Render(dashboard, 780, 560, Path.Combine(output, "settings-english-small.png"));
        Check(Descendants((DependencyObject)dashboard.Content).OfType<TextBlock>().Any(t => t.Text == "Application preferences"), "English text bindings update live");
        renderedDashboardVm.SelectedFilter = "All";
        Render(dashboard, 1080, 740, Path.Combine(output, "dashboard-english.png"));
        renderedDashboardVm.Editor.IsExpanded = true;
        Render(dashboard, 780, 560, Path.Combine(output, "editor-english-small.png"));
        Check(renderedDashboardVm.Editor.Priority == "Medium" && renderedDashboardVm.Editor.ReminderMinutes == 30, "Opening translated editor preserves selections");
        Check(Descendants((DependencyObject)dashboard.Content).OfType<ComboBox>().Any(c => c.SelectedValuePath == "Minutes" && c.SelectedValue is 30), "Translated reminder dropdown keeps visible selection");
        renderedDashboardVm.Editor.CancelCommand.Execute(null);
        renderedDashboardVm.Language = "id";
        Render(dashboard, 780, 560, Path.Combine(output, "dashboard-small.png"));
        var compactRow = Descendants((DependencyObject)dashboard.Content).OfType<TaskRowView>().First();
        Check(Grid.GetRow((UIElement)compactRow.FindName("DatePanel")) == 1, "Narrow rows place deadline beneath title");
        Render(dashboard, 1080, 740, Path.Combine(output, "dashboard-final.png"));
        var wideRow = Descendants((DependencyObject)dashboard.Content).OfType<TaskRowView>().First();
        Check(Grid.GetRow((UIElement)wideRow.FindName("DatePanel")) == 0, "Wide rows show deadline in a separate column");
        var workTarget = db.GetActiveTodos().First();
        renderedDashboardVm.StartTodoCommand.Execute(workTarget);
        Check(db.GetTodo(workTarget.Id)?.Status == "In Progress" && renderedDashboardVm.HasToast, "Start-working action saves status and shows feedback");
        renderedDashboardVm.SearchQuery = "no-match";
        renderedDashboardVm.ClearSearchCommand.Execute(null);
        Check(renderedDashboardVm.Todos.Count == db.GetActiveTodos().Count, "Clear search restores matching list");
        Render(widget, 360, 480, Path.Combine(output, "widget-final-small.png"));
        Render(notification, 420, 370, Path.Combine(output, "notification-final.png"));

        var calendar = renderedDashboardVm.Calendar;
        var leap = new Todo { Title = "Leap day", Deadline = new DateTime(2028,2,29,9,30,0), NotificationTime = new DateTime(2028,2,29,9,15,0) };
        db.SaveTodo(leap); reminder.NotifyDatabaseChanged();
        calendar.ShowMonth(new DateTime(2028,2,1));
        Check(calendar.Days.Count == 42 && calendar.Days[0].Date.DayOfWeek == DayOfWeek.Sunday, "Calendar has six complete weeks beginning Sunday");
        Check(calendar.Days.Single(d => d.Date == leap.Deadline.Value.Date).Tasks.Any(t => t.Id == leap.Id), "Leap-day deadline appears on correct date");
        Check(!calendar.Days.SelectMany(d => d.Tasks).Any(t => t.Deadline == null), "Undated tasks are excluded from calendar");
        calendar.ShowMonth(new DateTime(2027,12,1)); calendar.NextCommand.Execute(null);
        Check(calendar.Month == new DateTime(2028,1,1), "Calendar navigates across year boundary");
        calendar.PreviousCommand.Execute(null);
        Check(calendar.Month == new DateTime(2027,12,1), "Calendar previous month reverses year boundary");
        calendar.TodayCommand.Execute(null);
        Check(calendar.Days.Single(d => d.Date == DateTime.Today).IsToday, "Today shortcut and marker follow local date");
        leap.Deadline = DateTime.Today.AddDays(2).AddHours(9); db.SaveTodo(leap); reminder.NotifyDatabaseChanged();
        Check(calendar.Days.Single(d => d.Date == leap.Deadline.Value.Date).Tasks.Any(t => t.Id == leap.Id), "Editing deadline moves task to new calendar day");
        reminder.CompleteTodo(leap);
        Check(calendar.Days.SelectMany(d => d.Tasks).Single(t => t.Id == leap.Id).IsCompleted, "Completed tasks remain visible with completed state");
        renderedDashboardVm.EditTodoCommand.Execute(db.GetTodo(leap.Id));
        Check(renderedDashboardVm.Editor.IsEditing && renderedDashboardVm.Editor.Title == "Leap day", "Calendar edit command opens existing task");
        renderedDashboardVm.Editor.CancelCommand.Execute(null);
        var calendarBackup = Path.Combine(output, "calendar-backup.db"); db.BackupDatabase(calendarBackup);
        db.DeleteTodo(leap.Id); reminder.NotifyDatabaseChanged();
        Check(!calendar.Days.SelectMany(d => d.Tasks).Any(t => t.Id == leap.Id), "Deleted tasks leave calendar immediately");
        db.RestoreDatabase(calendarBackup); reminder.NotifyDatabaseChanged();
        Check(calendar.Days.SelectMany(d => d.Tasks).Any(t => t.Id == leap.Id), "Calendar updates after restoring backup");
        renderedDashboardVm.SelectedFilter = "Calendar";
        Check(!renderedDashboardVm.IsTaskView && renderedDashboardVm.IsCalendar, "Calendar navigation hides list view");
        Render(dashboard,1080,740,Path.Combine(output,"calendar.png"));
        Render(dashboard,780,560,Path.Combine(output,"calendar-small.png"));
        var calendarView = Descendants((DependencyObject)dashboard.Content).OfType<CalendarView>().Single();
        var calendarScroll = (ScrollViewer)calendarView.FindName("CalendarScroll");
        var horizontal = (System.Windows.Controls.Primitives.ScrollBar)calendarScroll.Template.FindName("PART_HorizontalScrollBar", calendarScroll);
        Check(horizontal.ActualWidth > 200 && horizontal.ActualHeight >= 10, $"Calendar horizontal scrollbar spans narrow viewport ({horizontal.ActualWidth} x {horizontal.ActualHeight})");
        calendarScroll.ScrollToRightEnd(); Drain();
        Check(calendarScroll.HorizontalOffset > 0, "Narrow calendar can scroll to weekend columns");
        calendarScroll.ScrollToLeftEnd();
        renderedDashboardVm.Language = "en";
        Check(calendar.Weekdays[0] == "Sun" && renderedDashboardVm.SectionTitle == "Calendar", "Calendar follows selected language");
        Render(dashboard,1080,740,Path.Combine(output,"calendar-english.png"));
        renderedDashboardVm.Language = "id";
        var beforePreview = System.Text.Json.JsonSerializer.Serialize(db.GetActiveTodos().Concat(db.GetCompletedTodos()).OrderBy(t => t.Id));
        var preview = new NotificationWindow(db.GetTodo(leap.Id)!, reminder, isPreview: true);
        var previewVm = (NotificationViewModel)preview.DataContext;
        previewVm.StartWorkingCommand.Execute(null);
        previewVm.CompleteCommand.Execute(null);
        previewVm.SnoozeCommand.Execute("30");
        previewVm.MuteCommand.Execute(null);
        Check(previewVm.IsPreview && previewVm.PreviewFeedback.Contains("30"), "Preview actions display simulated feedback");
        Check(beforePreview == System.Text.Json.JsonSerializer.Serialize(db.GetActiveTodos().Concat(db.GetCompletedTodos()).OrderBy(t => t.Id)), "Preview never changes task data or reminder timestamps");
        var samplePreview = renderedDashboardVm.CreateReminderPreview();
        Check(((NotificationViewModel)samplePreview.DataContext).Todo.Id == 0, "Settings preview uses separate example task");
        Render(samplePreview,420,435,Path.Combine(output,"reminder-preview.png"));
        preview.Close(); samplePreview.Close();
        using (var soundTest = new SoundTestService())
        {
            bool missingRejected = false;
            try { soundTest.Play(Path.Combine(output,"missing.mp3"),0); } catch (FileNotFoundException) { missingRejected = true; }
            Check(missingRejected && !soundTest.IsPlaying, "Sound test reports missing audio safely");
            soundTest.Play("Assets/default_alarm.mp3",0);
            Check(soundTest.IsPlaying, "Sound test opens bundled audio using independent player");
            soundTest.Stop();
            Check(!soundTest.IsPlaying, "Sound test stops on request");
        }
        Check(beforePreview == System.Text.Json.JsonSerializer.Serialize(db.GetActiveTodos().Concat(db.GetCompletedTodos()).OrderBy(t => t.Id)), "Sound test does not modify reminder data");
        renderedDashboardVm.SelectedFilter = "Settings";
        Render(dashboard,1080,740,Path.Combine(output,"settings-final.png"));
        var settingsView = Descendants((DependencyObject)dashboard.Content).OfType<SettingsView>().Single();
        var soundCard = (Border)settingsView.FindName("SoundCard");
        Check(Grid.GetColumn(soundCard) == 1 && Grid.GetRow(soundCard) == 0, "Settings uses two cards per row at full width");
        Render(dashboard,780,560,Path.Combine(output,"settings-small.png"));
        Check(Grid.GetColumn(soundCard) == 0 && Grid.GetRow(soundCard) == 1, "Settings stacks cards at minimum width");
        Check(renderedDashboardVm.DataLocation.EndsWith("todo.db") && dashboard.Title.StartsWith("G-list"), "Renamed app shows existing database location");
        renderedDashboardVm.SelectedFilter = "All";
        Render(dashboard,1080,740,Path.Combine(output,"dashboard-final.png"));

        dashboard.Show(); Drain();
        Check(dashboard.IsVisible && dashboard.ActualWidth >= dashboard.MinWidth, "Native dashboard opens with isolated test data");
        var nativeRow = Descendants((DependencyObject)dashboard.Content).OfType<TaskRowView>().First();
        var moreButton = Descendants(nativeRow).OfType<Button>().Single(b => b.ContextMenu != null);
        moreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain();
        Check(moreButton.ContextMenu.IsOpen, "Task action menu opens inside native dashboard");
        var menuSurface = (Border)moreButton.ContextMenu.Template.FindName("MenuSurface", moreButton.ContextMenu);
        Check(menuSurface != null && menuSurface.Background != null, "Task menu uses a complete themed surface without Windows gutter");
        RenderElement(moreButton.ContextMenu, Path.Combine(output, "task-menu.png"));
        dashboard.Left += 12; Drain();
        Check(!moreButton.ContextMenu.IsOpen, "Moving owner dismisses task menu");
        moreButton.ContextMenu.IsOpen = false;
        var maximize = (Button)dashboard.FindName("MaximizeButton");
        maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WaitUntil(() => dashboard.WindowState == WindowState.Maximized);
        Check(dashboard.WindowState == WindowState.Maximized, "Native maximize command works");
        maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WaitUntil(() => dashboard.WindowState == WindowState.Normal);
        Check(dashboard.WindowState == WindowState.Normal, "Native restore command works");
        dashboard.Close(); notification.Close(); Drain();

        // Weekly schedules use isolated storage and deterministic dates for reminder checks.
        var scheduleDb = new DatabaseService(Path.Combine(output,"schedules-"+Guid.NewGuid().ToString("N")));
        var scheduleReminder = new ReminderService(scheduleDb,startTimers:false);
        using var scheduleDashboard = new DashboardViewModel(scheduleDb,scheduleReminder);
        using var scheduleWidget = new WidgetViewModel(scheduleDb,scheduleReminder);
        var scheduleForm = scheduleDashboard.Schedules;
        Check(!scheduleForm.HasDay, "Schedule requires choosing a weekday first");
        Check(scheduleForm.Days.Select(d=>d.Value).SequenceEqual(Enumerable.Range(0,7)),"Schedule day buttons run Sunday through Saturday");
        scheduleForm.SaveCommand.Execute(null);
        Check(scheduleForm.Error.Length>0 && scheduleDb.GetSchedules().Count==0,"Missing schedule name/day rejected");
        scheduleForm.SelectedDay=(int)DateTime.Today.DayOfWeek;
        scheduleForm.Title="Kelas Software Engineering"; scheduleForm.Notes="Ruang B-204 · bawa laptop";
        scheduleForm.StartTime="09:00";scheduleForm.EndTime="08:00";
        scheduleForm.SaveCommand.Execute(null);
        Check(scheduleForm.Error.Length>0 && scheduleDb.GetSchedules().Count==0,"Reversed schedule hours rejected");
        scheduleForm.EndTime="10:30";scheduleForm.ReminderMinutes=30;
        scheduleForm.SaveCommand.Execute(null);
        var weekly=scheduleDb.GetSchedules().Single();
        Check(weekly.StartDate==null && weekly.EndDate==null && weekly.Notes.Contains("B-204"),"Optional bounds allow indefinite weekly schedule");
        Check(weekly.Between(DateTime.Today,DateTime.Today.AddDays(28)).Count()==5,"Recurrence repeats every seven days including both bounds");
        Check(scheduleDashboard.Calendar.Days.SelectMany(d=>d.Schedules).Any(o=>o.Schedule.Id==weekly.Id),"New schedule immediately appears in calendar");
        Check(scheduleWidget.UpcomingSchedules.Count==1 && scheduleWidget.UpcomingSchedules.All(o=>o.Start.Date==DateTime.Today),"Widget shows only today's occurrences, including earlier hours");
        var occurrence=weekly.Between(DateTime.Today,DateTime.Today).Single();
        Check(!scheduleDb.TryClaimScheduleReminder(occurrence,occurrence.ReminderAt.AddSeconds(-1)),"Reminder does not fire early");
        Check(scheduleDb.TryClaimScheduleReminder(occurrence,occurrence.ReminderAt),"Reminder fires at chosen offset before start");
        Check(!new DatabaseService(scheduleDb.DataDirectory).TryClaimScheduleReminder(occurrence,occurrence.Start),"Delivered occurrence stays deduplicated after restart");
        Check(scheduleDb.TryClaimScheduleReminder(weekly.Between(DateTime.Today.AddDays(7),DateTime.Today.AddDays(7)).Single(),occurrence.ReminderAt.AddDays(7)),"Following week has its own reminder delivery");
        var backupSchedules=Path.Combine(output,"schedule-backup.db");scheduleDb.BackupDatabase(backupSchedules);
        scheduleForm.Edit(weekly);scheduleForm.SelectedDay=((int)weekly.Weekday+1)%7;
        scheduleForm.StartTime="00:10";scheduleForm.EndTime="01:00";scheduleForm.SaveCommand.Execute(null);
        var moved=scheduleDb.GetSchedule(weekly.Id)!;
        Check(scheduleWidget.HasNoSchedules,"Widget is empty when all schedules belong to another weekday");
        Check(moved.Revision==2 && moved.Weekday!=weekly.Weekday,"Editing moves weekly series to new day and increments revision");
        Check(!scheduleDb.TryClaimScheduleReminder(occurrence,occurrence.ReminderAt),"Stale occurrence cannot remind after editing");
        var overnight=moved.Next(DateTime.Today)!;
        Check(overnight.ReminderAt.Date==overnight.Start.Date.AddDays(-1),"Reminder can cross midnight into previous day");
        Check(scheduleReminder.GetDueSchedules(overnight.ReminderAt).Any(o=>o.Schedule.Id==moved.Id),"Previous-day reminder is discovered");
        Check(!scheduleDb.TryClaimScheduleReminder(overnight,overnight.End),"Ended activity is not reminded late");
        scheduleDb.ArchiveSchedule(moved.Id,DateTime.Now);scheduleReminder.NotifyDatabaseChanged();
        scheduleDashboard.SelectedFilter="Completed";
        Check(scheduleDb.GetSchedules().Count==1 && scheduleDashboard.DisplayItems.OfType<WeeklySchedule>().Count()==1,"Ending schedule retains data and moves it to Completed");
        Check(scheduleDashboard.Schedules.ActiveCount==0 && scheduleWidget.UpcomingSchedules.Count==0,"Archived schedule disappears from active list and widget");
        Check(!scheduleDb.TryClaimScheduleReminder(overnight,overnight.ReminderAt),"Archive blocks queued reminders");
        Check(scheduleDb.GetSchedule(moved.Id)!.EffectiveEnd==DateTime.Today,"Archived range retains actual end date");
        scheduleDb.RestoreDatabase(backupSchedules);scheduleReminder.NotifyDatabaseChanged();
        Check(scheduleDb.GetSchedule(weekly.Id)!.ArchivedAt==null && scheduleDb.WasScheduleReminded(occurrence),"Backup restores schedules and reminder delivery history");
        var bounded=new WeeklySchedule {Title="Semester",Weekday=DayOfWeek.Monday,StartMinute=480,EndMinute=540,StartDate=new DateTime(2026,9,7),EndDate=new DateTime(2026,9,21)};
        Check(bounded.Between(new DateTime(2026,9,1),new DateTime(2026,10,1)).Count()==3,"Date bounds are inclusive and prevent extra weeks");
        Check(!bounded.IsCompletedAt(new DateTime(2026,9,21)) && bounded.IsCompletedAt(new DateTime(2026,9,22)),"Date-limited schedule completes after its last date");
        var far=new WeeklySchedule {Title="Future",Weekday=DayOfWeek.Monday,StartMinute=480,EndMinute=540,StartDate=DateTime.Today.AddYears(2)};
        Check(far.Next(DateTime.Today)!.Start>=far.StartDate,"Future start has no one-month recurrence cutoff");
        far.StartDate=DateTime.MaxValue.Date; Check(far.Next(DateTime.MaxValue.Date)==null,"Maximum date safely handles missing weekday");
        scheduleDashboard.SelectedFilter="Schedule";scheduleForm.Edit(scheduleDb.GetSchedule(weekly.Id)!);
        var scheduleWindow=new DashboardWindow(scheduleDb,scheduleReminder);
        ((DashboardViewModel)scheduleWindow.DataContext).Dispose();scheduleWindow.DataContext=scheduleDashboard;
        Render(scheduleWindow,1080,740,Path.Combine(output,"schedule-wide.png"));
        Render(scheduleWindow,780,620,Path.Combine(output,"schedule-small.png"));
        scheduleDashboard.SelectedFilter="Calendar";
        Render(scheduleWindow,1080,740,Path.Combine(output,"schedule-calendar.png"));
        scheduleDashboard.OpenCalendarEntryCommand.Execute(new CalendarEntry(null,occurrence));
        Check(scheduleDashboard.IsSchedule && scheduleForm.IsEditing,"Calendar schedule opens series editor");
        scheduleWidget.WidgetMode="Schedule";
        var scheduleWidgetWindow=new MainWindow(scheduleDb,scheduleReminder);
        ((WidgetViewModel)scheduleWidgetWindow.DataContext).Dispose();scheduleWidgetWindow.DataContext=scheduleWidget;
        Render(scheduleWidgetWindow,400,620,Path.Combine(output,"schedule-widget.png"));
        var schedulePopup=new ScheduleNotificationWindow(occurrence,scheduleReminder);
        Render(schedulePopup,430,310,Path.Combine(output,"schedule-reminder.png"));
        Localizer.Instance.SetLanguage("en");Drain();
        Check(scheduleForm.ReminderMinutes==30 && scheduleForm.ReminderOptions.First().Label=="At activity start","Schedule language change preserves reminder selection");
        Render(scheduleWindow,1080,740,Path.Combine(output,"schedule-english.png"));
        Localizer.Instance.SetLanguage("id");
        scheduleDb.ArchiveSchedule(weekly.Id,DateTime.Now);scheduleReminder.NotifyDatabaseChanged();
        scheduleDashboard.SelectedFilter="Completed";
        Render(scheduleWindow,1080,740,Path.Combine(output,"schedule-completed.png"));
        var disabled=new WeeklySchedule {Title="Silent",Weekday=DateTime.Today.DayOfWeek,StartMinute=600,EndMinute=660,ReminderMinutes=-1,StartDate=DateTime.Today};
        scheduleDb.SaveSchedule(disabled);
        Check(!scheduleDb.TryClaimScheduleReminder(disabled.Between(DateTime.Today,DateTime.Today).Single(),DateTime.Today.AddHours(10)),"No-reminder schedule never claims notification");
        scheduleDb.BackupDatabase(backupSchedules);
        using(var legacy=new SqliteConnection("Data Source="+backupSchedules))
        { legacy.Open();using var remove=new SqliteCommand("DROP TABLE schedules; DROP TABLE schedule_deliveries;",legacy);remove.ExecuteNonQuery(); }
        scheduleDb.RestoreDatabase(backupSchedules);
        Check(scheduleDb.GetSchedules().Count==0,"Restoring pre-Schedule backup safely recreates schedule tables");
        schedulePopup.Close();scheduleWidgetWindow.Close();scheduleWindow.Close();scheduleReminder.Shutdown();

        var dropdownDb=new DatabaseService(Path.Combine(output,"dropdown-"+Guid.NewGuid().ToString("N")));
        var dropdownReminder=new ReminderService(dropdownDb,startTimers:false);
        var taskForm=new TaskEditorViewModel(dropdownDb,dropdownReminder) {Title="Dropdown regression",IsExpanded=true};
        var taskControl=new TaskEditor {DataContext=taskForm};
        var dropdownWindow=new Window {Content=taskControl,Width=540,Height=760};
        dropdownWindow.Show();Drain();
        var taskDropdown=Descendants(taskControl).OfType<ComboBox>().Single(c=>c.SelectedValuePath=="Minutes");
        foreach(var minutes in new[]{15,30,60,-1,0,30})
        { SelectReminder(taskDropdown,minutes);Check(taskForm.ReminderMinutes==minutes,"Task dropdown selection updates model: "+minutes); }
        taskForm.SaveCommand.Execute(null);
        var dropdownTask=dropdownDb.GetActiveTodos().Single();
        Check(dropdownTask.NotificationTime==dropdownTask.Deadline!.Value.AddMinutes(-30),"New task persists reminder chosen through dropdown");
        taskForm.Edit(dropdownTask);SelectReminder(taskDropdown,60);taskForm.SaveCommand.Execute(null);
        Check(dropdownDb.GetTodo(dropdownTask.Id)!.NotificationTime==dropdownTask.Deadline!.Value.AddMinutes(-60),"Edited task persists changed dropdown reminder");
        dropdownWindow.Close();
        using var weeklyForm=new ScheduleViewModel(dropdownDb,dropdownReminder,_=>{},_=>{});
        weeklyForm.SelectedDay=(int)DateTime.Today.DayOfWeek;weeklyForm.Title="Schedule dropdown regression";
        var weeklyControl=new ScheduleView {DataContext=weeklyForm};
        var weeklyWindow=new Window {Content=weeklyControl,Width=900,Height=760};weeklyWindow.Show();Drain();
        var weeklyDropdown=Descendants(weeklyControl).OfType<ComboBox>().Single();
        foreach(var minutes in new[]{30,60,-1,0,15})
        { SelectReminder(weeklyDropdown,minutes);Check(weeklyForm.ReminderMinutes==minutes,"Schedule dropdown selection updates model: "+minutes); }
        weeklyForm.SaveCommand.Execute(null);
        var dropdownSchedule=dropdownDb.GetSchedules().Single();
        Check(dropdownSchedule.ReminderMinutes==15,"New schedule persists dropdown reminder");
        weeklyForm.Edit(dropdownSchedule);SelectReminder(weeklyDropdown,60);
        Localizer.Instance.SetLanguage("en");Drain();SelectReminder(weeklyDropdown,30);
        weeklyForm.SaveCommand.Execute(null);
        Check(dropdownDb.GetSchedule(dropdownSchedule.Id)!.ReminderMinutes==30,"Edited schedule persists selection after language switch");
        using var todayWidget=new WidgetViewModel(dropdownDb,dropdownReminder);
        var laterSchedule=dropdownDb.GetSchedule(dropdownSchedule.Id)!;
        laterSchedule.StartDate=DateTime.Today.AddDays(7);dropdownDb.SaveSchedule(laterSchedule);dropdownReminder.NotifyDatabaseChanged();
        Check(todayWidget.HasNoSchedules,"Widget excludes same-weekday schedules whose date range has not started");
        Localizer.Instance.SetLanguage("id");weeklyWindow.Close();dropdownReminder.Shutdown();
        var optionProbe=new ReminderOption(15,"");
        var originalHash=optionProbe.GetHashCode();
        optionProbe.PropertyChanged+=(_,_)=>{};
        Check(originalHash==optionProbe.GetHashCode(),"Reminder option identity stays stable when WPF subscribes to label updates");
        PresentationTraceSources.DataBindingSource.Flush();
        File.WriteAllText(Path.Combine(output, "binding-log.txt"), bindingLog.ToString());
        Check(!bindingLog.ToString().Contains("System.Windows.Data Error"), "Rendered views have no binding errors");
        renderedWidgetVm.Dispose(); renderedDashboardVm.Dispose();
        reminder.Shutdown();
        Console.WriteLine($"COMPLETE: {_assertions} checks passed. Output: {output}");
        File.WriteAllText(Path.Combine(output, "result.txt"), $"{_assertions} checks passed at {DateTime.Now:O}");
    }
    private static void SelectReminder(ComboBox dropdown,int minutes)
    {
        dropdown.BringIntoView();dropdown.IsDropDownOpen=true;Drain();
        var option=dropdown.Items.Cast<ReminderOption>().Single(o=>o.Minutes==minutes);
        var item=(ComboBoxItem)dropdown.ItemContainerGenerator.ContainerFromItem(option);
        item.IsSelected=true;
        dropdown.IsDropDownOpen=false;Drain();
        Check(Equals(dropdown.SelectedValue,minutes),"Dropdown UI accepts selection: "+minutes);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        yield return node;
        for (var i=0; i<VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(node,i))) yield return child;
    }
    private static void WaitUntil(Func<bool> ready)
    {
        var frame = new DispatcherFrame();
        var end = DateTime.UtcNow.AddSeconds(3);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => { if (ready() || DateTime.UtcNow >= end) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
    }
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void RenderElement(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96,96,PixelFormats.Pbgra32);
        bmp.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static void Render(Window window, int width, int height, string path)
    {
        var content = (FrameworkElement)window.Content;
        content.DataContext = window.DataContext;
        window.ApplyTemplate(); content.Measure(new Size(width,height)); content.Arrange(new Rect(0,0,width,height));
        content.UpdateLayout(); Drain(); content.UpdateLayout();
        var bmp = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        bmp.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path); encoder.Save(file);
    }
}

