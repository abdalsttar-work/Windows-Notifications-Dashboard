using System.Threading.Tasks;
using NotifyHub.Models;

namespace NotifyHub.Services;

public interface IEmailForwardingService
{
    Task<bool> SendTestEmailAsync(AppSettings settings);
    Task<bool> ForwardNotificationAsync(NotificationItem notification, AppSettings settings);
}
