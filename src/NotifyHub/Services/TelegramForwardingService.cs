using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NotifyHub.Models;

namespace NotifyHub.Services;

public class TelegramForwardingService : ITelegramForwardingService
{
    private readonly HttpClient _httpClient;
    private readonly IDatabaseService _databaseService;

    public TelegramForwardingService(IDatabaseService databaseService, HttpClient? httpClient = null)
    {
        _databaseService = databaseService;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<bool> TestConnectionAsync(string botToken)
    {
        if (string.IsNullOrWhiteSpace(botToken)) return false;

        try
        {
            string url = $"https://api.telegram.org/bot{botToken.Trim()}/getMe";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return false;

            string content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.GetProperty("ok").GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SendTestMessageAsync(string botToken, string chatId, string? customTemplate = null)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId)) return false;

        var sampleItem = new NotificationItem
        {
            AppName = "Notify Hub",
            Category = "System & Security",
            Title = "Test Connection Active",
            Body = "Your Telegram forwarding integration and custom message template are working properly!",
            ReceivedAt = DateTime.Now,
            Status = NotificationStatus.Pending,
            Priority = NotificationPriority.Normal
        };

        string message = BuildNotificationMessage(sampleItem, customTemplate);
        return await SendTelegramMessageRawAsync(botToken, chatId, message, "HTML");
    }

    public async Task<bool> ForwardNotificationAsync(NotificationItem notification, string botToken, string chatId, string? customTemplate = null)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            await LogDeliveryAsync(notification.Id, notification.AppName, notification.Title, "Failed", 1, "Bot Token or Chat ID not configured");
            return false;
        }

        if (string.IsNullOrWhiteSpace(customTemplate))
        {
            try
            {
                var settings = await _databaseService.LoadSettingsAsync();
                customTemplate = settings.TelegramMessageTemplate;
            }
            catch
            {
                customTemplate = AppSettings.DefaultTelegramTemplate;
            }
        }

        string message = BuildNotificationMessage(notification, customTemplate);
        const int maxRetries = 3;
        int attempt = 0;
        string? lastError = null;

        while (attempt < maxRetries)
        {
            attempt++;
            try
            {
                if (await SendTelegramMessageRawAsync(botToken, chatId, message, "HTML"))
                {
                    await LogDeliveryAsync(notification.Id, notification.AppName, notification.Title, "Success", attempt, null);
                    return true;
                }
                lastError = "Telegram API returned unsuccessful status";
            }
            catch (Exception ex)
            {
                lastError = $"{ex.GetType().Name}: {ex.Message}";
            }

            if (attempt < maxRetries)
            {
                await Task.Delay(1000 * attempt);
            }
        }

        await LogDeliveryAsync(notification.Id, notification.AppName, notification.Title, "Failed", attempt, lastError);
        return false;
    }

    public static string BuildNotificationMessage(NotificationItem item, string? template = null)
    {
        string activeTemplate = string.IsNullOrWhiteSpace(template)
            ? AppSettings.DefaultTelegramTemplate
            : template;

        string safeApp = EscapeHtml(item.AppName);
        string safeCategory = EscapeHtml(item.Category);
        string safeTitle = EscapeHtml(item.Title);
        string safeBody = EscapeHtml(item.Body);
        string safeStatus = EscapeHtml(item.Status.ToString());
        string safePriority = EscapeHtml(item.Priority.ToString());
        string safeTime = item.ReceivedAt.ToString("dd MMMM yyyy, HH:mm");

        if (safeBody.Length > 2500)
        {
            safeBody = safeBody.Substring(0, 2500) + "... [truncated]";
        }

        string formatted = activeTemplate
            .Replace("{Id}", item.DisplayId, StringComparison.OrdinalIgnoreCase)
            .Replace("{AppName}", safeApp, StringComparison.OrdinalIgnoreCase)
            .Replace("{Source}", safeApp, StringComparison.OrdinalIgnoreCase)
            .Replace("{Category}", safeCategory, StringComparison.OrdinalIgnoreCase)
            .Replace("{Title}", safeTitle, StringComparison.OrdinalIgnoreCase)
            .Replace("{Body}", safeBody, StringComparison.OrdinalIgnoreCase)
            .Replace("{Content}", safeBody, StringComparison.OrdinalIgnoreCase)
            .Replace("{Time}", safeTime, StringComparison.OrdinalIgnoreCase)
            .Replace("{Status}", safeStatus, StringComparison.OrdinalIgnoreCase)
            .Replace("{Priority}", safePriority, StringComparison.OrdinalIgnoreCase);

        if (formatted.Length > 4096)
        {
            formatted = formatted.Substring(0, 4090) + "...";
        }

        return formatted;
    }

    private async Task<bool> SendTelegramMessageRawAsync(string botToken, string chatId, string text, string parseMode)
    {
        string url = $"https://api.telegram.org/bot{botToken.Trim()}/sendMessage";
        var payload = new
        {
            chat_id = chatId.Trim(),
            text = text,
            parse_mode = parseMode,
            disable_web_page_preview = true
        };

        string json = JsonSerializer.Serialize(payload);
        using var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, requestContent);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        string errorContent = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Telegram API error ({response.StatusCode}): {errorContent}");
    }

    private static string EscapeHtml(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private async Task LogDeliveryAsync(long notificationId, string appName, string title, string status, int attempts, string? error)
    {
        try
        {
            await _databaseService.InsertForwardingLogAsync(new ForwardingLog
            {
                NotificationId = notificationId,
                AppName = appName,
                NotificationTitle = title,
                DestinationType = DestinationType.Telegram,
                Status = status,
                AttemptCount = attempts,
                Timestamp = DateTime.Now,
                ErrorDetails = error
            });
        }
        catch
        {
            // Non-critical logging failure must never block capture
        }
    }
}
