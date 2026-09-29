using TodoApp.Services;
using System;

namespace TodoApp.Models
{
    public class Todo
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        
        // Priority: "Light", "Medium", "Heavy"
        public string Priority { get; set; } = "Light";
        public bool ManualPriority { get; set; }
        public static string CategoryFor(DateTime deadline, DateTime today) =>
            (deadline.Date - today.Date).Days <= 7 ? "Heavy" : (deadline.Date - today.Date).Days <= 14 ? "Medium" : "Light";
        public string EffectivePriority => !ManualPriority && Deadline.HasValue ? CategoryFor(Deadline.Value, IsCompleted ? CompletedAt ?? DateTime.Today : DateTime.Today) : Priority;
        public string PriorityModeLabel => ManualPriority ? Localizer.Text("Kategori manual") : Localizer.Text("Kategori otomatis mengikuti tanggal");
        
        // Status: "Not Started", "In Progress", "Completed"
        public string Status { get; set; } = "Not Started";
        
        public DateTime? Deadline { get; set; }
        public DateTime? NotificationTime { get; set; }
        public string SoundPath { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
        public DateTime? LastRemindedAt { get; set; }
        public DateTime? SnoozeUntil { get; set; }

        public bool IsOverdue => Deadline.HasValue && Deadline.Value < DateTime.Now && Status != "Completed";
        public bool IsCompleted => Status == "Completed";
        public string DateLabel => Deadline?.ToString("dd MMM yyyy", Localizer.Instance.Culture) ?? Localizer.Text("Tanpa deadline");
        public string TimeLabel => Deadline?.ToString("HH:mm") ?? "";
        public string DeadlineLabel => Deadline?.ToString("dd MMM yyyy · HH:mm", Localizer.Instance.Culture) ?? Localizer.Text("Tanpa deadline");
        public string StatusLabel => Status == "Completed" ? Localizer.Text("Selesai") : Status == "In Progress" ? Localizer.Text("Dikerjakan") : Localizer.Text("Belum mulai");
    }
}
