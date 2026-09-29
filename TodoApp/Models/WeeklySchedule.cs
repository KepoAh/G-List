using TodoApp.Services;
namespace TodoApp.Models;
public sealed class WeeklySchedule
{
    public int Id { get; set; }
    public int Revision { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public DayOfWeek Weekday { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
    public int ReminderMinutes { get; set; } = 15;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ArchivedAt { get; set; }
    public DateTime EffectiveStart => (StartDate ?? CreatedAt).Date;
    public bool IsCompletedAt(DateTime now) => ArchivedAt.HasValue || EndDate?.Date < now.Date;
    public bool IsCompleted => IsCompletedAt(DateTime.Now);
    public string DayLabel => Localizer.Instance.Culture.DateTimeFormat.GetDayName(Weekday);
    public string TimeRange => $"{TimeSpan.FromMinutes(StartMinute):hh\\:mm} – {TimeSpan.FromMinutes(EndMinute):hh\\:mm}";
    public string DayTimeLabel => $"{DayLabel} · {TimeRange}";
    public DateTime? EffectiveEnd => ArchivedAt.HasValue && (!EndDate.HasValue || ArchivedAt.Value.Date<EndDate.Value.Date) ? ArchivedAt.Value.Date : EndDate?.Date;
    public string RangeLabel => EffectiveEnd.HasValue && EffectiveEnd.Value<EffectiveStart
        ? Localizer.Text("Belum mulai") + " · " + EffectiveStart.ToString("dd MMM yyyy",Localizer.Instance.Culture)
        : $"{EffectiveStart.ToString("dd MMM yyyy", Localizer.Instance.Culture)} — {(EffectiveEnd.HasValue ? EffectiveEnd.Value.ToString("dd MMM yyyy", Localizer.Instance.Culture) : Localizer.Text("Tanpa batas akhir"))}";
    public string CompletionLabel => ArchivedAt.HasValue
        ? Localizer.Format("Diakhiri pada {0}", ArchivedAt.Value.ToString("dd MMM yyyy · HH:mm", Localizer.Instance.Culture))
        : IsCompleted ? Localizer.Text("Rentang jadwal telah berakhir.") : "";
    public bool OccursOn(DateTime day)
    {
        var start = day.Date.AddMinutes(StartMinute);
        return day.DayOfWeek == Weekday && day.Date >= EffectiveStart && (!EndDate.HasValue || day.Date <= EndDate.Value.Date)
            && (!ArchivedAt.HasValue || start <= ArchivedAt.Value);
    }
    public IEnumerable<ScheduleOccurrence> Between(DateTime from, DateTime to)
    {
        var first = from.Date > EffectiveStart ? from.Date : EffectiveStart;
        if (to.Date < first) yield break;
        var offset = ((int)Weekday - (int)first.DayOfWeek + 7) % 7;
        if ((DateTime.MaxValue.Date-first).TotalDays < offset) yield break;
        first = first.AddDays(offset);
        for (var day = first; day <= to.Date; day = day.AddDays(7))
        {
            if (OccursOn(day)) yield return new(this, day.AddMinutes(StartMinute), day.AddMinutes(EndMinute));
            if (day > DateTime.MaxValue.Date.AddDays(-7)) break;
        }
    }
    public ScheduleOccurrence? Next(DateTime now)
    {
        if (IsCompletedAt(now)) return null;
        var first = now.Date > EffectiveStart ? now.Date : EffectiveStart;
        return Between(first, first > DateTime.MaxValue.Date.AddDays(-7) ? DateTime.MaxValue.Date : first.AddDays(7)).FirstOrDefault(o => o.End > now);
    }
}
public sealed record ScheduleOccurrence(WeeklySchedule Schedule, DateTime Start, DateTime End)
{
    public string Title => Schedule.Title;
    public string Notes => Schedule.Notes;
    public string TimeLabel => Schedule.TimeRange;
    public string WhenLabel => $"{Start.ToString("dddd, dd MMM", Localizer.Instance.Culture)} · {TimeLabel}";
    public bool IsCompleted => Schedule.IsCompleted;
    public DateTime ReminderAt => Start.AddMinutes(-Math.Max(0, Schedule.ReminderMinutes));
}
