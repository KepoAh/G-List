using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using TodoApp.Models;

namespace TodoApp.Services
{
    public partial class DatabaseService
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public event Action? SettingsChanged;
        public string DataDirectory => Path.GetDirectoryName(_dbPath)!;

        public DatabaseService(string? dataDirectory = null)
        {
            bool migrate = dataDirectory == null;
            dataDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "G-tools");
            Directory.CreateDirectory(dataDirectory);
            _dbPath = Path.Combine(dataDirectory, "todo.db");
            _connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
            if (migrate && !File.Exists(_dbPath))
            {
                var candidates = new[] { Path.Combine(AppContext.BaseDirectory, "todo.db"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Personal Desktop Todo Assistant", "todo.db") };
                foreach (var legacy in candidates)
                {
                    if (!File.Exists(legacy) || Path.GetFullPath(legacy).Equals(Path.GetFullPath(_dbPath), StringComparison.OrdinalIgnoreCase)) continue;
                    var pendingMigration = _dbPath + ".migrating";
                    Snapshot(legacy, pendingMigration);
                    File.Move(pendingMigration, _dbPath);
                    break;
                }
            }
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();

                // Create todos table
                InitializeSchedules(conn);
                string createTodosTable = @"
                    CREATE TABLE IF NOT EXISTS todos (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        title TEXT NOT NULL,
                        notes TEXT,
                        priority TEXT NOT NULL,
                        status TEXT NOT NULL,
                        deadline TEXT,
                        notification_time TEXT,
                        sound_path TEXT,
                        created_at TEXT NOT NULL,
                        completed_at TEXT,
                        last_reminded_at TEXT,
                        snooze_until TEXT
                    );";

                // Create settings table
                string createSettingsTable = @"
                    CREATE TABLE IF NOT EXISTS settings (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        startup INTEGER DEFAULT 0,
                        always_on_top INTEGER DEFAULT 0,
                        theme TEXT DEFAULT 'Dark',
                        volume REAL DEFAULT 0.5,
                        default_sound TEXT
                    );";

                // Insert default settings if not exists
                string insertDefaultSettings = @"
                    INSERT OR IGNORE INTO settings (id, startup, always_on_top, theme, volume, default_sound)
                    VALUES (1, 0, 0, 'Dark', 0.5, 'Assets/default_alarm.mp3');";

                using (var cmd = new SqliteCommand(createTodosTable, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SqliteCommand(createSettingsTable, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var info = new SqliteCommand("SELECT name FROM pragma_table_info('todos') WHERE name='manual_priority'", conn))
                    if (info.ExecuteScalar() == null)
                        using (var alter = new SqliteCommand("ALTER TABLE todos ADD COLUMN manual_priority INTEGER NOT NULL DEFAULT 0", conn)) alter.ExecuteNonQuery();

                // Run migration to add draft columns if they don't exist
                using (var cmd = new SqliteCommand("PRAGMA table_info(settings);", conn))
                {
                    var columns = new List<string>();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            columns.Add(reader.GetString(reader.GetOrdinal("name")));
                        }
                    }

                    if (!columns.Contains("draft_title"))
                    {
                        using (var alterCmd = new SqliteCommand("ALTER TABLE settings ADD COLUMN draft_title TEXT DEFAULT '';", conn))
                        {
                            alterCmd.ExecuteNonQuery();
                        }
                    }
                    if (!columns.Contains("draft_priority"))
                    {
                        using (var alterCmd = new SqliteCommand("ALTER TABLE settings ADD COLUMN draft_priority TEXT DEFAULT 'Heavy';", conn))
                        {
                            alterCmd.ExecuteNonQuery();
                        }
                    }
                }

                var draftColumns = new Dictionary<string, string> {
                    ["language"] = "TEXT NOT NULL DEFAULT 'id'", ["draft_notes"] = "TEXT DEFAULT ''", ["draft_date"] = "TEXT DEFAULT ''",
                    ["draft_time"] = "TEXT DEFAULT '17:00'", ["draft_reminder"] = "INTEGER DEFAULT 0", ["draft_use_deadline"] = "INTEGER DEFAULT 1" };
                foreach (var column in draftColumns)
                {
                    using var info = new SqliteCommand("SELECT name FROM pragma_table_info('settings') WHERE name = $name", conn);
                    info.Parameters.AddWithValue("$name", column.Key);
                    if (info.ExecuteScalar() == null)
                        new SqliteCommand($"ALTER TABLE settings ADD COLUMN {column.Key} {column.Value}", conn).ExecuteNonQuery();
                }
                using (var cmd = new SqliteCommand(insertDefaultSettings, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // --- CRUD Operations for Todos ---

        public Todo? GetTodo(int id)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = new SqliteCommand("SELECT * FROM todos WHERE id = $id", conn);
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadTodo(reader) : null;
        }

        public List<Todo> GetActiveTodos()
        {
            var list = new List<Todo>();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT * FROM todos WHERE status != 'Completed' ORDER BY deadline ASC, created_at DESC";
                using (var cmd = new SqliteCommand(query, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(ReadTodo(reader));
                        }
                    }
                }
            }
            return list;
        }

        public List<Todo> GetCompletedTodos()
        {
            var list = new List<Todo>();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT * FROM todos WHERE status = 'Completed' ORDER BY completed_at DESC";
                using (var cmd = new SqliteCommand(query, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(ReadTodo(reader));
                        }
                    }
                }
            }
            return list;
        }

        public List<Todo> GetPendingReminders()
        {
            var list = new List<Todo>();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                // Select active todos that have notification times set
                string query = "SELECT * FROM todos WHERE status != 'Completed' AND notification_time IS NOT NULL";
                using (var cmd = new SqliteCommand(query, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(ReadTodo(reader));
                        }
                    }
                }
            }
            return list;
        }

        public void SaveTodo(Todo todo)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                if (todo.Id == 0)
                {
                    // Insert
                    string query = @"
                        INSERT INTO todos (manual_priority, title, notes, priority, status, deadline, notification_time, sound_path, created_at, completed_at, last_reminded_at, snooze_until)
                        VALUES ($manual_priority, $title, $notes, $priority, $status, $deadline, $notification_time, $sound_path, $created_at, $completed_at, $last_reminded_at, $snooze_until);
                        SELECT last_insert_rowid();";
                    
                    using (var cmd = new SqliteCommand(query, conn))
                    {
                        AddTodoParameters(cmd, todo);
                        todo.Id = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
                else
                {
                    // Update
                    string query = @"
                        UPDATE todos SET 
                            title = $title, 
                            notes = $notes, 
                            priority = $priority, manual_priority = $manual_priority, 
                            status = $status, 
                            deadline = $deadline, 
                            notification_time = $notification_time, 
                            sound_path = $sound_path, 
                            created_at = $created_at, 
                            completed_at = $completed_at, 
                            last_reminded_at = $last_reminded_at, 
                            snooze_until = $snooze_until
                        WHERE id = $id";
                    
                    using (var cmd = new SqliteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("$id", todo.Id);
                        AddTodoParameters(cmd, todo);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        public void DeleteTodo(int id)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                string query = "DELETE FROM todos WHERE id = $id";
                using (var cmd = new SqliteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // --- Settings Management ---

        public Settings LoadSettings()
        {
            var settings = new Settings();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT * FROM settings WHERE id = 1";
                using (var cmd = new SqliteCommand(query, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            settings.Startup = reader.GetInt32(reader.GetOrdinal("startup")) == 1;
                            settings.AlwaysOnTop = reader.GetInt32(reader.GetOrdinal("always_on_top")) == 1;
                            settings.Theme = reader.GetString(reader.GetOrdinal("theme"));
                            settings.Language = reader["language"] as string ?? "id";
                            settings.Volume = reader.GetDouble(reader.GetOrdinal("volume"));
                            settings.DefaultSound = reader.IsDBNull(reader.GetOrdinal("default_sound")) 
                                ? "Assets/default_alarm.mp3" 
                                : reader.GetString(reader.GetOrdinal("default_sound"));
                            settings.DraftTitle = reader.IsDBNull(reader.GetOrdinal("draft_title")) 
                                ? string.Empty 
                                : reader.GetString(reader.GetOrdinal("draft_title"));
                            settings.DraftPriority = reader.IsDBNull(reader.GetOrdinal("draft_priority")) 
                                ? "Medium" 
                                : reader.GetString(reader.GetOrdinal("draft_priority"));
                            settings.DraftNotes = reader["draft_notes"] as string ?? "";
                            settings.DraftDate = reader["draft_date"] as string ?? "";
                            settings.DraftTime = reader["draft_time"] as string ?? "17:00";
                            settings.DraftReminder = Convert.ToInt32(reader["draft_reminder"] is DBNull ? 0 : reader["draft_reminder"]);
                            settings.DraftUseDeadline = Convert.ToInt32(reader["draft_use_deadline"] is DBNull ? 1 : reader["draft_use_deadline"]) != 0;
                        }
                    }
                }
            }
            return settings;
        }

        public void SaveSettings(Settings settings)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                string query = @"
                    UPDATE settings SET 
                        startup = $startup, 
                        always_on_top = $always_on_top, 
                        theme = $theme, language = $language, 
                        volume = $volume, 
                        default_sound = $default_sound
                    WHERE id = 1";
                
                using (var cmd = new SqliteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("$startup", settings.Startup ? 1 : 0);
                    cmd.Parameters.AddWithValue("$always_on_top", settings.AlwaysOnTop ? 1 : 0);
                    cmd.Parameters.AddWithValue("$theme", settings.Theme);
                    cmd.Parameters.AddWithValue("$language", settings.Language == "en" ? "en" : "id");
                    cmd.Parameters.AddWithValue("$volume", settings.Volume);
                    cmd.Parameters.AddWithValue("$default_sound", settings.DefaultSound ?? (object)DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
            SettingsChanged?.Invoke();
        }

        // --- Backup & Restore ---

        public void SaveDraft(Settings draft)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = new SqliteCommand(@"UPDATE settings SET draft_title=$title, draft_notes=$notes,
                draft_priority=$priority, draft_date=$date, draft_time=$time, draft_reminder=$reminder, draft_use_deadline=$useDeadline WHERE id=1", conn);
            cmd.Parameters.AddWithValue("$title", draft.DraftTitle);
            cmd.Parameters.AddWithValue("$notes", draft.DraftNotes);
            cmd.Parameters.AddWithValue("$priority", draft.DraftPriority);
            cmd.Parameters.AddWithValue("$date", draft.DraftDate);
            cmd.Parameters.AddWithValue("$time", draft.DraftTime);
            cmd.Parameters.AddWithValue("$reminder", draft.DraftReminder);
            cmd.Parameters.AddWithValue("$useDeadline", draft.DraftUseDeadline ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        private static void Snapshot(string source, string destination)
        {
            using var from = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            using var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
            from.Open(); to.Open(); from.BackupDatabase(to);
        }

        public void BackupDatabase(string targetFilePath)
        {
            if (Path.GetFullPath(targetFilePath).Equals(Path.GetFullPath(_dbPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Pilih lokasi backup yang berbeda dari database aktif.");
            Snapshot(_dbPath, targetFilePath);
        }

        public void RestoreDatabase(string sourceFilePath)
        {
            if (Path.GetFullPath(sourceFilePath).Equals(Path.GetFullPath(_dbPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("File ini sudah menjadi database aktif.");
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceFilePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                source.Open();
                using var check = new SqliteCommand("PRAGMA integrity_check", source);
                if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("File backup tidak valid.");
                using var schema = new SqliteCommand("SELECT id,title,notes,priority,status,deadline,notification_time,sound_path,created_at,completed_at,last_reminded_at,snooze_until FROM todos LIMIT 1", source);
                using var reader = schema.ExecuteReader();
                  if (reader.Read()) ReadTodo(reader);
                  reader.Close();
                  using var scheduleTable = new SqliteCommand("SELECT 1 FROM sqlite_master WHERE type='table' AND name='schedules'", source);
                  if (scheduleTable.ExecuteScalar() != null)
                  {
                      using var schedules = new SqliteCommand("SELECT id,revision,title,notes,weekday,start_minute,end_minute,reminder_minutes,start_date,end_date,created_at,archived_at FROM schedules", source);
                      using var rows = schedules.ExecuteReader();
                      while (rows.Read()) ReadSchedule(rows);
                  }
            }
            BackupDatabase(Path.Combine(DataDirectory, "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".db"));
            Snapshot(sourceFilePath, _dbPath);
            InitializeDatabase();
            SettingsChanged?.Invoke();
        }

        // --- Helper Methods ---

        private Todo ReadTodo(SqliteDataReader reader)
        {
            return new Todo
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Notes = reader.IsDBNull(reader.GetOrdinal("notes")) ? string.Empty : reader.GetString(reader.GetOrdinal("notes")),
                Priority = reader.GetString(reader.GetOrdinal("priority")),
                ManualPriority = Enumerable.Range(0, reader.FieldCount).Any(i => reader.GetName(i) == "manual_priority") && reader.GetInt32(reader.GetOrdinal("manual_priority")) != 0,
                Status = reader.GetString(reader.GetOrdinal("status")),
                Deadline = ParseNullableDateTime(reader.IsDBNull(reader.GetOrdinal("deadline")) ? null : reader.GetString(reader.GetOrdinal("deadline"))),
                NotificationTime = ParseNullableDateTime(reader.IsDBNull(reader.GetOrdinal("notification_time")) ? null : reader.GetString(reader.GetOrdinal("notification_time"))),
                SoundPath = reader.IsDBNull(reader.GetOrdinal("sound_path")) ? string.Empty : reader.GetString(reader.GetOrdinal("sound_path")),
                CreatedAt = ParseNullableDateTime(reader.GetString(reader.GetOrdinal("created_at"))) ?? DateTime.Now,
                CompletedAt = ParseNullableDateTime(reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : reader.GetString(reader.GetOrdinal("completed_at"))),
                LastRemindedAt = ParseNullableDateTime(reader.IsDBNull(reader.GetOrdinal("last_reminded_at")) ? null : reader.GetString(reader.GetOrdinal("last_reminded_at"))),
                SnoozeUntil = ParseNullableDateTime(reader.IsDBNull(reader.GetOrdinal("snooze_until")) ? null : reader.GetString(reader.GetOrdinal("snooze_until")))
            };
        }

        private void AddTodoParameters(SqliteCommand cmd, Todo todo)
        {
            cmd.Parameters.AddWithValue("$title", todo.Title);
            cmd.Parameters.AddWithValue("$notes", todo.Notes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$priority", todo.Priority);
            cmd.Parameters.AddWithValue("$manual_priority", todo.ManualPriority ? 1 : 0);
            cmd.Parameters.AddWithValue("$status", todo.Status);
            cmd.Parameters.AddWithValue("$deadline", todo.Deadline.HasValue ? todo.Deadline.Value.ToString("o") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$notification_time", todo.NotificationTime.HasValue ? todo.NotificationTime.Value.ToString("o") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$sound_path", todo.SoundPath ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$created_at", todo.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$completed_at", todo.CompletedAt.HasValue ? todo.CompletedAt.Value.ToString("o") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$last_reminded_at", todo.LastRemindedAt.HasValue ? todo.LastRemindedAt.Value.ToString("o") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$snooze_until", todo.SnoozeUntil.HasValue ? todo.SnoozeUntil.Value.ToString("o") : (object)DBNull.Value);
        }

        private DateTime? ParseNullableDateTime(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
            {
                if (dt.Kind == DateTimeKind.Utc)
                    return dt.ToLocalTime();
                return DateTime.SpecifyKind(dt, DateTimeKind.Local);
            }
            return null;
        }
    }
}
