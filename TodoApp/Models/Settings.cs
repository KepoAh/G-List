namespace TodoApp.Models
{
    public class Settings
    {
        public bool Startup { get; set; } = false;
        public bool AlwaysOnTop { get; set; } = false;
        public string Theme { get; set; } = "Dark";
        public string Language { get; set; } = "id";
        public double Volume { get; set; } = 0.5;
        public string DefaultSound { get; set; } = "Assets/default_alarm.mp3";
        public string DraftTitle { get; set; } = string.Empty;
        public string DraftPriority { get; set; } = "Heavy";
        public string DraftNotes { get; set; } = string.Empty;
        public string DraftDate { get; set; } = string.Empty;
        public string DraftTime { get; set; } = "17:00";
        public int DraftReminder { get; set; } = 0;
        public bool DraftUseDeadline { get; set; } = true;
    }
}
