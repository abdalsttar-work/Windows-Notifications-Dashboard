using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NotifyHub.Models;

public class NotificationItem : INotifyPropertyChanged
{
    private long _id;
    private string _notificationId = string.Empty;
    private string _appName = "Unknown App";
    private string _appId = string.Empty;
    private string _title = "(No Title)";
    private string _body = "(No Content)";
    private string _category = "Other";
    private DateTime _receivedAt = DateTime.Now;
    private NotificationStatus _status = NotificationStatus.Pending;
    private NotificationPriority _priority = NotificationPriority.Normal;
    private string? _userNotes;
    private DateTime _lastUpdatedAt = DateTime.Now;
    private ForwardingStatus _forwardingStatus = ForwardingStatus.None;
    private string? _rawMetadata;
    private string? _launchUri;
    private bool _isSelected;

    public long Id
    {
        get => _id;
        set
        {
            if (SetField(ref _id, value))
                OnPropertyChanged(nameof(DisplayId));
        }
    }

    // Human-friendly sequential record key (N-1, N-2, N-3 ...) based on the SQLite primary key
    public string DisplayId => _id > 0 ? $"N-{_id}" : "N-?";

    public string NotificationId
    {
        get => _notificationId;
        set => SetField(ref _notificationId, value);
    }

    public string AppName
    {
        get => _appName;
        set => SetField(ref _appName, value);
    }

    public string AppId
    {
        get => _appId;
        set => SetField(ref _appId, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Body
    {
        get => _body;
        set => SetField(ref _body, value);
    }

    public string Category
    {
        get => _category;
        set => SetField(ref _category, value);
    }

    public DateTime ReceivedAt
    {
        get => _receivedAt;
        set
        {
            if (SetField(ref _receivedAt, value))
            {
                OnPropertyChanged(nameof(PendingDurationText));
                OnPropertyChanged(nameof(FormattedReceivedAt));
            }
        }
    }

    public NotificationStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(IsPending));
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(PendingDurationText));
            }
        }
    }

    public NotificationPriority Priority
    {
        get => _priority;
        set => SetField(ref _priority, value);
    }

    public string? UserNotes
    {
        get => _userNotes;
        set => SetField(ref _userNotes, value);
    }

    public DateTime LastUpdatedAt
    {
        get => _lastUpdatedAt;
        set => SetField(ref _lastUpdatedAt, value);
    }

    public ForwardingStatus ForwardingStatus
    {
        get => _forwardingStatus;
        set => SetField(ref _forwardingStatus, value);
    }

    public string? RawMetadata
    {
        get => _rawMetadata;
        set => SetField(ref _rawMetadata, value);
    }

    public string? LaunchUri
    {
        get => _launchUri;
        set => SetField(ref _launchUri, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    // Helper Properties
    public bool IsPending => Status == NotificationStatus.Pending;
    public bool IsDone => Status == NotificationStatus.Done;

    public string FormattedReceivedAt => ReceivedAt.ToString("MMM dd, yyyy HH:mm:ss");

    public string PendingDurationText
    {
        get
        {
            if (Status != NotificationStatus.Pending)
                return "-";

            var duration = DateTime.Now - ReceivedAt;
            if (duration.TotalMinutes < 1)
                return "Just now";
            if (duration.TotalHours < 1)
                return $"{(int)duration.TotalMinutes}m ago";
            if (duration.TotalDays < 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m ago";
            return $"{(int)duration.TotalDays}d {duration.Hours}h ago";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
