using System.Windows.Data;
using System.Windows.Markup;
using TodoApp.Services;
namespace TodoApp.Views;
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T(string key) { Key = key; }
    public string Key { get; }
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding("[" + Key + "]") { Source = Localizer.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
