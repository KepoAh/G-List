using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace TodoApp.Views;
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => Equals(value, parameter);
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => value is true ? parameter : Binding.DoNothing;
}
