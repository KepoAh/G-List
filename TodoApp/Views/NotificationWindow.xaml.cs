using System;
using System.Windows;
using System.Windows.Input;
using TodoApp.Models;
using TodoApp.Services;
using TodoApp.ViewModels;

namespace TodoApp.Views
{
    public partial class NotificationWindow : Window
    {
        public NotificationWindow(Todo todo, ReminderService reminderService, bool isPreview = false)
        {
            InitializeComponent();
            DataContext = new NotificationViewModel(todo, reminderService, this, isPreview);
            if (isPreview) Height = 435;
            Localizer.Instance.Changed += RefreshLanguage;
            Closed += (_, _) => Localizer.Instance.Changed -= RefreshLanguage;
            Left = SystemParameters.WorkArea.Right - Width - 20;
            Top = SystemParameters.WorkArea.Bottom - Height - 20;
        }

        private void RefreshLanguage()
        {
            if (DataContext is NotificationViewModel vm) vm.RefreshLanguage();
        }
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }
    }
}
