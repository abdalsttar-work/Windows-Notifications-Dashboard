namespace NotifyHub.Models;

public enum NotificationStatus
{
    Pending = 0,
    InProgress = 1,
    Done = 2,
    Archived = 3
}

public enum NotificationPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public enum ForwardingStatus
{
    None = 0,
    Pending = 1,
    Success = 2,
    PartialSuccess = 3,
    Failed = 4
}

public enum DestinationType
{
    Telegram = 0,
    Email = 1
}

public enum RuleMatchType
{
    AppNameEquals = 0,
    AppNameContains = 1,
    TitleContains = 2,
    BodyContains = 3,
    ContentContains = 4,
    RegexMatch = 5
}
