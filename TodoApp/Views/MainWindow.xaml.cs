using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TodoApp.Services;
using TodoApp.ViewModels;
namespace TodoApp.Views;
public partial class MainWindow : Window
{
    public MainWindow(DatabaseService database, ReminderService reminder)
    {
        InitializeComponent(); DataContext = new WidgetViewModel(database, reminder);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 30);
        Top = Math.Max(SystemParameters.WorkArea.Top, Math.Min(40, SystemParameters.WorkArea.Bottom - Height));
    }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (Application.Current is App app && !app.IsExiting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
