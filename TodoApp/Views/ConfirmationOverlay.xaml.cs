using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TodoApp.ViewModels;
namespace TodoApp.Views;
public partial class ConfirmationOverlay : UserControl
{
    private IInputElement? _previousFocus;
    public ConfirmationOverlay() { InitializeComponent(); }
    private void Overlay_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _previousFocus = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) CancelButton.Focus(); }));
        }
        else if (_previousFocus is UIElement element && element.IsVisible && element.IsEnabled)
            Keyboard.Focus(element);
    }
    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is TaskListViewModel vm)
        { vm.CancelConfirmationCommand.Execute(null); e.Handled = true; }
    }
}
