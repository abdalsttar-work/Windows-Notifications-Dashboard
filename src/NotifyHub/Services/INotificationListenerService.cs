using System;
using System.Threading.Tasks;
using NotifyHub.Models;

namespace NotifyHub.Services;

public interface INotificationListenerService
{
    bool IsRunning { get; }
    bool IsPaused { get; }
    string AccessStatus { get; }
    DateTime? LastCaptureTime { get; }
    int TotalCapturedThisSession { get; }

    event EventHandler<NotificationItem>? NotificationCaptured;
    event EventHandler<string>? MonitoringStatusChanged;

    Task StartAsync();
    Task StopAsync();
    void Pause();
    void Resume();
    Task TriggerManualSyncAsync();
    void OpenNotificationSettings();
}
