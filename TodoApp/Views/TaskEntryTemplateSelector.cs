using System.Windows;
using System.Windows.Controls;
using TodoApp.Models;
namespace TodoApp.Views;
public sealed class TaskEntryTemplateSelector : DataTemplateSelector
{
    public override DataTemplate SelectTemplate(object item,DependencyObject container) =>
        (DataTemplate)Application.Current.FindResource(item is WeeklySchedule ? "ScheduleArchiveRow" : "TaskRow");
}
