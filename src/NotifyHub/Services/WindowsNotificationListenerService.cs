using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using NotifyHub.Models;

namespace NotifyHub.Services;

public class WindowsNotificationListenerService : INotificationListenerService
{
    private readonly IDatabaseService _databaseService;
    private readonly ICategorizationService _categorizationService;
    private readonly ITelegramForwardingService _telegramService;
    private readonly IEmailForwardingService _emailService;

    private UserNotificationListener? _listener;
    private System.Threading.Timer? _syncTimer;
    private System.Threading.Timer? _watchdogTimer;
    private readonly HashSet<string> _seenNotificationIds = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _isRunning;
    private bool _isPaused;
    private string _accessStatus = "Unchecked";
    private DateTime? _lastCaptureTime;
    private int _sessionCaptureCount;

    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;
    public string AccessStatus => _accessStatus;
    public DateTime? LastCaptureTime => _lastCaptureTime;
    public int TotalCapturedThisSession => _sessionCaptureCount;

    public event EventHandler<NotificationItem>? NotificationCaptured;
    public event EventHandler<string>? MonitoringStatusChanged;

    public WindowsNotificationListenerService(
        IDatabaseService databaseService,
        ICategorizationService categorizationService,
        ITelegramForwardingService telegramService,
        IEmailForwardingService emailService)
    {
        _databaseService = databaseService;
        _categorizationService = categorizationService;
        _telegramService = telegramService;
        _emailService = emailService;
    }

    public async Task StartAsync()
    {
        if (_isRunning) return;

        bool isSupported = ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener");
        if (!isSupported)
        {
            _accessStatus = "Not Supported";
            MonitoringStatusChanged?.Invoke(this, "UserNotificationListener is not supported on this Windows version.");
            return;
        }

        try
        {
            _listener = UserNotificationListener.Current;
            var status = _listener.GetAccessStatus();
            _accessStatus = status.ToString();

            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                try
                {
                    status = await _listener.RequestAccessAsync();
                    _accessStatus = status.ToString();
                }
                catch
                {
                    // Unpackaged Win32 desktop apps cannot trigger UWP permission dialogs
                }
            }

            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                _isRunning = false;
                MonitoringStatusChanged?.Invoke(this, $"Notification access is {_accessStatus}. Enable in Windows Settings > Privacy & security > Notifications.");
                StartPermissionWatchdog();
                return;
            }

            // Access is allowed! Stop watchdog if running
            _watchdogTimer?.Dispose();
            _watchdogTimer = null;

            MonitoringStatusChanged?.Invoke(this, "Notification access allowed. Monitoring active.");

            // Subscribe to real-time OS event
            try
            {
                _listener.NotificationChanged -= Listener_NotificationChanged;
                _listener.NotificationChanged += Listener_NotificationChanged;
            }
            catch
            {
                // Some Windows builds require polling for unpackaged apps
            }

            _isRunning = true;
            _isPaused = false;

