using System.Threading.Tasks;
using NotifyHub.Models;

namespace NotifyHub.Services;

public interface ITelegramForwardingService
{
    Task<bool> TestConnectionAsync(string botToken);
    Task<bool> SendTestMessageAsync(string botToken, string chatId, string? customTemplate = null);
    Task<bool> ForwardNotificationAsync(NotificationItem notification, string botToken, string chatId, string? customTemplate = null);
}
