using System.Windows;
using TodoApp.Models;
using TodoApp.Services;
namespace TodoApp.Views;
public partial class ScheduleNotificationWindow : Window
{
    private readonly ReminderService _reminder;
    public ScheduleNotificationWindow(ScheduleOccurrence occurrence, ReminderService reminder)
    {
        InitializeComponent(); DataContext=occurrence; _reminder=reminder;
        Left=SystemParameters.WorkArea.Right-Width-20; Top=SystemParameters.WorkArea.Bottom-Height-20;
    }
    private void Mute_Click(object sender,RoutedEventArgs e) => _reminder.StopSound();
    private void Close_Click(object sender,RoutedEventArgs e) => Close();
}