            // Start fallback synchronization timer (every 3.5 seconds)
            _syncTimer?.Dispose();
            _syncTimer = new System.Threading.Timer(
                async _ => await SyncNotificationsSafeAsync(),
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3.5));

            // Perform initial scan
            await SyncNotificationsSafeAsync();
        }
        catch (Exception ex)
        {
            _accessStatus = "Error";
            MonitoringStatusChanged?.Invoke(this, $"Notification listener issue: {ex.Message}. Check Windows Notification permissions.");
            StartPermissionWatchdog();
        }
    }

    private void StartPermissionWatchdog()
    {
        if (_watchdogTimer != null) return;

        // Periodically probe if user turned on permission in Windows Settings
        _watchdogTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                if (_listener != null && _listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed)
                {
                    _watchdogTimer?.Dispose();
                    _watchdogTimer = null;
                    await StartAsync();
                }
            }
            catch
            {
                // Ignore transient probe errors
            }
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4));
    }

    public void OpenNotificationSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true });
            }
            catch { }
        }
    }

    public Task StopAsync()
    {
        _isRunning = false;
        _syncTimer?.Dispose();
        _syncTimer = null;
        _watchdogTimer?.Dispose();
        _watchdogTimer = null;

        if (_listener != null)
        {
            try
            {
                _listener.NotificationChanged -= Listener_NotificationChanged;
            }
            catch
            {
                // Ignore
            }
        }

        MonitoringStatusChanged?.Invoke(this, "Notification monitoring stopped.");
        return Task.CompletedTask;
    }

    public void Pause()
    {
        _isPaused = true;
        MonitoringStatusChanged?.Invoke(this, "Notification monitoring paused.");
    }

    public void Resume()
    {
        _isPaused = false;
        MonitoringStatusChanged?.Invoke(this, "Notification monitoring resumed.");
    }

    public async Task TriggerManualSyncAsync()
    {
        await SyncNotificationsSafeAsync();
    }

    private async void Listener_NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (!_isRunning || _isPaused) return;

        if (args.ChangeKind == UserNotificationChangedKind.Added)
        {
            try
            {
                var notif = sender.GetNotification(args.UserNotificationId);
                if (notif != null)
                {
                    await ProcessSingleNotificationAsync(notif);
                }
            }
            catch
            {
                // If GetNotification by single ID throws, fallback to sync
                await SyncNotificationsSafeAsync();
            }
        }
    }

    private async Task SyncNotificationsSafeAsync()
    {
        if (!_isRunning || _isPaused || _listener == null || _listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) return;

        if (!await _syncLock.WaitAsync(100))
        {
            return; // Another sync is already active
        }

        try
        {
            IReadOnlyList<UserNotification> notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var notif in notifications)
            {
                if (!_isRunning || _isPaused) break;
                await ProcessSingleNotificationAsync(notif);
            }
        }
        catch (Exception ex)
        {
            // Transient error while querying notifications
            System.Diagnostics.Debug.WriteLine($"Sync error: {ex.Message}");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task ProcessSingleNotificationAsync(UserNotification notif)
    {
        string rawId = notif.Id.ToString();
        string appName = notif.AppInfo?.DisplayInfo?.DisplayName ?? "Windows System";
        string appId = notif.AppInfo?.Id ?? "";
        DateTimeOffset creationTime = notif.CreationTime;

        // Extract Visual Elements (Title, Body)
        string title = "(No Title)";
        string body = "(No Content)";

        try
        {
            NotificationBinding binding = notif.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding != null)
            {
                var texts = binding.GetTextElements();
                var textList = new List<string>();
                foreach (var t in texts)
                {
                    if (!string.IsNullOrWhiteSpace(t.Text))
                        textList.Add(t.Text.Trim());
                }

                if (textList.Count > 0)
                {
                    title = textList[0];
                }
                if (textList.Count > 1)
                {
                    body = string.Join("\n", textList.GetRange(1, textList.Count - 1));
                }
            }
        }
        catch
        {
            // Ignore visual binding extraction errors
        }

        // Composite unique ID to prevent duplicates across reboots or same user notification IDs
        string compositeId = $"{appId}_{rawId}_{creationTime.ToUnixTimeSeconds()}";

        lock (_seenNotificationIds)
        {
            if (_seenNotificationIds.Contains(compositeId))
            {
                return;
            }
            _seenNotificationIds.Add(compositeId);
        }

        // Check if already in SQLite
        if (await _databaseService.NotificationExistsAsync(compositeId))
        {
            return;
        }

        // Check settings for exclusions
        var settings = await _databaseService.LoadSettingsAsync();
        if (IsAppExcluded(appName, appId, settings.ExcludedApps))
        {
            return; // Excluded app
        }

        // Smart Categorization & Priority Determination
        string category = await _categorizationService.CategorizeAsync(appName, title, body);
        var priority = _categorizationService.DeterminePriority(appName, title, body, category);

        // Check category exclusion
        if (settings.ExcludedCategories.Contains(category))
        {
            return; // Excluded category
        }

        // Extract Windows toast launch target (for 100% parity with clicking normal Windows notification)
        string? launchUri = WindowsToastHelper.TryGetToastLaunchUri(notif.Id, appName, title, body);

        var notificationItem = new NotificationItem
        {
            NotificationId = compositeId,
            AppName = appName,
            AppId = appId,
            Title = title,
            Body = body,
            Category = category,
            ReceivedAt = creationTime.LocalDateTime,
            Status = NotificationStatus.Pending,
            Priority = priority,
            LastUpdatedAt = DateTime.Now,
            ForwardingStatus = ForwardingStatus.None,
            LaunchUri = launchUri,
            RawMetadata = JsonSerializer.Serialize(new
            {
                RawId = rawId,
                AppId = appId,
                CreationTime = creationTime,
                LaunchUri = launchUri
            })
        };

        // 1. Commit to SQLite FIRST (Guaranteed local persistence)
        long newDbId = await _databaseService.InsertNotificationAsync(notificationItem);
        if (newDbId <= 0)
        {
            return; // Duplicate caught at DB level
        }

        notificationItem.Id = newDbId;
        _lastCaptureTime = DateTime.Now;
        Interlocked.Increment(ref _sessionCaptureCount);

        // 2. Notify dashboard subscribers
        try
        {
            NotificationCaptured?.Invoke(this, notificationItem);
        }
        catch
        {
            // Ignore UI subscriber errors
        }

        // 3. Instant Asynchronous Forwarding (Background Task - never blocks capture)
        _ = Task.Run(async () =>
        {
            await HandleForwardingAsync(notificationItem, settings);
        });
    }

    public static bool IsAppExcluded(string? appName, string? appId, IEnumerable<string>? excludedApps)
    {
        if (excludedApps == null) return false;

        string cleanName = (appName ?? string.Empty).Trim();
        string cleanId = (appId ?? string.Empty).Trim();

        foreach (var raw in excludedApps)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string ex = raw.Trim();

            if (!string.IsNullOrEmpty(cleanName) && cleanName.Equals(ex, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(cleanId) && cleanId.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private async Task HandleForwardingAsync(NotificationItem item, AppSettings settings)
    {
        bool telegramSuccess = false;
        bool emailSuccess = false;
        bool anyEnabled = false;

        // Telegram Forwarding
        if (settings.TelegramEnabled && !string.IsNullOrWhiteSpace(settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            anyEnabled = true;
            telegramSuccess = await _telegramService.ForwardNotificationAsync(
                item,
                settings.TelegramBotToken,
                settings.TelegramChatId,
                settings.TelegramMessageTemplate);
        }

        // Email Forwarding
        if (settings.EmailEnabled && !string.IsNullOrWhiteSpace(settings.SmtpHost) && !string.IsNullOrWhiteSpace(settings.SmtpToEmail))
        {
            anyEnabled = true;
            emailSuccess = await _emailService.ForwardNotificationAsync(item, settings);
        }

        if (anyEnabled)
        {
            ForwardingStatus finalStatus;
            if (settings.TelegramEnabled && settings.EmailEnabled)
            {
                if (telegramSuccess && emailSuccess) finalStatus = ForwardingStatus.Success;
                else if (telegramSuccess || emailSuccess) finalStatus = ForwardingStatus.PartialSuccess;
                else finalStatus = ForwardingStatus.Failed;
            }
            else if (settings.TelegramEnabled)
            {
                finalStatus = telegramSuccess ? ForwardingStatus.Success : ForwardingStatus.Failed;
            }
            else
            {
                finalStatus = emailSuccess ? ForwardingStatus.Success : ForwardingStatus.Failed;
            }

            item.ForwardingStatus = finalStatus;
            await _databaseService.UpdateForwardingStatusAsync(item.Id, finalStatus);
        }
    }
}
