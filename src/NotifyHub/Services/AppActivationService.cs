using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using NotifyHub.Models;

namespace NotifyHub.Services;

public static class AppActivationService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    private static readonly Dictionary<string, string> KnownProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Slack", "slack://" },
        { "Telegram", "tg://" },
        { "WhatsApp", "whatsapp://" },
        { "Discord", "discord://" },
        { "Spotify", "spotify://" },
        { "Microsoft Teams", "msteams://" },
        { "Teams", "msteams://" },
        { "Zoom", "zoommtg://" },
        { "Skype", "skype://" },
        { "Mail", "mailto:" },
        { "Outlook", "outlookmail:" }
    };

    public static bool TryNavigateToNotification(NotificationItem item, IDatabaseService? dbService = null)
    {
        if (item == null) return false;

        string appName = (item.AppName ?? string.Empty).Trim();
        string appId = (item.AppId ?? string.Empty).Trim();
        string title = (item.Title ?? string.Empty).Trim();
        string body = (item.Body ?? string.Empty).Trim();

        // 1. HIGHEST PRIORITY: Native Windows Toast Action (Exact parity with clicking a Windows notification toast)
        string? launchUri = ExtractLaunchUriFromItem(item);
        if (string.IsNullOrWhiteSpace(launchUri))
        {
            long? rawId = ExtractRawNotificationId(item);
            launchUri = WindowsToastHelper.TryGetToastLaunchUri(rawId, appName, title, body);
            if (!string.IsNullOrWhiteSpace(launchUri))
            {
                item.LaunchUri = launchUri;
                if (dbService != null)
                {
                    _ = dbService.UpdateLaunchUriAsync(item.Id, launchUri);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(launchUri))
        {
            bool launched = ExecuteLaunchTarget(launchUri, appName, appId);
            if (launched)
            {
                return true;
            }
        }

        // 2. Embedded web URL fallback (common in Jira, GitHub, Gmail, web apps)
        string? embeddedUrl = ExtractUrl(title) ?? ExtractUrl(body);
        if (!string.IsNullOrEmpty(embeddedUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo(embeddedUrl) { UseShellExecute = true });
                return true;
            }
            catch { }
        }

        // 3. Specialized Navigation: Slack
        if (IsMatch(appName, appId, "Slack"))
        {
            return NavigateSlack(title, body);
        }

        // 4. Specialized Navigation: Telegram
        if (IsMatch(appName, appId, "Telegram"))
        {
            return NavigateTelegram(title, body);
        }

        // 5. Specialized Navigation: WhatsApp
        if (IsMatch(appName, appId, "WhatsApp"))
        {
            return NavigateWhatsApp(title, body);
        }

        // 6. Specialized Navigation: Microsoft Teams
        if (IsMatch(appName, appId, "Teams") || IsMatch(appName, appId, "Microsoft Teams"))
        {
            return NavigateTeams(title, body);
        }

        // 7. Specialized Navigation: Discord
        if (IsMatch(appName, appId, "Discord"))
        {
            return NavigateDiscord(title, body);
        }

        // 8. General fallback: activate window or launch executable
        return TryActivateApp(appName, appId);
    }

    private static bool ExecuteLaunchTarget(string launchTarget, string appName, string appId)
    {
        try
        {
            // Case A: Protocol URI (e.g. slack://..., tg://..., whatsapp://..., https://..., mailto:..., zoommtg:...)
            if (Regex.IsMatch(launchTarget, @"^[a-zA-Z][a-zA-Z0-9+.-]+://", RegexOptions.IgnoreCase) ||
                launchTarget.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                var psi = new ProcessStartInfo(launchTarget) { UseShellExecute = true };
                Process.Start(psi);

                // Ensure the application window is brought to foreground
                Task.Run(() =>
                {
                    try
                    {
                        Thread.Sleep(200);
                        TryBringRunningAppToForeground(appName, appId);
                    }
                    catch { }
                });

                return true;
            }

            // Case B: Packaged App AUMID with arguments
            if (!string.IsNullOrEmpty(appId) && appId.Contains("!"))
            {
                try
                {
                    var psi = new ProcessStartInfo($"shell:AppsFolder\\{appId}", launchTarget) { UseShellExecute = true };
                    Process.Start(psi);
                    return true;
                }
                catch { }
            }

            // Case C: Direct shell execution
            var directPsi = new ProcessStartInfo(launchTarget) { UseShellExecute = true };
            Process.Start(directPsi);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to execute launch target '{launchTarget}': {ex.Message}");
            return false;
        }
    }

    private static string? ExtractLaunchUriFromItem(NotificationItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.LaunchUri))
        {
            return item.LaunchUri;
        }

        if (!string.IsNullOrWhiteSpace(item.RawMetadata))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(item.RawMetadata);
                if (doc.RootElement.TryGetProperty("LaunchUri", out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    string? uri = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(uri))
                        return uri;
                }
            }
            catch { }
        }

        return null;
    }

    private static long? ExtractRawNotificationId(NotificationItem item)
    {
        // 1. Check RawMetadata JSON
        if (!string.IsNullOrWhiteSpace(item.RawMetadata))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(item.RawMetadata);
                if (doc.RootElement.TryGetProperty("RawId", out var rawIdProp))
                {
                    if (rawIdProp.ValueKind == System.Text.Json.JsonValueKind.Number && rawIdProp.TryGetInt64(out long num))
                        return num;
                    if (rawIdProp.ValueKind == System.Text.Json.JsonValueKind.String && long.TryParse(rawIdProp.GetString(), out long parsed))
                        return parsed;
                }
            }
            catch { }
        }

        // 2. Check NotificationId pattern: {appId}_{rawId}_{timestamp}
        if (!string.IsNullOrWhiteSpace(item.NotificationId))
        {
            var parts = item.NotificationId.Split('_');
            if (parts.Length >= 2 && long.TryParse(parts[parts.Length - 2], out long parsedId))
            {
                return parsedId;
            }
        }

        return null;
    }

    private static bool NavigateSlack(string title, string body)
    {
        // Extract the channel or sender/conversation target
        string target = ExtractSlackTarget(title, body);

        // Copy a snippet of the message to the clipboard so the user can easily find/paste it
        CopySnippetToClipboard(body);

        // Bring Slack window to foreground
        IntPtr hWnd = FindProcessMainWindow("Slack");
        if (hWnd != IntPtr.Zero)
        {
            ActivateWindow(hWnd);

            if (!string.IsNullOrWhiteSpace(target))
            {
                Task.Run(() =>
                {
                    try
                    {
                        Thread.Sleep(200);

                        // Trigger Slack Quick Switcher (Ctrl+K)
                        SendKeys.SendWait("^k");
                        Thread.Sleep(150);

                        // Type target channel or contact name
                        string escapedTarget = EscapeForSendKeys(target);
                        SendKeys.SendWait(escapedTarget);
                        Thread.Sleep(250);

                        // Press Enter to jump to the conversation
                        SendKeys.SendWait("{ENTER}");
                    }
                    catch { }
                });
            }

            return true;
        }

        // If Slack is not running, launch it with deep link or search query
        try
        {
            string cleanSearch = !string.IsNullOrWhiteSpace(target) ? target : title;
            string url = !string.IsNullOrWhiteSpace(cleanSearch)
                ? $"slack://search?query={Uri.EscapeDataString(cleanSearch)}"
                : "slack://open";

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return TryActivateApp("Slack", "Slack");
        }
    }

    private static string ExtractSlackTarget(string title, string body)
    {
        string t = title.Trim();

        // If title is generic (e.g. "Reminder due", "New message"), extract sender from body
        if (string.Equals(t, "Reminder due", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, "New message", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, "(No Title)", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(t))
        {
            int colon = body.IndexOf(':');
            if (colon > 0 && colon < 40)
            {
                return body.Substring(0, colon).Trim();
            }
            return string.Empty;
        }

        // If title is a comma-separated list of group participants (e.g. "Amr Nasser, Orkun")
        if (t.Contains(','))
        {
            int colon = body.IndexOf(':');
            if (colon > 0 && colon < 40)
            {
                string speaker = body.Substring(0, colon).Trim();
                if (t.IndexOf(speaker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return speaker;
                }
            }
            return t.Split(',')[0].Trim();
        }

        return t;
    }

    private static bool NavigateTelegram(string title, string body)
    {
        CopySnippetToClipboard(body);

        string searchTarget = !string.IsNullOrWhiteSpace(title) ? title : body;
        if (!string.IsNullOrWhiteSpace(searchTarget))
        {
            // Truncate to first 40 chars
            if (searchTarget.Length > 40) searchTarget = searchTarget.Substring(0, 40);

            try
            {
                Process.Start(new ProcessStartInfo($"tg://search?query={Uri.EscapeDataString(searchTarget)}") { UseShellExecute = true });
                return true;
            }
            catch { }
        }

        IntPtr hWnd = FindProcessMainWindow("Telegram");
        if (hWnd != IntPtr.Zero)
        {
            ActivateWindow(hWnd);
            return true;
        }

        return TryActivateApp("Telegram", "Telegram.TelegramDesktop");
    }

    private static bool NavigateWhatsApp(string title, string body)
    {
        CopySnippetToClipboard(body);

        IntPtr hWnd = FindProcessMainWindow("WhatsApp");
        if (hWnd != IntPtr.Zero)
        {
            ActivateWindow(hWnd);

            if (!string.IsNullOrWhiteSpace(title) && !title.Equals("(No Title)", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() =>
                {
                    try
                    {
                        Thread.Sleep(200);
                        // Jump to chat search in WhatsApp (Ctrl+F)
                        SendKeys.SendWait("^f");
                        Thread.Sleep(150);
                        SendKeys.SendWait(EscapeForSendKeys(title));
                        Thread.Sleep(250);
                        SendKeys.SendWait("{ENTER}");
                    }
                    catch { }
                });
            }
            return true;
        }

        return TryActivateApp("WhatsApp", "");
    }

    private static bool NavigateTeams(string title, string body)
    {
        CopySnippetToClipboard(body);

        IntPtr hWnd = FindProcessMainWindow("ms-teams") != IntPtr.Zero ? FindProcessMainWindow("ms-teams") : FindProcessMainWindow("Teams");
        if (hWnd != IntPtr.Zero)
        {
            ActivateWindow(hWnd);

            if (!string.IsNullOrWhiteSpace(title))
            {
                Task.Run(() =>
                {
                    try
                    {
                        Thread.Sleep(200);
                        // Teams Search / Command Box (Ctrl+E)
                        SendKeys.SendWait("^e");
                        Thread.Sleep(150);
                        SendKeys.SendWait(EscapeForSendKeys(title));
                        Thread.Sleep(250);
                        SendKeys.SendWait("{ENTER}");
                    }
                    catch { }
                });
            }
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo("msteams://") { UseShellExecute = true });
            return true;
        }
        catch
        {
            return TryActivateApp("Teams", "Microsoft.Teams");
        }
    }

    private static bool NavigateDiscord(string title, string body)
    {
        CopySnippetToClipboard(body);

        IntPtr hWnd = FindProcessMainWindow("Discord");
        if (hWnd != IntPtr.Zero)
        {
            ActivateWindow(hWnd);

            if (!string.IsNullOrWhiteSpace(title))
            {
                Task.Run(() =>
                {
                    try
                    {
                        Thread.Sleep(200);
                        // Discord Quick Switcher (Ctrl+K)
                        SendKeys.SendWait("^k");
                        Thread.Sleep(150);
                        SendKeys.SendWait(EscapeForSendKeys(title));
                        Thread.Sleep(250);
                        SendKeys.SendWait("{ENTER}");
                    }
                    catch { }
                });
            }
            return true;
        }

        return TryActivateApp("Discord", "Discord");
    }

    public static bool TryActivateApp(string? appName, string? appId)
    {
        string name = (appName ?? string.Empty).Trim();
        string id = (appId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        // 1. Try to bring existing running process window to foreground
        if (TryBringRunningAppToForeground(name, id))
        {
            return true;
        }

        // 2. Try launching packaged / UWP app via shell:AppsFolder AUMID
        if (!string.IsNullOrEmpty(id) && id.Contains("!"))
        {
            try
            {
                var psi = new ProcessStartInfo($"shell:AppsFolder\\{id}") { UseShellExecute = true };
                Process.Start(psi);
                return true;
            }
            catch { }
        }

        // 3. Try launching via known protocol scheme
        if (!string.IsNullOrEmpty(name))
        {
            foreach (var kvp in KnownProtocols)
            {
                if (name.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    try
                    {
                        var psi = new ProcessStartInfo(kvp.Value) { UseShellExecute = true };
                        Process.Start(psi);
                        return true;
                    }
                    catch { }
                }
            }
        }

        // 4. Try launch by AppId if it points to an existing file path or executable
        if (!string.IsNullOrEmpty(id))
        {
            try
            {
                if (File.Exists(id))
                {
                    Process.Start(new ProcessStartInfo(id) { UseShellExecute = true });
                    return true;
                }
            }
            catch { }
        }

        // 5. Try resolving from Windows App Paths Registry
        if (!string.IsNullOrEmpty(name))
        {
            string cleanName = name.Replace(" ", "");
            string? registeredPath = FindAppPathFromRegistry(cleanName) ?? FindAppPathFromRegistry(name);
            if (!string.IsNullOrEmpty(registeredPath) && File.Exists(registeredPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(registeredPath) { UseShellExecute = true });
                    return true;
                }
                catch { }
            }
        }

        // 6. Try direct shell execute with app name or id
        try
        {
            string launchTarget = !string.IsNullOrEmpty(name) ? name.ToLowerInvariant() : id;
            var psi = new ProcessStartInfo(launchTarget) { UseShellExecute = true };
            Process.Start(psi);
            return true;
        }
        catch { }

        return false;
    }

    private static IntPtr FindProcessMainWindow(string processKeyword)
    {
        try
        {
            string lower = processKeyword.ToLowerInvariant();
            var procs = Process.GetProcesses()
                .Where(p =>
                {
                    try
                    {
                        return p.ProcessName.ToLowerInvariant().Contains(lower);
                    }
                    catch { return false; }
                })
                .ToList();

            foreach (var p in procs)
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        return p.MainWindowHandle;
                    }
                }
                catch { }
            }
        }
        catch { }

        return IntPtr.Zero;
    }

    private static bool TryBringRunningAppToForeground(string appName, string appId)
    {
        try
        {
            string searchName = appName.ToLowerInvariant();
            string searchId = appId.ToLowerInvariant();

            var candidates = Process.GetProcesses()
                .Where(p =>
                {
                    try
                    {
                        string pName = p.ProcessName.ToLowerInvariant();
                        if (!string.IsNullOrEmpty(searchName) && (pName.Contains(searchName) || searchName.Contains(pName)))
                            return true;

                        if (!string.IsNullOrEmpty(searchId) && (pName.Contains(searchId) || searchId.Contains(pName)))
                            return true;

                        // Check main window title
                        string title = p.MainWindowTitle.ToLowerInvariant();
                        if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(searchName) && title.Contains(searchName))
                            return true;

                        return false;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();

            foreach (var proc in candidates)
            {
                try
                {
                    IntPtr hWnd = proc.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        ActivateWindow(hWnd);
                        return true;
                    }
                }
                catch { }
            }
        }
        catch { }

        return false;
    }

    private static void ActivateWindow(IntPtr hWnd)
    {
        if (IsIconic(hWnd))
        {
            ShowWindow(hWnd, SW_RESTORE);
        }
        else
        {
            ShowWindow(hWnd, SW_SHOW);
        }

        IntPtr foregroundWnd = GetForegroundWindow();
        uint foregroundThreadId = GetWindowThreadProcessId(foregroundWnd, out _);
        uint currentThreadId = (uint)Environment.CurrentManagedThreadId;

        if (foregroundThreadId != currentThreadId)
        {
            AttachThreadInput(currentThreadId, foregroundThreadId, true);
            SetForegroundWindow(hWnd);
            AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
        else
        {
            SetForegroundWindow(hWnd);
        }
    }

    private static string? FindAppPathFromRegistry(string appName)
    {
        string[] extensions = { "", ".exe" };
        string[] hives = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths" };

        foreach (var ext in extensions)
        {
            string keyName = appName + ext;
            foreach (var hivePath in hives)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey($@"{hivePath}\{keyName}");
                    if (key?.GetValue(null) is string path && !string.IsNullOrEmpty(path))
                        return path;
                }
                catch { }

                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey($@"{hivePath}\{keyName}");
                    if (key?.GetValue(null) is string path && !string.IsNullOrEmpty(path))
                        return path;
                }
                catch { }
            }
        }

        return null;
    }

    private static string? ExtractUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = Regex.Match(text, @"https?://[^\s""'>)]+", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Value;
        }

        return null;
    }

    private static bool IsMatch(string appName, string appId, string target)
    {
        return (!string.IsNullOrEmpty(appName) && appName.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (!string.IsNullOrEmpty(appId) && appId.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string EscapeForSendKeys(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (c is '+' or '^' or '%' or '~' or '(' or ')' or '[' or ']' or '{' or '}')
            {
                sb.Append('{').Append(c).Append('}');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static void CopySnippetToClipboard(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;

        try
        {
            string snippet = body.Trim();
            // Take first line or up to 100 characters
            int newline = snippet.IndexOf('\n');
            if (newline > 0) snippet = snippet.Substring(0, newline).Trim();
            if (snippet.Length > 100) snippet = snippet.Substring(0, 100);

            // Strip "Speaker: " if present
            int colon = snippet.IndexOf(':');
            if (colon > 0 && colon < 25)
            {
                snippet = snippet.Substring(colon + 1).Trim();
            }

            if (!string.IsNullOrWhiteSpace(snippet))
            {
                // In STA thread or Dispatcher
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(snippet);
                    }
                    catch { }
                });
            }
        }
        catch { }
    }
}
