using System;
using System.Collections.Generic;

namespace NotifyHub.Models;

public class AppSettings
{
    public const string DefaultTelegramTemplate =
        "🔔 <b>NEW NOTIFICATION</b>\n\n" +
        "📱 <b>Source:</b> {AppName}\n" +
        "📌 <b>Category:</b> {Category}\n" +
        "📝 <b>Title:</b> {Title}\n" +
        "💬 <b>Content:</b> {Body}\n" +
        "🕒 <b>Received:</b> {Time}\n" +
        "📊 <b>Status:</b> {Status}";

    // General
    public bool MonitoringEnabled { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true; // Hide to tray when closing (X)
    public bool LaunchOnStartup { get; set; } = false;
    public string Theme { get; set; } = "Dark";
    public string DbBackupPath { get; set; } = string.Empty;
    public int RetentionDays { get; set; } = 0;

    // Filters & Exclusions
    public List<string> ExcludedApps { get; set; } = new();
    public List<string> ExcludedCategories { get; set; } = new();

    // Telegram Forwarding
    public bool TelegramEnabled { get; set; } = false;
    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;
    public string TelegramMessageTemplate { get; set; } = DefaultTelegramTemplate;

    // Email Forwarding
    public bool EmailEnabled { get; set; } = false;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string SmtpFromEmail { get; set; } = string.Empty;
    public string SmtpToEmail { get; set; } = string.Empty;

    // Google Drive Automated Backup (OAuth 2.0 drive.file scope)
    public bool GoogleDriveBackupEnabled { get; set; } = false;
    public string GoogleDriveClientId { get; set; } = string.Empty;
    public string GoogleDriveClientSecret { get; set; } = string.Empty;
    public int GoogleDriveBackupIntervalValue { get; set; } = 24;
    public string GoogleDriveBackupIntervalUnit { get; set; } = "Hours"; // Minutes, Hours, Days
    public int GoogleDriveMaxBackupsToKeep { get; set; } = 10;
    public int GoogleDriveMaxSpaceValue { get; set; } = 100;
    public string GoogleDriveMaxSpaceUnit { get; set; } = "MB"; // MB, GB
    public DateTime? LastGoogleDriveBackupAt { get; set; }

    public long GetGoogleDriveMaxSpaceBytes()
    {
        if (GoogleDriveMaxSpaceValue <= 0) return 0; // 0 = Unlimited
        long unitBytes = GoogleDriveMaxSpaceUnit == "GB"
            ? 1024L * 1024L * 1024L
            : 1024L * 1024L;
        return GoogleDriveMaxSpaceValue * unitBytes;
    }

    public TimeSpan GetGoogleDriveBackupInterval()
    {
        int val = Math.Max(1, GoogleDriveBackupIntervalValue);
        TimeSpan interval = GoogleDriveBackupIntervalUnit switch
        {
            "Minutes" => TimeSpan.FromMinutes(val),
            "Days" => TimeSpan.FromDays(val),
            _ => TimeSpan.FromHours(val)
        };

        // Safety floor: at least 5 minutes to prevent excessive API traffic
        return interval < TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : interval;
    }
}
