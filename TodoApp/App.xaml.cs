using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using TodoApp.Services;
using TodoApp.Views;
using TodoApp.ViewModels;

namespace TodoApp;
public partial class App : Application
{
    private readonly bool _suppressStartup;
    public App() : this(false) { }
    // Used by the isolated UI harness; WPF queues startup when Dispatcher is pumped.
    public App(bool suppressStartup) { _suppressStartup = suppressStartup; }
    private static DatabaseService? _database;
    private static ReminderService? _reminder;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.Timer? _blink;
    private System.Drawing.Icon? _icon;
    private MainWindow? _widget;
    private DashboardWindow? _dashboard;
    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;
    public bool IsExiting { get; private set; }
    public static DatabaseService Database => _database ?? throw new InvalidOperationException("App not initialized");
    public static ReminderService Reminder => _reminder ?? throw new InvalidOperationException("App not initialized");

    protected override void OnStartup(StartupEventArgs e)
    {
        if (_suppressStartup) return;
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            string? dataDirectory = null;
            int dataIndex = Array.IndexOf(e.Args, "--data-dir");
            if (dataIndex >= 0 && dataIndex + 1 < e.Args.Length) dataDirectory = Path.GetFullPath(e.Args[dataIndex + 1]);
            // Preserve the shared identity and data path across the display-name change.
            var identity = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "G-tools");
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToUpperInvariant())))[..16];
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\GTools-Activate-" + key);
            _mutex = new Mutex(true, "Local\\GTools-" + key, out _ownsMutex);
            if (!_ownsMutex) { _activate.Set(); Shutdown(); return; }
            _activationWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => {
                Dispatcher.BeginInvoke(new Action(() => { if (!IsExiting) ShowWidget(); }));
            }, null, Timeout.Infinite, false);
            _database = new DatabaseService(dataDirectory);
            Localizer.Instance.SetLanguage(_database.LoadSettings().Language);
            _database.SettingsChanged += RefreshLanguage;
            _reminder = new ReminderService(_database, !e.Args.Contains("--no-reminders"));
            SetupTray();
            _widget = new MainWindow(_database, _reminder);
            if (!e.Args.Contains("--startup") && !e.Args.Contains("--minimized")) _widget.Show();
            if (dataDirectory == null && StartupService.IsStartupEnabled()) StartupService.SetStartup(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show("G-list belum dapat dibuka: " + ex.Message, "G-list", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private void SetupTray()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        _icon = File.Exists(path) ? new System.Drawing.Icon(path) : (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        _tray = new System.Windows.Forms.NotifyIcon { Icon = _icon, Visible = true, Text = "G-list" };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Buka widget", null, (_, _) => ShowWidget());
        menu.Items.Add("Buka dashboard", null, (_, _) => ShowDashboard());
        menu.Items.Add("Senyapkan pengingat", null, (_, _) => Reminder.StopSound());
        menu.Items.Add("-");
        menu.Items.Add("Keluar G-list", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        Localizer.Instance.Changed += RefreshTrayLanguage;
        RefreshTrayLanguage();
        _tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowWidget(); };
        bool flip = false;
        _blink = new System.Windows.Forms.Timer { Interval = 700 };
        _blink.Tick += (_, _) => { flip = !flip; if (_tray != null) _tray.Icon = flip ? System.Drawing.SystemIcons.Information : _icon; };
        Reminder.OnTrayBlinkStateChanged += enabled => {
            if (enabled) _blink.Start();
            else { _blink.Stop(); if (_tray != null) _tray.Icon = _icon; }
        };
    }
    private void RefreshLanguage() => Localizer.Instance.SetLanguage(Database.LoadSettings().Language);
    private void RefreshTrayLanguage()
    {
        if (_tray?.ContextMenuStrip is not { } menu) return;
        var keys = new[] { "Buka widget", "Buka dashboard", "Senyapkan pengingat", "-", "Keluar G-list" };
        for (int i = 0; i < keys.Length; i++) menu.Items[i].Text = Localizer.Text(keys[i]);
    }
    public void ShowWidget()
    {
        if (IsExiting) return;
        _widget ??= new MainWindow(Database, Reminder);
        _dashboard?.Hide();
        _widget.Show(); _widget.WindowState = WindowState.Normal; _widget.Activate();
    }
    public void ShowDashboard()
    {
        if (IsExiting) return;
        if (_dashboard == null)
        {
            _dashboard = new DashboardWindow(Database, Reminder);
            _dashboard.Closed += (_, _) => { _dashboard = null; if (!IsExiting) ShowWidget(); };
        }
        _dashboard.Show(); _dashboard.WindowState = WindowState.Normal; _dashboard.Activate(); _widget?.Hide();
    }
    public void ShowScheduleDashboard()
    {
        ShowDashboard();
        if(_dashboard?.DataContext is DashboardViewModel vm) vm.SelectedFilter="Schedule";
    }
    public void ExitApp() { IsExiting = true; Shutdown(); }
    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        Localizer.Instance.Changed -= RefreshTrayLanguage;
        if (_database != null) _database.SettingsChanged -= RefreshLanguage;
        (_widget?.DataContext as WidgetViewModel)?.Dispose();
        (_dashboard?.DataContext as DashboardViewModel)?.Dispose();
        _reminder?.Shutdown(); _blink?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _icon?.Dispose(); _activationWait?.Unregister(null); _activate?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
