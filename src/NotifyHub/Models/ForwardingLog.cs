using System;

namespace NotifyHub.Models;

public class ForwardingLog
{
    public long Id { get; set; }
    public long NotificationId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string NotificationTitle { get; set; } = string.Empty;
    public DestinationType DestinationType { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; } = 1;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? ErrorDetails { get; set; }

    public string DestinationName => DestinationType == DestinationType.Telegram ? "Telegram" : "Email";
    public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    public bool IsSuccess => Status.Equals("Success", StringComparison.OrdinalIgnoreCase);
}
