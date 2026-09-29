using System.Windows;
using Point = System.Windows.Point;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TodoApp.Models;
using TodoApp.ViewModels;
namespace TodoApp.Views;
public partial class TaskRowView : UserControl
{
    public TaskRowView() { InitializeComponent(); Unloaded += (_, _) => { if (_menu != null) _menu.IsOpen = false; }; }
    private ContextMenu? _menu;
    private Window? _menuOwner;
    private void CloseMenu(object? sender, EventArgs e) { if (_menu != null) _menu.IsOpen = false; }
    private void Menu_Closed(object? sender, RoutedEventArgs e)
    {
        if (_menuOwner == null) return;
        _menuOwner.LocationChanged -= CloseMenu;
        _menuOwner.Deactivated -= CloseMenu;
        _menuOwner = null;
    }
    private void Row_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 600;
        DateColumn.Width = compact ? new GridLength(0) : new GridLength(136);
        PriorityColumn.Width = new GridLength(compact ? 64 : 76);
        Grid.SetRow(DatePanel, compact ? 1 : 0);
        Grid.SetColumn(DatePanel, compact ? 1 : 2);
        DatePanel.Margin = compact ? new Thickness(0,8,8,0) : new Thickness(0,0,8,0);
        if (_menu != null) _menu.IsOpen = false;
    }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        _menu = button.ContextMenu;
        if (_menu == null) return;
        _menu.PlacementTarget = button; _menu.Placement = PlacementMode.Custom;
        _menu.CustomPopupPlacementCallback = (popup, target, offset) => new[] {
            new CustomPopupPlacement(new Point(target.Width - popup.Width, target.Height + 6), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(target.Width - popup.Width, -popup.Height - 6), PopupPrimaryAxis.Horizontal)
        };
        _menu.Closed -= Menu_Closed;
        _menu.Closed += Menu_Closed;
        _menuOwner = Window.GetWindow(this);
        if (_menuOwner != null) { _menuOwner.LocationChanged += CloseMenu; _menuOwner.Deactivated += CloseMenu; }
        foreach (var item in _menu.Items.OfType<MenuItem>())
            if (item.Tag?.ToString() == "start") item.IsEnabled = DataContext is Todo t && !t.IsCompleted && t.Status != "In Progress";
        _menu.IsOpen = true;
        e.Handled = true;
    }
    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Todo todo || Window.GetWindow(this)?.DataContext is not TaskListViewModel vm) return;
        var command = ((MenuItem)sender).Tag?.ToString() switch {
            "edit" => vm.EditTodoCommand, "start" => vm.StartTodoCommand,
            "complete" => vm.ToggleCompleteCommand, "delete" => vm.DeleteTodoCommand, _ => null };
        command?.Execute(todo);
    }
}
