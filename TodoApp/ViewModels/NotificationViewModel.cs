using System;
using System.Windows;
using System.Windows.Input;
using TodoApp.Models;
using TodoApp.Services;

namespace TodoApp.ViewModels
{
    public class NotificationViewModel : ViewModelBase
    {
        private readonly ReminderService _reminderService;
        private readonly Window _window;
        private Todo _todo;
        private bool _isMuted = false;
        public bool IsPreview { get; }
        private string _previewFeedback = "";
        public string PreviewFeedback { get => _previewFeedback; private set => SetProperty(ref _previewFeedback, value); }

        public Todo Todo
        {
            get => _todo;
            set => SetProperty(ref _todo, value);
        }

        public bool IsMuted
        {
            get => _isMuted;
            set => SetProperty(ref _isMuted, value);
        }

        public ICommand StartWorkingCommand { get; }
        public ICommand CompleteCommand { get; }
        public ICommand SnoozeCommand { get; }
        public ICommand MuteCommand { get; }
        public string MuteLabel => Localizer.Text(IsMuted ? "Disenyapkan" : "Senyapkan");
        public void RefreshLanguage() { OnPropertyChanged(nameof(Todo)); OnPropertyChanged(nameof(MuteLabel)); }

        public NotificationViewModel(Todo todo, ReminderService reminderService, Window window, bool isPreview = false)
        {
            _todo = todo;
            _reminderService = reminderService;
            _window = window;
            IsPreview = isPreview;

            StartWorkingCommand = new RelayCommand(StartWorking);
            CompleteCommand = new RelayCommand(Complete);
            SnoozeCommand = new RelayCommand(Snooze);
            MuteCommand = new RelayCommand(Mute);
        }

        private void StartWorking()
        {
            if (IsPreview) { PreviewFeedback = Localizer.Text("Preview: tugas ditandai sedang dikerjakan."); return; }
            _reminderService.StartWorking(Todo);
            if (_window.IsLoaded) _window.Close();
        }

        private void Complete()
        {
            if (IsPreview) { PreviewFeedback = Localizer.Text("Preview: tugas ditandai selesai."); return; }
            _reminderService.CompleteTodo(Todo);
            if (_window.IsLoaded) _window.Close();
        }

        private void Snooze(object? parameter)
        {
            if (IsPreview) { PreviewFeedback = Localizer.Format("Preview: pengingat ditunda {0} menit.", parameter ?? 15); return; }
            if (parameter != null && int.TryParse(parameter.ToString(), out int minutes))
            {
                _reminderService.SnoozeTodo(Todo, minutes);
                if (_window.IsLoaded) _window.Close();
            }
        }

        private void Mute()
        {
            IsMuted = true;
            OnPropertyChanged(nameof(MuteLabel));
            if (!IsPreview) _reminderService.MuteSound();
        }
    }
}
