using Microsoft.Data.Sqlite;
using TodoApp.Models;
namespace TodoApp.Services;
public partial class DatabaseService
{
    private static void InitializeSchedules(SqliteConnection conn)
    {
        using var cmd = new SqliteCommand(@"
            CREATE TABLE IF NOT EXISTS schedules (
                id INTEGER PRIMARY KEY AUTOINCREMENT, revision INTEGER NOT NULL DEFAULT 1,
                title TEXT NOT NULL, notes TEXT NOT NULL DEFAULT '', weekday INTEGER NOT NULL CHECK(weekday BETWEEN 0 AND 6),
                start_minute INTEGER NOT NULL CHECK(start_minute BETWEEN 0 AND 1439),
                end_minute INTEGER NOT NULL CHECK(end_minute BETWEEN 1 AND 1439 AND end_minute > start_minute),
                reminder_minutes INTEGER NOT NULL DEFAULT 15, start_date TEXT, end_date TEXT, created_at TEXT NOT NULL, archived_at TEXT
            );
            CREATE TABLE IF NOT EXISTS schedule_deliveries (
                schedule_id INTEGER NOT NULL, revision INTEGER NOT NULL, occurrence_start TEXT NOT NULL,
                delivered_at TEXT NOT NULL, PRIMARY KEY(schedule_id,revision,occurrence_start)
            );", conn);
        cmd.ExecuteNonQuery();
    }
    private static WeeklySchedule ReadSchedule(SqliteDataReader r) => new() {
        Id = r.GetInt32(r.GetOrdinal("id")), Revision = r.GetInt32(r.GetOrdinal("revision")),
        Title = (string)r["title"], Notes = (string)r["notes"], Weekday = (DayOfWeek)Convert.ToInt32(r["weekday"]),
        StartMinute = Convert.ToInt32(r["start_minute"]), EndMinute = Convert.ToInt32(r["end_minute"]),
        ReminderMinutes = Convert.ToInt32(r["reminder_minutes"]),
        StartDate = ReadScheduleDate(r["start_date"]), EndDate = ReadScheduleDate(r["end_date"]),
        CreatedAt = ReadScheduleDate(r["created_at"])!.Value, ArchivedAt = ReadScheduleDate(r["archived_at"])
    };
    private static DateTime? ReadScheduleDate(object value) => value is DBNull ? null :
        DateTime.Parse((string)value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
    public List<WeeklySchedule> GetSchedules()
    {
        using var conn = new SqliteConnection(_connectionString); conn.Open();
        using var cmd = new SqliteCommand("SELECT * FROM schedules ORDER BY weekday,start_minute,title",conn);
        using var r = cmd.ExecuteReader(); var result = new List<WeeklySchedule>();
        while(r.Read()) result.Add(ReadSchedule(r)); return result;
    }
    public WeeklySchedule? GetSchedule(int id) => GetSchedules().FirstOrDefault(s => s.Id == id);
    public void SaveSchedule(WeeklySchedule s)
    {
        if (string.IsNullOrWhiteSpace(s.Title) || (int)s.Weekday is < 0 or > 6 ||
            s.StartMinute < 0 || s.EndMinute > 1439 || s.EndMinute <= s.StartMinute ||
            s.ReminderMinutes is < -1 or > 1440 || s.EndDate?.Date < s.EffectiveStart)
            throw new ArgumentException(Localizer.Text("Periksa nama, jam, dan rentang tanggal jadwal."));
        using var conn = new SqliteConnection(_connectionString); conn.Open();
        using var cmd = new SqliteCommand(s.Id == 0 ? @"
            INSERT INTO schedules(title,notes,weekday,start_minute,end_minute,reminder_minutes,start_date,end_date,created_at)
            VALUES($title,$notes,$day,$start,$end,$reminder,$from,$to,$created); SELECT last_insert_rowid();" : @"
            UPDATE schedules SET title=$title,notes=$notes,weekday=$day,start_minute=$start,end_minute=$end,
            reminder_minutes=$reminder,start_date=$from,end_date=$to,revision=revision+1
            WHERE id=$id AND archived_at IS NULL AND revision=$revision;",conn);
        cmd.Parameters.AddWithValue("$id",s.Id); cmd.Parameters.AddWithValue("$revision",s.Revision);
        cmd.Parameters.AddWithValue("$title",s.Title.Trim()); cmd.Parameters.AddWithValue("$notes",s.Notes.Trim());
        cmd.Parameters.AddWithValue("$day",(int)s.Weekday); cmd.Parameters.AddWithValue("$start",s.StartMinute); cmd.Parameters.AddWithValue("$end",s.EndMinute);
        cmd.Parameters.AddWithValue("$reminder",s.ReminderMinutes);
        cmd.Parameters.AddWithValue("$from",(object?)s.StartDate?.Date.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$to",(object?)s.EndDate?.Date.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created",s.CreatedAt.ToString("o"));
        if(s.Id == 0) s.Id = Convert.ToInt32(cmd.ExecuteScalar());
        else { if(cmd.ExecuteNonQuery()!=1) throw new InvalidOperationException(Localizer.Text("Jadwal sudah berubah atau diakhiri. Buka kembali jadwalnya.")); s.Revision++; }
    }
    public void ArchiveSchedule(int id, DateTime now)
    {
        using var conn = new SqliteConnection(_connectionString); conn.Open();
        using var cmd = new SqliteCommand("UPDATE schedules SET archived_at=$now WHERE id=$id AND archived_at IS NULL",conn);
        cmd.Parameters.AddWithValue("$id",id); cmd.Parameters.AddWithValue("$now",now.ToString("o")); cmd.ExecuteNonQuery();
    }
    public bool WasScheduleReminded(ScheduleOccurrence occurrence)
    {
        using var conn = new SqliteConnection(_connectionString); conn.Open();
        using var cmd = new SqliteCommand("SELECT 1 FROM schedule_deliveries WHERE schedule_id=$id AND revision=$rev AND occurrence_start=$start",conn);
        cmd.Parameters.AddWithValue("$id",occurrence.Schedule.Id); cmd.Parameters.AddWithValue("$rev",occurrence.Schedule.Revision);
        cmd.Parameters.AddWithValue("$start",occurrence.Start.ToString("o")); return cmd.ExecuteScalar()!=null;
    }
    public bool TryClaimScheduleReminder(ScheduleOccurrence occurrence, DateTime now)
    {
        // Recheck persisted state so an old popup/queue cannot remind an edited or archived schedule.
        using var conn = new SqliteConnection(_connectionString); conn.Open();
        using var transaction = conn.BeginTransaction();
        using var select = new SqliteCommand("SELECT * FROM schedules WHERE id=$id",conn,transaction);
        select.Parameters.AddWithValue("$id",occurrence.Schedule.Id);
        WeeklySchedule? current;
        using(var r=select.ExecuteReader()) current=r.Read()?ReadSchedule(r):null;
        if(current==null || current.Revision!=occurrence.Schedule.Revision || current.IsCompletedAt(now) ||
            current.ReminderMinutes<0 || !current.OccursOn(occurrence.Start.Date) ||
            occurrence.Start!=occurrence.Start.Date.AddMinutes(current.StartMinute) ||
            occurrence.ReminderAt>now || occurrence.End<=now) return false;
        using var cmd = new SqliteCommand(@"INSERT OR IGNORE INTO schedule_deliveries(schedule_id,revision,occurrence_start,delivered_at)
            VALUES($id,$rev,$start,$now)",conn,transaction);
        cmd.Parameters.AddWithValue("$id",current.Id); cmd.Parameters.AddWithValue("$rev",current.Revision);
        cmd.Parameters.AddWithValue("$start",occurrence.Start.ToString("o")); cmd.Parameters.AddWithValue("$now",now.ToString("o"));
        bool claimed=cmd.ExecuteNonQuery()==1; transaction.Commit(); return claimed;
    }
}

