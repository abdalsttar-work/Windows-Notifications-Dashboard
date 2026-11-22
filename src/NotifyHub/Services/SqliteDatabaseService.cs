using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NotifyHub.Models;

namespace NotifyHub.Services;

public class SqliteDatabaseService : IDatabaseService
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly ISecureStorageService _secureStorage;

    public string DatabasePath => _dbPath;

    public SqliteDatabaseService(ISecureStorageService secureStorage, string? customPath = null)
    {
        _secureStorage = secureStorage;

        if (string.IsNullOrWhiteSpace(customPath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "NotifyHub");
            Directory.CreateDirectory(folder);
            _dbPath = Path.Combine(folder, "notifyhub.db");
        }
        else
        {
            _dbPath = customPath;
            string? dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    private async Task<SqliteConnection> CreateOpenConnectionAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
            await cmd.ExecuteNonQueryAsync();
        }

        return conn;
    }

    public async Task InitializeDatabaseAsync()
    {
        using var conn = await CreateOpenConnectionAsync();
        using var transaction = conn.BeginTransaction();

        string createTablesSql = @"
            CREATE TABLE IF NOT EXISTS Notifications (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NotificationId TEXT NOT NULL,
                AppName TEXT NOT NULL,
                AppId TEXT,
                Title TEXT,
                Body TEXT,
                Category TEXT NOT NULL,
                ReceivedAt TEXT NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                Priority INTEGER NOT NULL DEFAULT 1,
                UserNotes TEXT,
                LastUpdatedAt TEXT NOT NULL,
                ForwardingStatus INTEGER NOT NULL DEFAULT 0,
                RawMetadata TEXT,
                LaunchUri TEXT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_Notifications_NotificationId ON Notifications(NotificationId);
            CREATE INDEX IF NOT EXISTS IX_Notifications_ReceivedAt ON Notifications(ReceivedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_Notifications_Status ON Notifications(Status);
            CREATE INDEX IF NOT EXISTS IX_Notifications_Category ON Notifications(Category);
            CREATE INDEX IF NOT EXISTS IX_Notifications_AppName ON Notifications(AppName);

            CREATE TABLE IF NOT EXISTS Categories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT,
                IconGlyph TEXT,
                ColorHex TEXT,
                IsSystem INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS CategoryRules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CategoryName TEXT NOT NULL,
                MatchType INTEGER NOT NULL DEFAULT 0,
                Pattern TEXT NOT NULL,
                Priority INTEGER NOT NULL DEFAULT 10,
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS ForwardingLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NotificationId INTEGER NOT NULL,
                AppName TEXT,
                NotificationTitle TEXT,
                DestinationType INTEGER NOT NULL,
                Status TEXT NOT NULL,
                AttemptCount INTEGER NOT NULL DEFAULT 1,
                Timestamp TEXT NOT NULL,
                ErrorDetails TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_ForwardingLogs_NotificationId ON ForwardingLogs(NotificationId);
            CREATE INDEX IF NOT EXISTS IX_ForwardingLogs_Timestamp ON ForwardingLogs(Timestamp DESC);

            CREATE TABLE IF NOT EXISTS AppSettingsData (
                Key TEXT PRIMARY KEY,
                Value TEXT
            );
        ";

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = createTablesSql;
            await cmd.ExecuteNonQueryAsync();
        }

        // Safe migration: Add LaunchUri column to Notifications if upgrading existing DB
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.Transaction = transaction;
            alterCmd.CommandText = "ALTER TABLE Notifications ADD COLUMN LaunchUri TEXT;";
            await alterCmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Column already exists or table was just created
        }

        await SeedDefaultCategoriesAndRulesAsync(conn, transaction);
        transaction.Commit();
    }

    private async Task SeedDefaultCategoriesAndRulesAsync(SqliteConnection conn, SqliteTransaction transaction)
    {
        // Check if categories exist
        using var countCmd = conn.CreateCommand();
        countCmd.Transaction = transaction;
        countCmd.CommandText = "SELECT COUNT(*) FROM Categories;";
        long catCount = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);

        if (catCount == 0)
        {
            var defaultCategories = new (string Name, string Desc, string Icon, string Color)[]
            {
                ("Work & Business", "Productivity, collaboration, and office tools", "💼", "#0078D4"),
                ("Emails", "Email clients and newsletters", "✉️", "#00B294"),
                ("Messages & Communication", "Instant messengers and chat apps", "💬", "#107C41"),
                ("Social Media", "Social networks, forums, and video platforms", "🌐", "#5C2D91"),
                ("Finance & Banking", "Banks, payment gateways, and crypto wallets", "💳", "#D83B01"),
                ("System & Security", "Windows OS alerts, antivirus, and security updates", "🛡️", "#E81123"),
                ("Calendar & Reminders", "Schedule alerts, alarms, and tasks", "📅", "#FF8C00"),
                ("Shopping & Orders", "E-commerce and shipment deliveries", "🛍️", "#B4009E"),
                ("Personal", "Personal contacts, family, and photos", "👤", "#008272"),
                ("Other", "Uncategorized notifications", "📌", "#69797E")
            };

            foreach (var cat in defaultCategories)
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT INTO Categories (Name, Description, IconGlyph, ColorHex, IsSystem)
                    VALUES (@Name, @Desc, @Icon, @Color, 1);";
                insertCmd.Parameters.AddWithValue("@Name", cat.Name);
                insertCmd.Parameters.AddWithValue("@Desc", cat.Desc);
                insertCmd.Parameters.AddWithValue("@Icon", cat.Icon);
                insertCmd.Parameters.AddWithValue("@Color", cat.Color);
                await insertCmd.ExecuteNonQueryAsync();
            }

            // Seed default rules
            var defaultRules = new (string Category, RuleMatchType MatchType, string Pattern, int Priority)[]
            {
                ("Work & Business", RuleMatchType.AppNameContains, "Slack", 1),
                ("Work & Business", RuleMatchType.AppNameContains, "Teams", 2),
                ("Work & Business", RuleMatchType.AppNameContains, "Zoom", 3),
                ("Work & Business", RuleMatchType.AppNameContains, "Trello", 4),
                ("Work & Business", RuleMatchType.AppNameContains, "Jira", 5),
                ("Work & Business", RuleMatchType.AppNameContains, "GitHub", 6),
                ("Emails", RuleMatchType.AppNameContains, "Outlook", 10),
                ("Emails", RuleMatchType.AppNameContains, "Thunderbird", 11),
                ("Emails", RuleMatchType.AppNameContains, "Mail", 12),
                ("Emails", RuleMatchType.ContentContains, "Email Received", 13),
                ("Messages & Communication", RuleMatchType.AppNameContains, "WhatsApp", 20),
                ("Messages & Communication", RuleMatchType.AppNameContains, "Telegram", 21),
                ("Messages & Communication", RuleMatchType.AppNameContains, "Signal", 22),
                ("Messages & Communication", RuleMatchType.AppNameContains, "Discord", 23),
                ("Social Media", RuleMatchType.AppNameContains, "Twitter", 30),
                ("Social Media", RuleMatchType.AppNameContains, "Instagram", 31),
                ("Social Media", RuleMatchType.AppNameContains, "Facebook", 32),
                ("Social Media", RuleMatchType.AppNameContains, "LinkedIn", 33),
                ("Social Media", RuleMatchType.AppNameContains, "Reddit", 34),
                ("Finance & Banking", RuleMatchType.ContentContains, "OTP", 40),
                ("Finance & Banking", RuleMatchType.ContentContains, "Verification Code", 41),
                ("Finance & Banking", RuleMatchType.ContentContains, "Transaction", 42),
                ("Finance & Banking", RuleMatchType.ContentContains, "Bank", 43),
                ("Finance & Banking", RuleMatchType.AppNameContains, "PayPal", 44),
                ("System & Security", RuleMatchType.AppNameContains, "Windows Security", 50),
                ("System & Security", RuleMatchType.AppNameContains, "Windows Update", 51),
                ("System & Security", RuleMatchType.AppNameContains, "Antivirus", 52),
                ("Calendar & Reminders", RuleMatchType.AppNameContains, "Calendar", 60),
                ("Calendar & Reminders", RuleMatchType.AppNameContains, "Reminders", 61),
                ("Shopping & Orders", RuleMatchType.AppNameContains, "Amazon", 70),
                ("Shopping & Orders", RuleMatchType.ContentContains, "Shipped", 71),
                ("Shopping & Orders", RuleMatchType.ContentContains, "Delivered", 72)
            };

            foreach (var rule in defaultRules)
            {
                using var insertRuleCmd = conn.CreateCommand();
                insertRuleCmd.Transaction = transaction;
                insertRuleCmd.CommandText = @"
                    INSERT INTO CategoryRules (CategoryName, MatchType, Pattern, Priority, IsActive)
                    VALUES (@Cat, @Type, @Pattern, @Prio, 1);";
                insertRuleCmd.Parameters.AddWithValue("@Cat", rule.Category);
                insertRuleCmd.Parameters.AddWithValue("@Type", (int)rule.MatchType);
                insertRuleCmd.Parameters.AddWithValue("@Pattern", rule.Pattern);
                insertRuleCmd.Parameters.AddWithValue("@Prio", rule.Priority);
                await insertRuleCmd.ExecuteNonQueryAsync();
            }
        }
    }

    public async Task<long> InsertNotificationAsync(NotificationItem item)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Notifications (
                NotificationId, AppName, AppId, Title, Body, Category,
                ReceivedAt, Status, Priority, UserNotes, LastUpdatedAt, ForwardingStatus, RawMetadata, LaunchUri
            )
            VALUES (
                @NotificationId, @AppName, @AppId, @Title, @Body, @Category,
                @ReceivedAt, @Status, @Priority, @UserNotes, @LastUpdatedAt, @ForwardingStatus, @RawMetadata, @LaunchUri
            );
            SELECT last_insert_rowid();";

        cmd.Parameters.AddWithValue("@NotificationId", item.NotificationId);
        cmd.Parameters.AddWithValue("@AppName", item.AppName);
        cmd.Parameters.AddWithValue("@AppId", item.AppId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Title", item.Title ?? string.Empty);
        cmd.Parameters.AddWithValue("@Body", item.Body ?? string.Empty);
        cmd.Parameters.AddWithValue("@Category", item.Category);
        cmd.Parameters.AddWithValue("@ReceivedAt", item.ReceivedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@Status", (int)item.Status);
        cmd.Parameters.AddWithValue("@Priority", (int)item.Priority);
        cmd.Parameters.AddWithValue("@UserNotes", (object?)item.UserNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LastUpdatedAt", item.LastUpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ForwardingStatus", (int)item.ForwardingStatus);
        cmd.Parameters.AddWithValue("@RawMetadata", (object?)item.RawMetadata ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LaunchUri", (object?)item.LaunchUri ?? DBNull.Value);

        try
        {
            var result = await cmd.ExecuteScalarAsync();
            if (result != null && long.TryParse(result.ToString(), out long newId))
            {
                item.Id = newId;
                return newId;
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLite UNIQUE constraint failed
        {
            // Already recorded
            return -1;
        }

        return -1;
    }

    public async Task<bool> NotificationExistsAsync(string notificationId)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Notifications WHERE NotificationId = @NotificationId;";
        cmd.Parameters.AddWithValue("@NotificationId", notificationId);
        long count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return count > 0;
    }

    public async Task<bool> UpdateNotificationStatusAsync(long id, NotificationStatus status)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Notifications
            SET Status = @Status, LastUpdatedAt = @Now
            WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Status", (int)status);
        cmd.Parameters.AddWithValue("@Now", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@Id", id);
        int affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }

    public async Task<int> BulkUpdateStatusAsync(IEnumerable<long> ids, NotificationStatus status)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var transaction = conn.BeginTransaction();
        int count = 0;
        string now = DateTime.Now.ToString("o");

        foreach (var id in ids)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE Notifications SET Status = @Status, LastUpdatedAt = @Now WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Status", (int)status);
            cmd.Parameters.AddWithValue("@Now", now);
            cmd.Parameters.AddWithValue("@Id", id);
            count += await cmd.ExecuteNonQueryAsync();
        }

        transaction.Commit();
        return count;
    }

    public async Task<bool> UpdateNotificationNotesAsync(long id, string? notes)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Notifications
            SET UserNotes = @Notes, LastUpdatedAt = @Now
            WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Now", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@Id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> UpdateNotificationPriorityAsync(long id, NotificationPriority priority)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Notifications
            SET Priority = @Priority, LastUpdatedAt = @Now
            WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Priority", (int)priority);
        cmd.Parameters.AddWithValue("@Now", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@Id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> UpdateNotificationCategoryAsync(long id, string category)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Notifications
            SET Category = @Category, LastUpdatedAt = @Now
            WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Category", category);
        cmd.Parameters.AddWithValue("@Now", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@Id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> UpdateForwardingStatusAsync(long id, ForwardingStatus status)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Notifications
            SET ForwardingStatus = @ForwardingStatus, LastUpdatedAt = @Now
            WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@ForwardingStatus", (int)status);
        cmd.Parameters.AddWithValue("@Now", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@Id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> UpdateLaunchUriAsync(long id, string launchUri)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Notifications
            SET LaunchUri = @LaunchUri, LastUpdatedAt = @Now
            WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@LaunchUri", launchUri);
        cmd.Parameters.AddWithValue("@Now", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@Id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<NotificationItem?> GetNotificationByIdAsync(long id)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Notifications WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadNotificationFromReader(reader);
        }

        return null;
    }

    public async Task<List<NotificationItem>> GetNotificationsAsync(NotificationFilter filter, int limit = 50, int offset = 0)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();

        var (whereSql, parameters) = BuildFilterQuery(filter);
        string sortOrder = filter.SortDescending ? "DESC" : "ASC";

        cmd.CommandText = $@"
            SELECT * FROM Notifications
            {whereSql}
            ORDER BY ReceivedAt {sortOrder}
            LIMIT @Limit OFFSET @Offset;";

        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }
        cmd.Parameters.AddWithValue("@Limit", limit <= 0 || limit == int.MaxValue ? -1 : limit);
        cmd.Parameters.AddWithValue("@Offset", Math.Max(0, offset));

        var list = new List<NotificationItem>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadNotificationFromReader(reader));
        }

        return list;
    }

    public async Task<int> GetNotificationCountAsync(NotificationFilter filter)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();

        var (whereSql, parameters) = BuildFilterQuery(filter);
        cmd.CommandText = $"SELECT COUNT(*) FROM Notifications {whereSql};";

        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        long count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return (int)count;
    }

    public async Task<KpiSummary> GetKpiSummaryAsync()
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        
        string todayStart = DateTime.Today.ToString("o");
        cmd.CommandText = @"
            SELECT
                COUNT(*) AS TotalCount,
                SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END) AS PendingCount,
                SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END) AS InProgressCount,
                SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) AS CompletedCount,
                SUM(CASE WHEN ReceivedAt >= @TodayStart THEN 1 ELSE 0 END) AS ReceivedTodayCount
            FROM Notifications;";
        cmd.Parameters.AddWithValue("@TodayStart", todayStart);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new KpiSummary
            {
                TotalCount = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetInt64(0)),
                PendingCount = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetInt64(1)),
                InProgressCount = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetInt64(2)),
                CompletedCount = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetInt64(3)),
                ReceivedTodayCount = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetInt64(4))
            };
        }

        return new KpiSummary();
    }

    public async Task<List<string>> GetDistinctAppNamesAsync()
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT AppName FROM Notifications WHERE AppName IS NOT NULL AND AppName != '' ORDER BY AppName ASC;";

        var list = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    public async Task<List<SourceChartItem>> GetSourceDistributionAsync()
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                AppName,
                COUNT(*) AS TotalCount,
                SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) AS DoneCount,
                SUM(CASE WHEN Status != 2 THEN 1 ELSE 0 END) AS PendingCount
            FROM Notifications
            WHERE AppName IS NOT NULL AND AppName != ''
            GROUP BY AppName
            ORDER BY TotalCount DESC
            LIMIT 12;";

        var items = new List<SourceChartItem>();
        int grandTotal = 0;
        int maxCount = 0;

        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                int total = Convert.ToInt32(reader.GetInt64(1));
                int done = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetInt64(2));
                int pending = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetInt64(3));

                grandTotal += total;
                if (total > maxCount) maxCount = total;

                items.Add(new SourceChartItem
                {
                    AppName = reader.GetString(0),
                    TotalCount = total,
                    DoneCount = done,
                    PendingCount = pending
                });
            }
        }

        foreach (var item in items)
        {
            item.Percentage = grandTotal > 0 ? (item.TotalCount * 100.0 / grandTotal) : 0;
            item.BarWidth = maxCount > 0 ? Math.Max(8, (item.TotalCount * 320.0 / maxCount)) : 8;
        }

        return items;
    }

    public async Task<List<TimelineChartItem>> GetActivityTimelineAsync(int days)
    {
        if (days <= 0) days = 7;
        DateTime startDate = DateTime.Today.AddDays(-(days - 1));

        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT substr(ReceivedAt, 1, 10) AS DayStr, COUNT(*) AS DayCount
            FROM Notifications
            WHERE ReceivedAt >= @StartDate
            GROUP BY DayStr
            ORDER BY DayStr ASC;";
        cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("o"));

        var countsByDay = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    countsByDay[reader.GetString(0)] = Convert.ToInt32(reader.GetInt64(1));
                }
            }
        }

        var timeline = new List<TimelineChartItem>();
        int maxCount = 0;

        for (int i = 0; i < days; i++)
        {
            DateTime d = startDate.AddDays(i);
            string key = d.ToString("yyyy-MM-dd");
            int count = countsByDay.TryGetValue(key, out int c) ? c : 0;
            if (count > maxCount) maxCount = count;

            timeline.Add(new TimelineChartItem
            {
                Date = d,
                Label = days <= 7 ? d.ToString("ddd dd") : d.ToString("MM/dd"),
                FullDateText = $"{d:MMM dd, yyyy}: {count} notification(s)",
                Count = count
            });
        }

        foreach (var point in timeline)
        {
            point.BarHeight = maxCount > 0 ? Math.Max(6, (point.Count * 165.0 / maxCount)) : 6;
        }

        return timeline;
    }

    public async Task<bool> DeleteNotificationAsync(long id)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Notifications WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<int> BulkDeleteAsync(IEnumerable<long> ids)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var transaction = conn.BeginTransaction();
        int count = 0;

        foreach (var id in ids)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM Notifications WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            count += await cmd.ExecuteNonQueryAsync();
        }

        transaction.Commit();
        return count;
    }

    public async Task<int> DeleteAllHistoryAsync()
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Notifications; DELETE FROM ForwardingLogs;";
        return await cmd.ExecuteNonQueryAsync();
    }

    // Categories
    public async Task<List<Category>> GetCategoriesAsync()
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.Id, c.Name, c.Description, c.IconGlyph, c.ColorHex, c.IsSystem,
                   COUNT(n.Id) AS NotificationCount
            FROM Categories c
            LEFT JOIN Notifications n ON c.Name = n.Category
            GROUP BY c.Id
            ORDER BY c.IsSystem DESC, c.Name ASC;";

        var list = new List<Category>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Category
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                IconGlyph = reader.IsDBNull(3) ? "📁" : reader.GetString(3),
                ColorHex = reader.IsDBNull(4) ? "#4A90E2" : reader.GetString(4),
                IsSystem = reader.GetInt32(5) == 1,
                NotificationCount = Convert.ToInt32(reader.GetInt64(6))
            });
        }

        return list;
    }

    public async Task<int> AddCategoryAsync(Category category)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Categories (Name, Description, IconGlyph, ColorHex, IsSystem)
            VALUES (@Name, @Desc, @Icon, @Color, @IsSystem);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@Name", category.Name);
        cmd.Parameters.AddWithValue("@Desc", category.Description ?? "");
        cmd.Parameters.AddWithValue("@Icon", category.IconGlyph ?? "📁");
        cmd.Parameters.AddWithValue("@Color", category.ColorHex ?? "#4A90E2");
        cmd.Parameters.AddWithValue("@IsSystem", category.IsSystem ? 1 : 0);

        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt32(result) : -1;
    }

    public async Task<bool> UpdateCategoryAsync(Category category)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Categories
            SET Description = @Desc, IconGlyph = @Icon, ColorHex = @Color
            WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Desc", category.Description ?? "");
        cmd.Parameters.AddWithValue("@Icon", category.IconGlyph ?? "📁");
        cmd.Parameters.AddWithValue("@Color", category.ColorHex ?? "#4A90E2");
        cmd.Parameters.AddWithValue("@Id", category.Id);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteCategoryAsync(int id)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Categories WHERE Id = @Id AND IsSystem = 0;";
        cmd.Parameters.AddWithValue("@Id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // Rules
    public async Task<List<CategoryRule>> GetRulesAsync()
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, CategoryName, MatchType, Pattern, Priority, IsActive FROM CategoryRules ORDER BY Priority ASC;";

        var list = new List<CategoryRule>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CategoryRule
            {
                Id = reader.GetInt32(0),
                CategoryName = reader.GetString(1),
                MatchType = (RuleMatchType)reader.GetInt32(2),
                Pattern = reader.GetString(3),
                Priority = reader.GetInt32(4),
                IsActive = reader.GetInt32(5) == 1
            });
        }

        return list;
    }

    public async Task<int> AddRuleAsync(CategoryRule rule)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO CategoryRules (CategoryName, MatchType, Pattern, Priority, IsActive)
            VALUES (@Cat, @Type, @Pattern, @Prio, @Active);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@Cat", rule.CategoryName);
        cmd.Parameters.AddWithValue("@Type", (int)rule.MatchType);
        cmd.Parameters.AddWithValue("@Pattern", rule.Pattern);
        cmd.Parameters.AddWithValue("@Prio", rule.Priority);
        cmd.Parameters.AddWithValue("@Active", rule.IsActive ? 1 : 0);

        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt32(result) : -1;
    }

    public async Task<bool> UpdateRuleAsync(CategoryRule rule)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE CategoryRules
            SET CategoryName = @Cat, MatchType = @Type, Pattern = @Pattern, Priority = @Prio, IsActive = @Active
            WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Cat", rule.CategoryName);
        cmd.Parameters.AddWithValue("@Type", (int)rule.MatchType);
        cmd.Parameters.AddWithValue("@Pattern", rule.Pattern);
        cmd.Parameters.AddWithValue("@Prio", rule.Priority);
        cmd.Parameters.AddWithValue("@Active", rule.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@Id", rule.Id);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteRuleAsync(int id)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CategoryRules WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // Forwarding Logs
    public async Task<long> InsertForwardingLogAsync(ForwardingLog log)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ForwardingLogs (NotificationId, AppName, NotificationTitle, DestinationType, Status, AttemptCount, Timestamp, ErrorDetails)
            VALUES (@NotifId, @AppName, @Title, @Dest, @Status, @Attempt, @Timestamp, @Err);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@NotifId", log.NotificationId);
        cmd.Parameters.AddWithValue("@AppName", log.AppName ?? "");
        cmd.Parameters.AddWithValue("@Title", log.NotificationTitle ?? "");
        cmd.Parameters.AddWithValue("@Dest", (int)log.DestinationType);
        cmd.Parameters.AddWithValue("@Status", log.Status);
        cmd.Parameters.AddWithValue("@Attempt", log.AttemptCount);
        cmd.Parameters.AddWithValue("@Timestamp", log.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@Err", (object?)log.ErrorDetails ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt64(result) : -1;
    }

    public async Task<List<ForwardingLog>> GetForwardingLogsAsync(int limit = 200, long? notificationId = null)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();

        if (notificationId.HasValue)
        {
            cmd.CommandText = "SELECT * FROM ForwardingLogs WHERE NotificationId = @NotifId ORDER BY Timestamp DESC LIMIT @Limit;";
            cmd.Parameters.AddWithValue("@NotifId", notificationId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT * FROM ForwardingLogs ORDER BY Timestamp DESC LIMIT @Limit;";
        }
        cmd.Parameters.AddWithValue("@Limit", limit);

        var list = new List<ForwardingLog>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ForwardingLog
            {
                Id = reader.GetInt64(0),
                NotificationId = reader.GetInt64(1),
                AppName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                NotificationTitle = reader.IsDBNull(3) ? "" : reader.GetString(3),
                DestinationType = (DestinationType)reader.GetInt32(4),
                Status = reader.GetString(5),
                AttemptCount = reader.GetInt32(6),
                Timestamp = DateTime.Parse(reader.GetString(7)),
                ErrorDetails = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return list;
    }

    public async Task<List<ForwardingLog>> GetFailedForwardingLogsAsync()
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ForwardingLogs WHERE Status = 'Failed' ORDER BY Timestamp DESC LIMIT 100;";

        var list = new List<ForwardingLog>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ForwardingLog
            {
                Id = reader.GetInt64(0),
                NotificationId = reader.GetInt64(1),
                AppName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                NotificationTitle = reader.IsDBNull(3) ? "" : reader.GetString(3),
                DestinationType = (DestinationType)reader.GetInt32(4),
                Status = reader.GetString(5),
                AttemptCount = reader.GetInt32(6),
                Timestamp = DateTime.Parse(reader.GetString(7)),
                ErrorDetails = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return list;
    }

    // Settings
    public async Task<AppSettings> LoadSettingsAsync()
    {
        using var conn = await CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM AppSettingsData;";

        var dict = new Dictionary<string, string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }

        var settings = new AppSettings();
        if (dict.TryGetValue("MonitoringEnabled", out var mon)) settings.MonitoringEnabled = bool.Parse(mon);
        if (dict.TryGetValue("MinimizeToTray", out var mtt)) settings.MinimizeToTray = bool.Parse(mtt);
        if (dict.TryGetValue("LaunchOnStartup", out var los)) settings.LaunchOnStartup = bool.Parse(los);
        if (dict.TryGetValue("Theme", out var th)) settings.Theme = th;
        if (dict.TryGetValue("DbBackupPath", out var dbp)) settings.DbBackupPath = dbp;
        if (dict.TryGetValue("RetentionDays", out var rd)) settings.RetentionDays = int.Parse(rd);
        if (dict.TryGetValue("ZoomScale", out var zs) && double.TryParse(zs, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedZoom))
            settings.ZoomScale = parsedZoom;

        if (dict.TryGetValue("ExcludedApps", out var ea)) settings.ExcludedApps = JsonSerializer.Deserialize<List<string>>(ea) ?? new();
        if (dict.TryGetValue("ExcludedCategories", out var ec)) settings.ExcludedCategories = JsonSerializer.Deserialize<List<string>>(ec) ?? new();

        // Telegram
        if (dict.TryGetValue("TelegramEnabled", out var te)) settings.TelegramEnabled = bool.Parse(te);
        if (dict.TryGetValue("TelegramChatId", out var tci)) settings.TelegramChatId = tci;
        if (dict.TryGetValue("TelegramMessageTemplate", out var tmt) && !string.IsNullOrWhiteSpace(tmt))
            settings.TelegramMessageTemplate = tmt;

        // Email
        if (dict.TryGetValue("EmailEnabled", out var ee)) settings.EmailEnabled = bool.Parse(ee);
        if (dict.TryGetValue("SmtpHost", out var sh)) settings.SmtpHost = sh;
        if (dict.TryGetValue("SmtpPort", out var sp)) settings.SmtpPort = int.Parse(sp);
        if (dict.TryGetValue("SmtpUseSsl", out var ss)) settings.SmtpUseSsl = bool.Parse(ss);
        if (dict.TryGetValue("SmtpUsername", out var su)) settings.SmtpUsername = su;
        if (dict.TryGetValue("SmtpFromEmail", out var sfe)) settings.SmtpFromEmail = sfe;
        if (dict.TryGetValue("SmtpToEmail", out var ste)) settings.SmtpToEmail = ste;

        // Google Drive Backup
        if (dict.TryGetValue("GoogleDriveBackupEnabled", out var gdbe) && bool.TryParse(gdbe, out bool gdEnabled))
            settings.GoogleDriveBackupEnabled = gdEnabled;
        if (dict.TryGetValue("GoogleDriveBackupIntervalValue", out var gdiv) && int.TryParse(gdiv, out int gdIntervalVal))
            settings.GoogleDriveBackupIntervalValue = gdIntervalVal;
        if (dict.TryGetValue("GoogleDriveBackupIntervalUnit", out var gdiu) && !string.IsNullOrWhiteSpace(gdiu))
            settings.GoogleDriveBackupIntervalUnit = gdiu;
        if (dict.TryGetValue("GoogleDriveMaxBackupsToKeep", out var gdmax) && int.TryParse(gdmax, out int gdMaxKeep))
            settings.GoogleDriveMaxBackupsToKeep = gdMaxKeep;
        if (dict.TryGetValue("GoogleDriveMaxSpaceValue", out var gdspv) && int.TryParse(gdspv, out int gdSpaceVal))
            settings.GoogleDriveMaxSpaceValue = gdSpaceVal;
        if (dict.TryGetValue("GoogleDriveMaxSpaceUnit", out var gdspu) && !string.IsNullOrWhiteSpace(gdspu))
            settings.GoogleDriveMaxSpaceUnit = gdspu;
        if (dict.TryGetValue("LastGoogleDriveBackupAt", out var gdLast) && DateTime.TryParse(gdLast, out DateTime parsedLast))
            settings.LastGoogleDriveBackupAt = parsedLast;

        // Load sensitive secrets from Credential Manager
        settings.TelegramBotToken = _secureStorage.GetSecret("TelegramBotToken") ?? "";
        settings.SmtpPassword = _secureStorage.GetSecret("SmtpPassword") ?? "";
        settings.GoogleDriveClientId = _secureStorage.GetSecret("GoogleDriveClientId") ?? "";
        settings.GoogleDriveClientSecret = _secureStorage.GetSecret("GoogleDriveClientSecret") ?? "";

        return settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        using var conn = await CreateOpenConnectionAsync();
        using var transaction = conn.BeginTransaction();

        var dict = new Dictionary<string, string>
        {
            ["MonitoringEnabled"] = settings.MonitoringEnabled.ToString(),
            ["MinimizeToTray"] = settings.MinimizeToTray.ToString(),
            ["LaunchOnStartup"] = settings.LaunchOnStartup.ToString(),
            ["Theme"] = settings.Theme,
            ["DbBackupPath"] = settings.DbBackupPath,
            ["RetentionDays"] = settings.RetentionDays.ToString(),
            ["ZoomScale"] = settings.ZoomScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ExcludedApps"] = JsonSerializer.Serialize(settings.ExcludedApps),
            ["ExcludedCategories"] = JsonSerializer.Serialize(settings.ExcludedCategories),
            ["TelegramEnabled"] = settings.TelegramEnabled.ToString(),
            ["TelegramChatId"] = settings.TelegramChatId,
            ["TelegramMessageTemplate"] = string.IsNullOrWhiteSpace(settings.TelegramMessageTemplate)
                ? AppSettings.DefaultTelegramTemplate
                : settings.TelegramMessageTemplate,
            ["EmailEnabled"] = settings.EmailEnabled.ToString(),
            ["SmtpHost"] = settings.SmtpHost,
            ["SmtpPort"] = settings.SmtpPort.ToString(),
            ["SmtpUseSsl"] = settings.SmtpUseSsl.ToString(),
            ["SmtpUsername"] = settings.SmtpUsername,
            ["SmtpFromEmail"] = settings.SmtpFromEmail,
            ["SmtpToEmail"] = settings.SmtpToEmail,
            ["GoogleDriveBackupEnabled"] = settings.GoogleDriveBackupEnabled.ToString(),
            ["GoogleDriveBackupIntervalValue"] = Math.Max(1, settings.GoogleDriveBackupIntervalValue).ToString(),
            ["GoogleDriveBackupIntervalUnit"] = settings.GoogleDriveBackupIntervalUnit,
            ["GoogleDriveMaxBackupsToKeep"] = Math.Clamp(settings.GoogleDriveMaxBackupsToKeep, 1, 100).ToString(),
            ["GoogleDriveMaxSpaceValue"] = Math.Max(0, settings.GoogleDriveMaxSpaceValue).ToString(),
            ["GoogleDriveMaxSpaceUnit"] = settings.GoogleDriveMaxSpaceUnit == "GB" ? "GB" : "MB",
            ["LastGoogleDriveBackupAt"] = settings.LastGoogleDriveBackupAt?.ToString("o") ?? string.Empty
        };

        foreach (var kvp in dict)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR REPLACE INTO AppSettingsData (Key, Value) VALUES (@K, @V);";
            cmd.Parameters.AddWithValue("@K", kvp.Key);
            cmd.Parameters.AddWithValue("@V", kvp.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        transaction.Commit();

        // Securely store credentials via Windows Credential Manager
        SaveOrDeleteSecret("TelegramBotToken", settings.TelegramBotToken);
        SaveOrDeleteSecret("SmtpPassword", settings.SmtpPassword);
        SaveOrDeleteSecret("GoogleDriveClientId", settings.GoogleDriveClientId);
        SaveOrDeleteSecret("GoogleDriveClientSecret", settings.GoogleDriveClientSecret);
    }

    private void SaveOrDeleteSecret(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _secureStorage.SaveSecret(key, value.Trim());
        else
            _secureStorage.DeleteSecret(key);
    }

    // Database Maintenance
    public async Task<bool> BackupDatabaseAsync(string destinationFilePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var conn = await CreateOpenConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO @Dest;";
            cmd.Parameters.AddWithValue("@Dest", destinationFilePath);
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RestoreDatabaseAsync(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath)) return false;

        try
        {
            // Clear current connection pools
            SqliteConnection.ClearAllPools();
            string backupTemp = _dbPath + ".bak";
            if (File.Exists(_dbPath))
            {
                File.Copy(_dbPath, backupTemp, true);
            }

            File.Copy(sourceFilePath, _dbPath, true);
            // Verify restored database integrity
            using (var conn = await CreateOpenConnectionAsync())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA integrity_check;";
                var result = (string?)await cmd.ExecuteScalarAsync();
                if (result != "ok")
                {
                    // Rollback
                    if (File.Exists(backupTemp)) File.Copy(backupTemp, _dbPath, true);
                    return false;
                }
            }

            if (File.Exists(backupTemp)) File.Delete(backupTemp);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (string WhereSql, List<SqliteParameter> Parameters) BuildFilterQuery(NotificationFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            string search = filter.SearchText.Trim();

            // Searching "N-123" (or "n-123") jumps directly to that record key
            if (search.Length > 2 &&
                search.StartsWith("N-", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(search.Substring(2), out long recordId))
            {
                clauses.Add("Id = @RecordId");
                parameters.Add(new SqliteParameter("@RecordId", recordId));
            }
            else
            {
                clauses.Add("(Title LIKE @Search OR Body LIKE @Search OR AppName LIKE @Search OR UserNotes LIKE @Search)");
                parameters.Add(new SqliteParameter("@Search", $"%{search}%"));
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.AppName) && filter.AppName != "All")
        {
            clauses.Add("AppName = @AppName");
            parameters.Add(new SqliteParameter("@AppName", filter.AppName));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category) && filter.Category != "All")
        {
            clauses.Add("Category = @Category");
            parameters.Add(new SqliteParameter("@Category", filter.Category));
        }

        if (filter.Status.HasValue)
        {
            clauses.Add("Status = @Status");
            parameters.Add(new SqliteParameter("@Status", (int)filter.Status.Value));
        }

        if (filter.Priority.HasValue)
        {
            clauses.Add("Priority = @Priority");
            parameters.Add(new SqliteParameter("@Priority", (int)filter.Priority.Value));
        }

        if (filter.FromDate.HasValue)
        {
            clauses.Add("ReceivedAt >= @FromDate");
            parameters.Add(new SqliteParameter("@FromDate", filter.FromDate.Value.ToString("o")));
        }

        if (filter.ToDate.HasValue)
        {
            clauses.Add("ReceivedAt <= @ToDate");
            parameters.Add(new SqliteParameter("@ToDate", filter.ToDate.Value.ToString("o")));
        }

        string whereSql = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
        return (whereSql, parameters);
    }

    private static NotificationItem ReadNotificationFromReader(SqliteDataReader reader)
    {
        string? rawMetadata = reader.IsDBNull(reader.GetOrdinal("RawMetadata")) ? null : reader.GetString(reader.GetOrdinal("RawMetadata"));
        string? launchUri = null;

        try
        {
            int launchCol = reader.GetOrdinal("LaunchUri");
            if (!reader.IsDBNull(launchCol))
            {
                launchUri = reader.GetString(launchCol);
            }
        }
        catch { }

        // Fallback: extract LaunchUri from RawMetadata if column was null or not yet set
        if (string.IsNullOrEmpty(launchUri) && !string.IsNullOrEmpty(rawMetadata))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(rawMetadata);
                if (doc.RootElement.TryGetProperty("LaunchUri", out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    launchUri = prop.GetString();
                }
            }
            catch { }
        }

        return new NotificationItem
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            NotificationId = reader.GetString(reader.GetOrdinal("NotificationId")),
            AppName = reader.IsDBNull(reader.GetOrdinal("AppName")) ? "Unknown" : reader.GetString(reader.GetOrdinal("AppName")),
            AppId = reader.IsDBNull(reader.GetOrdinal("AppId")) ? "" : reader.GetString(reader.GetOrdinal("AppId")),
            Title = reader.IsDBNull(reader.GetOrdinal("Title")) ? "(No Title)" : reader.GetString(reader.GetOrdinal("Title")),
            Body = reader.IsDBNull(reader.GetOrdinal("Body")) ? "(No Content)" : reader.GetString(reader.GetOrdinal("Body")),
            Category = reader.IsDBNull(reader.GetOrdinal("Category")) ? "Other" : reader.GetString(reader.GetOrdinal("Category")),
            ReceivedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("ReceivedAt"))),
            Status = (NotificationStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            Priority = (NotificationPriority)reader.GetInt32(reader.GetOrdinal("Priority")),
            UserNotes = reader.IsDBNull(reader.GetOrdinal("UserNotes")) ? null : reader.GetString(reader.GetOrdinal("UserNotes")),
            LastUpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastUpdatedAt"))),
            ForwardingStatus = (ForwardingStatus)reader.GetInt32(reader.GetOrdinal("ForwardingStatus")),
            RawMetadata = rawMetadata,
            LaunchUri = launchUri
        };
    }
}
