using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using NotifyHub.Models;

namespace NotifyHub.Services;

public class EmailForwardingService : IEmailForwardingService
{
    private readonly IDatabaseService _databaseService;

    public EmailForwardingService(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<bool> SendTestEmailAsync(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.SmtpToEmail))
        {
            return false;
        }

        string fromAddress = !string.IsNullOrWhiteSpace(settings.SmtpFromEmail)
            ? settings.SmtpFromEmail
            : (!string.IsNullOrWhiteSpace(settings.SmtpUsername) ? settings.SmtpUsername : "notifyhub@localhost");

        using var client = CreateSmtpClient(settings);
        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, "Notify Hub"),
            Subject = "🔔 Notify Hub — Test Email Delivery",
            IsBodyHtml = true,
            Body = @"
                <div style='font-family: -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, Helvetica, Arial, sans-serif; max-width: 580px; margin: 0 auto; padding: 24px; border: 1px solid #e0e0e0; border-radius: 8px;'>
                    <h2 style='color: #0078D4; margin-top: 0;'>🔔 Notify Hub — Test Delivery</h2>
                    <p style='color: #333; font-size: 15px;'>Your email forwarding integration has been configured successfully!</p>
                    <p style='color: #666; font-size: 13px;'>Notifications captured by Notify Hub will be delivered to this address.</p>
                    <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;' />
                    <p style='color: #999; font-size: 12px;'>Sent automatically by Notify Hub for Windows.</p>
                </div>"
        };
        message.To.Add(settings.SmtpToEmail);

        try
        {
            await client.SendMailAsync(message);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ForwardNotificationAsync(NotificationItem notification, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.SmtpToEmail))
        {
            await LogDeliveryAsync(notification.Id, notification.AppName, notification.Title, "Failed", 1, "SMTP Host or Destination Email not configured");
            return false;
        }

        string fromAddress = !string.IsNullOrWhiteSpace(settings.SmtpFromEmail)
            ? settings.SmtpFromEmail
            : (!string.IsNullOrWhiteSpace(settings.SmtpUsername) ? settings.SmtpUsername : "notifyhub@localhost");

        int maxRetries = 3;
        int attempt = 0;
        string? lastError = null;

        while (attempt < maxRetries)
        {
            attempt++;
            try
            {
                using var client = CreateSmtpClient(settings);
                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress, "Notify Hub"),
                    Subject = $"[{notification.AppName}] {notification.Title}",
                    IsBodyHtml = true,
                    Body = BuildEmailHtml(notification)
                };
                message.To.Add(settings.SmtpToEmail);

                await client.SendMailAsync(message);
                await LogDeliveryAsync(notification.Id, notification.AppName, notification.Title, "Success", attempt, null);
                return true;
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

    private static SmtpClient CreateSmtpClient(AppSettings settings)
    {
        var client = new SmtpClient(settings.SmtpHost.Trim(), settings.SmtpPort)
        {
            EnableSsl = settings.SmtpUseSsl,
            Timeout = 15000
        };

        if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(settings.SmtpUsername.Trim(), settings.SmtpPassword ?? "");
        }
        else
        {
            client.UseDefaultCredentials = true;
        }

        return client;
    }

    private static string BuildEmailHtml(NotificationItem item)
    {
        string safeApp = WebUtility.HtmlEncode(item.AppName);
        string safeCategory = WebUtility.HtmlEncode(item.Category);
        string safeTitle = WebUtility.HtmlEncode(item.Title);
        string safeBody = WebUtility.HtmlEncode(item.Body).Replace("\n", "<br/>");
        string safeStatus = WebUtility.HtmlEncode(item.Status.ToString());
        string safePriority = WebUtility.HtmlEncode(item.Priority.ToString());
        string safeTime = item.ReceivedAt.ToString("dd MMMM yyyy, HH:mm:ss");

        string priorityColor = item.Priority switch
        {
            NotificationPriority.Urgent => "#D83B01",
            NotificationPriority.High => "#E81123",
            NotificationPriority.Low => "#69797E",
            _ => "#0078D4"
        };

        return $@"
            <div style='font-family: -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, Helvetica, Arial, sans-serif; max-width: 620px; margin: 0 auto; padding: 24px; background: #ffffff; border: 1px solid #e1e4e8; border-radius: 10px; color: #24292e;'>
                <div style='display: flex; align-items: center; justify-content: space-between; border-bottom: 2px solid #0078D4; padding-bottom: 12px; margin-bottom: 20px;'>
                    <h2 style='margin: 0; color: #0078D4; font-size: 20px; font-weight: 600;'>🔔 Notify Hub Alert</h2>
                    <span style='background: {priorityColor}; color: white; padding: 4px 10px; border-radius: 12px; font-size: 11px; font-weight: bold; text-transform: uppercase;'>{safePriority}</span>
                </div>
                
                <table style='width: 100%; border-collapse: collapse; margin-bottom: 20px; font-size: 14px;'>
                    <tr>
                        <td style='padding: 6px 0; color: #586069; width: 110px;'><strong>Source:</strong></td>
                        <td style='padding: 6px 0; color: #24292e; font-weight: 600;'>{safeApp}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #586069;'><strong>Category:</strong></td>
                        <td style='padding: 6px 0; color: #24292e;'><span style='background: #f1f3f5; padding: 2px 8px; border-radius: 4px;'>{safeCategory}</span></td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #586069;'><strong>Received:</strong></td>
                        <td style='padding: 6px 0; color: #24292e;'>{safeTime}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #586069;'><strong>Status:</strong></td>
                        <td style='padding: 6px 0; color: #0078D4; font-weight: 600;'>{safeStatus}</td>
                    </tr>
                </table>

                <div style='background: #f6f8fa; border-left: 4px solid #0078D4; padding: 14px 16px; border-radius: 0 6px 6px 0; margin-bottom: 24px;'>
                    <h3 style='margin: 0 0 8px 0; font-size: 16px; color: #1b1f23;'>{safeTitle}</h3>
                    <div style='font-size: 14px; line-height: 1.5; color: #24292e;'>{safeBody}</div>
                </div>

                <div style='border-top: 1px solid #eaecef; padding-top: 14px; font-size: 12px; color: #6a737d;'>
                    Captured permanently and forwarded by <strong>Notify Hub</strong> for Windows.
                </div>
            </div>";
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
                DestinationType = DestinationType.Email,
                Status = status,
                AttemptCount = attempts,
                Timestamp = DateTime.Now,
                ErrorDetails = error
            });
        }
        catch
        {
            // Non-critical logging failure must never block
        }
    }
}
