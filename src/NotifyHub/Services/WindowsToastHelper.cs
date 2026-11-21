using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace NotifyHub.Services;

public static class WindowsToastHelper
{
    private static readonly object _lock = new();

    public static string? GetWpnDatabasePath()
    {
        try
        {
            // 1. Standard user profile path
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                string p1 = Path.Combine(userProfile, @"AppData\Local\Microsoft\Windows\Notifications\wpndatabase.db");
                if (File.Exists(p1)) return p1;
            }

            // 2. SpecialFolder.LocalApplicationData
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localApp))
            {
                string p2 = Path.Combine(localApp, @"Microsoft\Windows\Notifications\wpndatabase.db");
                if (File.Exists(p2)) return p2;
            }

            // 3. Environment Variable LOCALAPPDATA
            string? envLocal = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(envLocal))
            {
                string p3 = Path.Combine(envLocal, @"Microsoft\Windows\Notifications\wpndatabase.db");
                if (File.Exists(p3)) return p3;
            }
        }
        catch { }

        return null;
    }

    public static string? TryGetToastLaunchUri(long? rawNotificationId, string? appName = null, string? title = null, string? body = null)
    {
        string? dbPath = GetWpnDatabasePath();
        if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
        {
            return null;
        }

        lock (_lock)
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"nh_wpn_{Guid.NewGuid():N}.db");
            try
            {
                File.Copy(dbPath, tempDb, true);
                using var conn = new SqliteConnection($"Data Source={tempDb};Mode=ReadOnly");
                conn.Open();

                // 1. Look up by exact RawNotificationId if provided
                if (rawNotificationId.HasValue && rawNotificationId.Value > 0)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Payload FROM Notification WHERE Id = @Id AND Type = 'toast' LIMIT 1;";
                    cmd.Parameters.AddWithValue("@Id", rawNotificationId.Value);

                    var payloadObj = cmd.ExecuteScalar();
                    if (payloadObj != null && payloadObj is byte[] bytes)
                    {
                        string xml = Encoding.UTF8.GetString(bytes);
                        string? launchUri = ParseLaunchUriFromToastXml(xml);
                        if (!string.IsNullOrWhiteSpace(launchUri))
                        {
                            return launchUri;
                        }
                    }
                }

                // 2. Fallback search among recent toasts by title/body or app name
                if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(body) || !string.IsNullOrWhiteSpace(appName))
                {
                    using var searchCmd = conn.CreateCommand();
                    searchCmd.CommandText = @"
                        SELECT n.Id, h.PrimaryId, n.Payload 
                        FROM Notification n 
                        LEFT JOIN NotificationHandler h ON n.HandlerId = h.RecordId 
                        WHERE n.Type = 'toast' 
                        ORDER BY n.Id DESC 
                        LIMIT 35;";

                    using var reader = searchCmd.ExecuteReader();
                    string cleanTitle = (title ?? string.Empty).Trim();
                    string cleanBody = (body ?? string.Empty).Trim();
                    string cleanAppName = (appName ?? string.Empty).Trim();

                    // If body has multiple lines, take first significant line or snippet
                    string bodySnippet = cleanBody;
                    int nl = bodySnippet.IndexOf('\n');
                    if (nl > 0) bodySnippet = bodySnippet.Substring(0, nl).Trim();
                    if (bodySnippet.Length > 50) bodySnippet = bodySnippet.Substring(0, 50);

                    while (reader.Read())
                    {
                        if (reader.IsDBNull(2)) continue;

                        byte[] bytes = (byte[])reader.GetValue(2);
                        string xml = Encoding.UTF8.GetString(bytes);
                        string? handler = reader.IsDBNull(1) ? null : reader.GetString(1);

                        bool matchesApp = string.IsNullOrEmpty(cleanAppName) ||
                                          (handler != null && handler.IndexOf(cleanAppName, StringComparison.OrdinalIgnoreCase) >= 0);

                        bool matchesContent = (!string.IsNullOrEmpty(cleanTitle) && !cleanTitle.Equals("(No Title)", StringComparison.OrdinalIgnoreCase) && xml.Contains(cleanTitle)) ||
                                              (!string.IsNullOrEmpty(bodySnippet) && !bodySnippet.Equals("(No Content)", StringComparison.OrdinalIgnoreCase) && xml.Contains(bodySnippet));

                        if (matchesApp && matchesContent)
                        {
                            string? launchUri = ParseLaunchUriFromToastXml(xml);
                            if (!string.IsNullOrWhiteSpace(launchUri))
                            {
                                return launchUri;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Graceful fallback if SQLite or file lock issue occurs
            }
            finally
            {
                try
                {
                    if (File.Exists(tempDb))
                    {
                        File.Delete(tempDb);
                    }
                }
                catch { }
            }
        }

        return null;
    }

    public static string? ParseLaunchUriFromToastXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        // 1. Root <toast launch="...">
        var match = Regex.Match(xml, @"<toast\b[^>]*\blaunch=""([^""]+)""", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
        }

        // 2. Any launch="..." attribute
        var generalMatch = Regex.Match(xml, @"\blaunch=""([^""]+)""", RegexOptions.IgnoreCase);
        if (generalMatch.Success)
        {
            return System.Net.WebUtility.HtmlDecode(generalMatch.Groups[1].Value.Trim());
        }

        // 3. Action button arguments: <action arguments="..." ...>
        var actionMatch = Regex.Match(xml, @"<action\b[^>]*\barguments=""([^""]+)""", RegexOptions.IgnoreCase);
        if (actionMatch.Success)
        {
            return System.Net.WebUtility.HtmlDecode(actionMatch.Groups[1].Value.Trim());
        }

        return null;
    }
}
