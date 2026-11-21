using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NotifyHub.Models;

namespace NotifyHub.Services;

public class SmartCategorizationService : ICategorizationService
{
    private readonly IDatabaseService _databaseService;
    private List<CategoryRule> _rules = new();
    private readonly object _lock = new();

    public SmartCategorizationService(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task ReloadRulesAsync()
    {
        var rules = await _databaseService.GetRulesAsync();
        lock (_lock)
        {
            _rules = rules;
        }
    }

    public async Task<string> CategorizeAsync(string appName, string title, string body)
    {
        if (_rules.Count == 0)
        {
            await ReloadRulesAsync();
        }

        string safeAppName = appName ?? string.Empty;
        string safeTitle = title ?? string.Empty;
        string safeBody = body ?? string.Empty;
        string combinedContent = $"{safeTitle} {safeBody}";

        // 1. Evaluate User and Seeded Custom Rules in Priority Order
        lock (_lock)
        {
            foreach (var rule in _rules)
            {
                if (!rule.IsActive || string.IsNullOrWhiteSpace(rule.Pattern)) continue;

                bool matched = false;
                switch (rule.MatchType)
                {
                    case RuleMatchType.AppNameEquals:
                        matched = safeAppName.Equals(rule.Pattern, StringComparison.OrdinalIgnoreCase);
                        break;
                    case RuleMatchType.AppNameContains:
                        matched = safeAppName.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case RuleMatchType.TitleContains:
                        matched = safeTitle.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case RuleMatchType.BodyContains:
                        matched = safeBody.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case RuleMatchType.ContentContains:
                        matched = combinedContent.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case RuleMatchType.RegexMatch:
                        try
                        {
                            matched = Regex.IsMatch(combinedContent, rule.Pattern, RegexOptions.IgnoreCase);
                        }
                        catch
                        {
                            matched = false;
                        }
                        break;
                }

                if (matched)
                {
                    return rule.CategoryName;
                }
            }
        }

        // 2. Fallback Heuristics by App Name and Content Keywords
        // Emails
        if (MatchesAny(safeAppName, "Outlook", "Thunderbird", "Mail", "Gmail", "ProtonMail", "Yahoo Mail") ||
            MatchesAny(combinedContent, "new email", "subject:", "re:", "inbox", "sent an email"))
        {
            return "Emails";
        }

        // Messages & Communication
        if (MatchesAny(safeAppName, "WhatsApp", "Telegram", "Signal", "Discord", "Skype", "Viber", "WeChat", "Line", "Messenger", "Phone Link", "Messages") ||
            MatchesAny(combinedContent, "sent a message", "new message", "missed call", "voice call", "incoming call", "chat message"))
        {
            return "Messages & Communication";
        }

        // Work & Business
        if (MatchesAny(safeAppName, "Slack", "Teams", "Microsoft Teams", "Zoom", "Trello", "Jira", "Confluence", "Asana", "Notion", "GitHub", "GitLab", "Bitbucket", "Visual Studio", "VS Code", "JetBrains", "Rider", "Linear") ||
            MatchesAny(combinedContent, "pull request", "build failed", "commit", "sprint", "meeting started", "joined the room"))
        {
            return "Work & Business";
        }

        // Finance & Banking
        if (MatchesAny(safeAppName, "Bank", "PayPal", "Revolut", "Stripe", "Wise", "Chase", "Wells Fargo", "Binance", "Coinbase", "Crypto", "Wallet", "Amex", "Mastercard", "Visa", "Barclays", "HSBC") ||
            MatchesAny(combinedContent, "otp", "verification code", "one-time password", "security code", "transaction", "payment of", "debited", "credited", "wire transfer", "balance is", "fraud alert"))
        {
            return "Finance & Banking";
        }

        // Social Media
        if (MatchesAny(safeAppName, "Twitter", "X", "Instagram", "Facebook", "LinkedIn", "TikTok", "Reddit", "Pinterest", "Snapchat", "YouTube", "Threads") ||
            MatchesAny(combinedContent, "started following you", "liked your post", "retweeted", "commented on", "subscribed to"))
        {
            return "Social Media";
        }

        // Calendar & Reminders
        if (MatchesAny(safeAppName, "Calendar", "Reminders", "Tasks", "Todoist", "Any.do", "Microsoft To Do", "TickTick", "Clock", "Alarm") ||
            MatchesAny(combinedContent, "starts in 10 minutes", "starts in 15 minutes", "event reminder", "due today", "due tomorrow", "alarm ringing"))
        {
            return "Calendar & Reminders";
        }

        // Shopping & Orders
        if (MatchesAny(safeAppName, "Amazon", "eBay", "AliExpress", "Shopify", "FedEx", "UPS", "DHL", "Aramex", "USPS", "Target", "Walmart", "Best Buy") ||
            MatchesAny(combinedContent, "order confirmed", "shipped", "out for delivery", "has been delivered", "package arrived", "tracking number", "order #"))
        {
            return "Shopping & Orders";
        }

        // System & Security
        if (MatchesAny(safeAppName, "Windows Security", "Windows Update", "Antivirus", "Defender", "Firewall", "Bitdefender", "Norton", "McAfee", "Avast", "Kaspersky", "SecurityHealthHost") ||
            MatchesAny(combinedContent, "restart required", "updates are ready", "threat detected", "virus scan", "firewall blocked", "malware", "drive errors", "battery low"))
        {
            return "System & Security";
        }

        // Personal
        if (MatchesAny(safeAppName, "Photos", "Family", "Contacts", "Your Phone", "Camera"))
        {
            return "Personal";
        }

        return "Other";
    }

    public NotificationPriority DeterminePriority(string appName, string title, string body, string category)
    {
        string combined = $"{appName} {title} {body}".ToLowerInvariant();

        // Urgent conditions: OTPs, security threats, server down, critical alerts
        if (combined.Contains("otp") ||
            combined.Contains("verification code") ||
            combined.Contains("threat detected") ||
            combined.Contains("unauthorized login") ||
            combined.Contains("security alert") ||
            combined.Contains("urgent") ||
            combined.Contains("fraud") ||
            combined.Contains("emergency") ||
            combined.Contains("critical"))
        {
            return NotificationPriority.Urgent;
        }

        // High priority: Finance transactions, important work notices, deadlines
        if (category == "Finance & Banking" ||
            category == "System & Security" ||
            combined.Contains("action required") ||
            combined.Contains("payment") ||
            combined.Contains("deadline") ||
            combined.Contains("failed") ||
            combined.Contains("important") ||
            combined.Contains("asap"))
        {
            return NotificationPriority.High;
        }

        // Low priority: Social media, marketing, shopping promotions
        if (category == "Social Media" ||
            combined.Contains("discount") ||
            combined.Contains("sale") ||
            combined.Contains("promo") ||
            combined.Contains("newsletter") ||
            combined.Contains("digest"))
        {
            return NotificationPriority.Low;
        }

        return NotificationPriority.Normal;
    }

    private static bool MatchesAny(string source, params string[] terms)
    {
        if (string.IsNullOrEmpty(source)) return false;
        foreach (var term in terms)
        {
            if (source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }
}
