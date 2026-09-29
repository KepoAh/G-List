using System.Windows;
using System.Windows.Controls;
namespace TodoApp.Views;
public partial class ScheduleView : UserControl
{
    public ScheduleView() { InitializeComponent(); }
    private void View_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        bool wide=e.NewSize.Width>=760;
        DayContent.ColumnDefinitions[1].Width=wide?new GridLength(1,GridUnitType.Star):new GridLength(0);
        Grid.SetColumn(FormCard,wide?1:0);Grid.SetRow(FormCard,wide?0:1);
    }
}
