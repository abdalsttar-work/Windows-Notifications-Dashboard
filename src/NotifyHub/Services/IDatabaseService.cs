using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NotifyHub.Models;

namespace NotifyHub.Services;

public class NotificationFilter
{
    public string? SearchText { get; set; }
    public string? AppName { get; set; }
    public string? Category { get; set; }
    public NotificationStatus? Status { get; set; }
    public NotificationPriority? Priority { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public bool SortDescending { get; set; } = true;
}

public class KpiSummary
{
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int InProgressCount { get; set; }
    public int CompletedCount { get; set; }
    public int ReceivedTodayCount { get; set; }
}

public class SourceChartItem
{
    public string AppName { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int DoneCount { get; set; }
    public int PendingCount { get; set; }
    public double Percentage { get; set; }
    public double BarWidth { get; set; }
    public string SummaryText => $"{TotalCount} ({Percentage:F0}%)  •  {DoneCount} Done / {PendingCount} Pending";
}

public class TimelineChartItem
{
    public DateTime Date { get; set; }
    public string Label { get; set; } = string.Empty;
    public string FullDateText { get; set; } = string.Empty;
    public int Count { get; set; }
    public double BarHeight { get; set; }
}

public interface IDatabaseService
{
    string DatabasePath { get; }
    Task InitializeDatabaseAsync();

    // Notifications
    Task<long> InsertNotificationAsync(NotificationItem item);
    Task<bool> NotificationExistsAsync(string notificationId);
    Task<bool> UpdateNotificationStatusAsync(long id, NotificationStatus status);
    Task<int> BulkUpdateStatusAsync(IEnumerable<long> ids, NotificationStatus status);
    Task<bool> UpdateNotificationNotesAsync(long id, string? notes);
    Task<bool> UpdateNotificationPriorityAsync(long id, NotificationPriority priority);
    Task<bool> UpdateNotificationCategoryAsync(long id, string category);
    Task<bool> UpdateForwardingStatusAsync(long id, ForwardingStatus status);
    Task<bool> UpdateLaunchUriAsync(long id, string launchUri);
    Task<NotificationItem?> GetNotificationByIdAsync(long id);
    Task<List<NotificationItem>> GetNotificationsAsync(NotificationFilter filter, int limit = 50, int offset = 0);
    Task<int> GetNotificationCountAsync(NotificationFilter filter);
    Task<KpiSummary> GetKpiSummaryAsync();
    Task<List<string>> GetDistinctAppNamesAsync();
    Task<List<SourceChartItem>> GetSourceDistributionAsync();
    Task<List<TimelineChartItem>> GetActivityTimelineAsync(int days);
    Task<bool> DeleteNotificationAsync(long id);
    Task<int> BulkDeleteAsync(IEnumerable<long> ids);
    Task<int> DeleteAllHistoryAsync();

    // Categories
    Task<List<Category>> GetCategoriesAsync();
    Task<int> AddCategoryAsync(Category category);
    Task<bool> UpdateCategoryAsync(Category category);
    Task<bool> DeleteCategoryAsync(int id);

    // Rules
    Task<List<CategoryRule>> GetRulesAsync();
    Task<int> AddRuleAsync(CategoryRule rule);
    Task<bool> UpdateRuleAsync(CategoryRule rule);
    Task<bool> DeleteRuleAsync(int id);

    // Forwarding Logs
    Task<long> InsertForwardingLogAsync(ForwardingLog log);
    Task<List<ForwardingLog>> GetForwardingLogsAsync(int limit = 200, long? notificationId = null);
    Task<List<ForwardingLog>> GetFailedForwardingLogsAsync();

    // Settings
    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);

    // Maintenance
    Task<bool> BackupDatabaseAsync(string destinationFilePath);
    Task<bool> RestoreDatabaseAsync(string sourceFilePath);
}
