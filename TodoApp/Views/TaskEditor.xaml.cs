using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TodoApp.ViewModels;
namespace TodoApp.Views;
public partial class TaskEditor : UserControl
{
    private IInputElement? _previousFocus;
    private void Editor_VisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _previousFocus = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(new System.Action(() => { if (IsVisible) { TitleInput.Focus(); TitleInput.CaretIndex = TitleInput.Text.Length; } }));
        }
        else if (_previousFocus is System.Windows.UIElement element && element.IsVisible && element.IsEnabled)
            Keyboard.Focus(element);
    }
    private void Confirmation_VisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Dispatcher.BeginInvoke(new System.Action(() => AutoChoice.Focus()));
        else if (IsVisible) Dispatcher.BeginInvoke(new System.Action(() => TitleInput.Focus()));
    }
    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is TaskEditorViewModel vm)
        { if (vm.ConfirmationRequired) vm.BackCommand.Execute(null); else vm.CancelCommand.Execute(null); e.Handled = true; }
    }
    public TaskEditor() { InitializeComponent(); }
    private void Title_Focus(object sender, KeyboardFocusChangedEventArgs e) { if (DataContext is TaskEditorViewModel vm) vm.IsExpanded = true; }
    private void Title_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && DataContext is TaskEditorViewModel vm) { vm.SaveCommand.Execute(null); e.Handled = true; } }
}
