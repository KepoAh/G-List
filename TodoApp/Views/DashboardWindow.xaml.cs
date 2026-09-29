using System.Windows;
using TodoApp.Services;
using TodoApp.ViewModels;
namespace TodoApp.Views;
public partial class DashboardWindow : Window
{
    public DashboardWindow(DatabaseService database, ReminderService reminder)
    {
        InitializeComponent(); DataContext = new DashboardViewModel(database, reminder);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 30);
        StateChanged += (_, _) => {
            Shell.Margin = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0);
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        };
        Closed += (_, _) => ((DashboardViewModel)DataContext).Dispose();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
