using TodoApp.Services;
namespace TodoApp.Models;
public sealed record CalendarEntry(Todo? Task, ScheduleOccurrence? Schedule)
{
    public string Title => Task?.Title ?? Schedule!.Title;
    public string TimeLabel => Task?.TimeLabel ?? Schedule!.TimeLabel;
    public DateTime Start => Task?.Deadline ?? Schedule!.Start;
    public bool IsCompleted => Task?.IsCompleted ?? Schedule!.IsCompleted;
    public bool IsSchedule => Schedule!=null;
    public string EffectivePriority => Task?.EffectivePriority ?? "Schedule";
    public string Tooltip => Schedule is { } s ? $"{s.Title}\n{s.WhenLabel}\n{s.Notes}" : Task!.Title;
}
