using System.Globalization;
using System.Windows.Data;
namespace TodoApp.Views;
public sealed class CalendarWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Math.Max(700, (value is double width && double.IsFinite(width) ? width : 700) - 18);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

