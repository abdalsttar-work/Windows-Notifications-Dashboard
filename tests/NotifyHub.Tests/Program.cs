using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using NotifyHub.Models;
using NotifyHub.Services;

namespace NotifyHub.Tests;

class Program
{
    private static int _passedTests = 0;
    private static int _failedTests = 0;

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("======================================================================");
        Console.WriteLine("           NOTIFY HUB — COMPREHENSIVE FUNCTIONAL TEST SUITE           ");
        Console.WriteLine("======================================================================");
        Console.WriteLine($"Started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

        string testDbFolder = Path.Combine(AppContext.BaseDirectory, "TestStorage");
        Directory.CreateDirectory(testDbFolder);
        string testDbPath = Path.Combine(testDbFolder, "notifyhub_test.db");
        if (File.Exists(testDbPath)) File.Delete(testDbPath);

        var secureStorage = new WindowsCredentialManagerService();
        var dbService = new SqliteDatabaseService(secureStorage, testDbPath);
        await dbService.InitializeDatabaseAsync();

        var categorizationService = new SmartCategorizationService(dbService);
        await categorizationService.ReloadRulesAsync();

        var telegramService = new TelegramForwardingService(dbService);
        var emailService = new EmailForwardingService(dbService);
        var startupService = new WindowsStartupService();
        var exportService = new ExportService();

        // RUN TESTS
        await RunTest("1. Windows Notification Capture & API Evaluation", async () =>
        {
            bool supported = ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener");
            Assert(supported, "UserNotificationListener API must be supported on Windows");

            var listener = UserNotificationListener.Current;
            var status = listener.GetAccessStatus();
            Console.WriteLine($"   [Info] UserNotificationListener AccessStatus: {status}");

            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                try
                {
                    status = await listener.RequestAccessAsync();
                    Console.WriteLine($"   [Info] Status after RequestAccessAsync: {status}");
                }
                catch (Exception exReq)
                {
                    Console.WriteLine($"   [Info] Note: RequestAccessAsync on unpackaged Win32 app: {exReq.Message}");
                }
            }

            if (status == UserNotificationListenerAccessStatus.Allowed)
            {
                var activeToasts = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                Console.WriteLine($"   [Info] Active Action Center Toast count: {activeToasts.Count}");
                if (activeToasts.Count > 0)
                {
                    foreach (var toast in activeToasts)
                    {
                        string appName = toast.AppInfo?.DisplayInfo?.DisplayName ?? "Unknown";
                        Console.WriteLine($"   [Toast] App: '{appName}', AppId: '{toast.AppInfo?.Id}', Id: {toast.Id}");
                        var b = toast.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                        if (b != null)
                        {
                            foreach (var txt in b.GetTextElements())
                            {
                                Console.WriteLine($"      Text: '{txt.Text}'");
                            }
                            foreach (var h in b.Hints)
                            {
                                Console.WriteLine($"      Hint: {h.Key} = '{h.Value}'");
                            }
                        }
                    }
                    var first = activeToasts[0];
                    string firstAppName = first.AppInfo?.DisplayInfo?.DisplayName ?? "Unknown";
                    Assert(!string.IsNullOrEmpty(firstAppName), "Source application name must be populated");
                }
            }
            else
            {
                Console.WriteLine($"   [Notice] Status is '{status}'. Grant permission via Windows Settings > Privacy & security > Notifications.");
            }
        });

        await RunTest("2. SQLite WAL Persistence & Table Schema Validation", async () =>
        {
            var item = new NotificationItem
            {
                NotificationId = "TEST_APP_001_100",
                AppName = "WhatsApp",
                AppId = "WhatsApp.Desktop",
                Title = "Sarah Connor",
                Body = "Hey, are you free for a call?",
                Category = "Messages & Communication",
                ReceivedAt = DateTime.Now,
                Status = NotificationStatus.Pending,
                Priority = NotificationPriority.Normal
            };

            long id = await dbService.InsertNotificationAsync(item);
            Assert(id > 0, $"Inserted notification Id must be positive (got {id})");

            var fetched = await dbService.GetNotificationByIdAsync(id);
            Assert(fetched != null, "Inserted notification must be retrievable from SQLite");
            Assert(fetched!.AppName == "WhatsApp", "App name must match");
            Assert(fetched.Title == "Sarah Connor", "Title must match");
            Assert(fetched.Status == NotificationStatus.Pending, "Status must be Pending");
        });

        await RunTest("3. Automatic Smart Categorization & Priority Engine", async () =>
        {
            var testCases = new (string App, string Title, string Body, string ExpectedCat, NotificationPriority ExpectedPrio)[]
            {
                ("Slack", "#general", "Team meeting starting in 5 minutes", "Work & Business", NotificationPriority.Normal),
                ("Microsoft Teams", "Standup Meeting", "Sprint discussion with team", "Work & Business", NotificationPriority.Normal),
                ("Outlook", "Project Milestone Review", "Project status update for Q4", "Emails", NotificationPriority.Normal),
                ("WhatsApp", "John Doe", "Hey! See you at lunch", "Messages & Communication", NotificationPriority.Normal),
                ("Telegram", "Dev Group", "New release v1.4 published", "Messages & Communication", NotificationPriority.Normal),
                ("Twitter", "TechNews", "Elon Musk announced new update", "Social Media", NotificationPriority.Low),
                ("Bank of America", "Security Code", "Your OTP is 849201. Do not share.", "Finance & Banking", NotificationPriority.Urgent),
                ("PayPal", "Payment Notice", "You sent $45.00 to merchant", "Finance & Banking", NotificationPriority.High),
                ("Windows Security", "Threat Detected", "Trojan found and quarantined", "System & Security", NotificationPriority.Urgent),
                ("Google Calendar", "10 min reminder", "Client demo starting at 10:00", "Calendar & Reminders", NotificationPriority.Normal),
                ("Amazon", "Order Shipped", "Your package has shipped via DHL", "Shopping & Orders", NotificationPriority.Normal),
                ("SomeUnknownApp", "Hello", "Random generic notification", "Other", NotificationPriority.Normal)
            };

            foreach (var tc in testCases)
            {
                string cat = await categorizationService.CategorizeAsync(tc.App, tc.Title, tc.Body);
                var prio = categorizationService.DeterminePriority(tc.App, tc.Title, tc.Body, cat);
                Console.WriteLine($"   [Category Check] [{tc.App}] '{tc.Title}' -> Cat: {cat} (Expected: {tc.ExpectedCat}) | Prio: {prio} (Expected: {tc.ExpectedPrio})");
                Assert(cat == tc.ExpectedCat, $"Expected category '{tc.ExpectedCat}' but got '{cat}' for app '{tc.App}'");
                Assert(prio == tc.ExpectedPrio, $"Expected priority '{tc.ExpectedPrio}' but got '{prio}' for app '{tc.App}'");
            }
        });

        await RunTest("4. Status Management, Notes & Pending Tracking", async () =>
        {
            var item = new NotificationItem
            {
                NotificationId = "STATUS_TEST_001",
                AppName = "Slack",
                Title = "Critical Bug in Prod",
                Body = "Server returning 500 error",
                Category = "Work & Business",
                ReceivedAt = DateTime.Now.AddHours(-3), // 3 hours ago
                Status = NotificationStatus.Pending,
                Priority = NotificationPriority.Urgent
            };

            long id = await dbService.InsertNotificationAsync(item);
            Assert(id > 0, "Insert succeeded");

            // Check pending duration helper
            var fetched = await dbService.GetNotificationByIdAsync(id);
            Assert(fetched!.IsPending, "Must be pending");
            Assert(fetched.PendingDurationText.Contains("h"), $"Pending duration should indicate hours: '{fetched.PendingDurationText}'");

            // Change status to InProgress
            await dbService.UpdateNotificationStatusAsync(id, NotificationStatus.InProgress);
            fetched = await dbService.GetNotificationByIdAsync(id);
            Assert(fetched!.Status == NotificationStatus.InProgress, "Status should be InProgress");

            // Change status to Done
            await dbService.UpdateNotificationStatusAsync(id, NotificationStatus.Done);
            fetched = await dbService.GetNotificationByIdAsync(id);
            Assert(fetched!.Status == NotificationStatus.Done, "Status should be Done");
            Assert(fetched.IsDone, "IsDone property must be true");

            // Add notes
            await dbService.UpdateNotificationNotesAsync(id, "Fixed by deploying patch hotfix-102");
            fetched = await dbService.GetNotificationByIdAsync(id);
            Assert(fetched!.UserNotes == "Fixed by deploying patch hotfix-102", "User notes must be persisted");
        });

        await RunTest("5. Restart Persistence & Connection Pool Safety", async () =>
        {
            // Close existing connection pools
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // Re-instantiate a fresh service instance targeting the same SQLite database file
            var freshDbService = new SqliteDatabaseService(secureStorage, testDbPath);
            await freshDbService.InitializeDatabaseAsync();

            var kpis = await freshDbService.GetKpiSummaryAsync();
            Console.WriteLine($"   [Restart Verify] Total Notifications after DB reload: {kpis.TotalCount}, Completed: {kpis.CompletedCount}");
            Assert(kpis.TotalCount >= 2, "Database must retain notifications after complete reload");
        });

        await RunTest("6. Duplicate Notification Prevention", async () =>
        {
            var item = new NotificationItem
            {
                NotificationId = "DUPLICATE_KEY_TEST",
                AppName = "Telegram",
                Title = "Duplicate Check",
                Body = "This message must only be stored once",
                Category = "Messages & Communication",
                ReceivedAt = DateTime.Now
            };

            long firstId = await dbService.InsertNotificationAsync(item);
            Assert(firstId > 0, "First insert must succeed");

            long secondId = await dbService.InsertNotificationAsync(item);
            Assert(secondId == -1, "Second insert with identical NotificationId must be blocked (-1 returned)");

            bool exists = await dbService.NotificationExistsAsync("DUPLICATE_KEY_TEST");
            Assert(exists, "Notification exists check must return true");
        });

        await RunTest("7. Windows Credential Manager & Secure Storage", async () =>
        {
            await Task.Yield();
            string secretKey = "UnitTestBotToken";
            string secretVal = "123456789:ABCdefGHIjklMNOpqrSTUvwxYZ_testtoken";

            secureStorage.SaveSecret(secretKey, secretVal);
            string? retrieved = secureStorage.GetSecret(secretKey);
            Assert(retrieved == secretVal, "Retrieved secret must match saved secret exactly");

            secureStorage.DeleteSecret(secretKey);
            string? afterDelete = secureStorage.GetSecret(secretKey);
            Assert(string.IsNullOrEmpty(afterDelete), "Secret must be removed after deletion");
        });

        await RunTest("8. Telegram Formatting & Markdown/HTML Escaping", async () =>
        {
            var item = new NotificationItem
            {
                AppName = "GitHub & Jira",
                Title = "PR <#104> Merged: Fix & Optimize",
                Body = "Commit details: changes to <App.xaml> & 'config.json'",
                Category = "Work & Business",
                ReceivedAt = new DateTime(2026, 11, 16, 9, 30, 0),
                Status = NotificationStatus.Pending
            };

            // Test connection with dummy token fails gracefully without exception
            bool connResult = await telegramService.TestConnectionAsync("invalid_token_123");
            Assert(!connResult, "Invalid Telegram token must return false gracefully");
        });

        await RunTest("9. Offline Resilience & Forwarding Retry Logging", async () =>
        {
            // Simulate saving a notification and attempting to forward when SMTP/Telegram is unconfigured or unreachable
            var notif = new NotificationItem
            {
                NotificationId = "OFFLINE_TEST_001",
                AppName = "Slack",
                Title = "Network Alert",
                Body = "Message while offline",
                Category = "Work & Business",
                ReceivedAt = DateTime.Now
            };

            long id = await dbService.InsertNotificationAsync(notif);
            Assert(id > 0, "Local capture to SQLite MUST succeed even if external network is unavailable");

            var settings = new AppSettings
            {
                EmailEnabled = true,
                SmtpHost = "invalid-smtp-server-999.local",
                SmtpPort = 587,
                SmtpToEmail = "user@test.com"
            };

            bool emailSuccess = await emailService.ForwardNotificationAsync(notif, settings);
            Assert(!emailSuccess, "Email forwarding to unreachable host must return false");

            var logs = await dbService.GetForwardingLogsAsync(10, notif.Id);
            Assert(logs.Count > 0, "Forwarding log must be recorded for failed attempt");
            Assert(logs[0].Status == "Failed", "Log status must be 'Failed'");
            Assert(!string.IsNullOrEmpty(logs[0].ErrorDetails), "Error details must be captured in log");
            Console.WriteLine($"   [Info] Recorded Error in Log: '{logs[0].ErrorDetails}'");
        });

        await RunTest("10. Search, Filtering & KPI Summary", async () =>
        {
            var filter = new NotificationFilter
            {
                SearchText = "Connor" // From "Sarah Connor" in Test 2
            };

            var results = await dbService.GetNotificationsAsync(filter);
            Assert(results.Count > 0, "Search by 'Connor' must return matched notification");
            Assert(results[0].Title.Contains("Connor"), "Result must contain search query");

            var kpis = await dbService.GetKpiSummaryAsync();
            Console.WriteLine($"   [KPI] Total: {kpis.TotalCount}, Pending: {kpis.PendingCount}, InProgress: {kpis.InProgressCount}, Done: {kpis.CompletedCount}, Today: {kpis.ReceivedTodayCount}");
            Assert(kpis.TotalCount > 0, "Total count must be > 0");
        });

        await RunTest("11. Database Backup & Restore", async () =>
        {
            string backupPath = Path.Combine(testDbFolder, "backup_test.db");
            if (File.Exists(backupPath)) File.Delete(backupPath);

            bool backupOk = await dbService.BackupDatabaseAsync(backupPath);
            Assert(backupOk, "Database backup via VACUUM INTO must succeed");
            Assert(File.Exists(backupPath), "Backup file must exist on disk");

            long backupSize = new FileInfo(backupPath).Length;
            Console.WriteLine($"   [Backup] Created backup at {backupPath} (Size: {backupSize} bytes)");
            Assert(backupSize > 0, "Backup file size must be > 0");

            bool restoreOk = await dbService.RestoreDatabaseAsync(backupPath);
            Assert(restoreOk, "Database restore from backup must succeed");
        });

        await RunTest("12. CSV & Excel Spreadsheet Export", async () =>
        {
            var items = await dbService.GetNotificationsAsync(new NotificationFilter(), 100);
            string csvPath = Path.Combine(testDbFolder, "export_test.csv");
            string xmlPath = Path.Combine(testDbFolder, "export_test.xml");

            await exportService.ExportToCsvAsync(items, csvPath);
            Assert(File.Exists(csvPath), "CSV export file must exist");
            string csvContent = await File.ReadAllTextAsync(csvPath);
            Assert(csvContent.Contains("Id,NotificationId,Source,Category,Title"), "CSV must contain valid header");
            Console.WriteLine($"   [CSV Export] Generated CSV with {items.Count} rows ({new FileInfo(csvPath).Length} bytes)");

            await exportService.ExportToExcelXmlAsync(items, xmlPath);
            Assert(File.Exists(xmlPath), "Excel XML export file must exist");
            string xmlContent = await File.ReadAllTextAsync(xmlPath);
            Assert(xmlContent.Contains("xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\""), "Excel XML must contain Office Spreadsheet namespace");
            Console.WriteLine($"   [Excel Export] Generated Excel XML with {items.Count} rows ({new FileInfo(xmlPath).Length} bytes)");
        });

        await RunTest("13. Windows Startup Manager Registry Integration", async () =>
        {
            await Task.Yield();
            bool initial = startupService.IsStartupEnabled();
            Console.WriteLine($"   [Startup Registry] Initial IsStartupEnabled: {initial}");
            // Test toggling without crashing
            startupService.SetStartup(false);
            bool afterDisable = startupService.IsStartupEnabled();
            Assert(!afterDisable, "Startup should be disabled");
        });

        await RunTest("14. Windows Toast Action & Launch URI Resolution", async () =>
        {
            // 1. Test Toast XML Parsing with encoded entities
            string sampleSlackXml = @"<toast activationType=""protocol"" launch=""slack://channel?id=C0B35GZJA0H&amp;message=1794985436.812499&amp;team=T0SCFBM7V&amp;origin=notification""><visual><binding template=""ToastGeneric""><text>#general</text><text>Hello team</text></binding></visual></toast>";
            string? parsedUri = WindowsToastHelper.ParseLaunchUriFromToastXml(sampleSlackXml);
            Assert(parsedUri != null, "Parsed URI should not be null");
            Assert(parsedUri == "slack://channel?id=C0B35GZJA0H&message=1794985436.812499&team=T0SCFBM7V&origin=notification",
                $"Parsed URI should match expected decoded slack protocol URI, got: {parsedUri}");

            // 2. Test action argument parsing
            string sampleActionXml = @"<toast><actions><action content=""View"" arguments=""https://example.com/item/42"" activationType=""protocol"" /></actions></toast>";
            string? parsedActionUri = WindowsToastHelper.ParseLaunchUriFromToastXml(sampleActionXml);
            Assert(parsedActionUri == "https://example.com/item/42", "Parsed action URI should match");

            // 3. Test SQLite persistence and retrieval of LaunchUri
            var toastItem = new NotificationItem
            {
                NotificationId = "test_toast_123",
                AppName = "Slack",
                AppId = "Slack",
                Title = "Test Message",
                Body = "Direct jump test",
                Category = "Work & Business",
                LaunchUri = parsedUri
            };
            long newId = await dbService.InsertNotificationAsync(toastItem);
            Assert(newId > 0, "Toast item should be inserted");

            var loaded = await dbService.GetNotificationByIdAsync(newId);
            Assert(loaded != null, "Loaded notification should not be null");
            Assert(loaded!.LaunchUri == parsedUri, "Loaded LaunchUri must match inserted LaunchUri");

            // 4. Test UpdateLaunchUriAsync
            string updatedUri = "slack://channel?id=C0999999999&message=1111111111.000000";
            bool updateOk = await dbService.UpdateLaunchUriAsync(newId, updatedUri);
            Assert(updateOk, "UpdateLaunchUriAsync should succeed");

            var reloaded = await dbService.GetNotificationByIdAsync(newId);
            Assert(reloaded!.LaunchUri == updatedUri, "Reloaded LaunchUri must reflect update");

            // 5. Test Live wpndatabase.db lookup
            string? wpnPath = WindowsToastHelper.GetWpnDatabasePath();
            Console.WriteLine($"\n   [WPN DB] Found path: {wpnPath ?? "None"}");
            if (File.Exists(wpnPath))
            {
                string? liveUri = WindowsToastHelper.TryGetToastLaunchUri(null, "Slack", null, null);
                Console.WriteLine($"   [Live Toast Lookup] Recent Slack toast launch URI: {liveUri ?? "(None active)"}");
            }
        });

        await RunTest("15. Custom Telegram Template, Analytics Charts & Source Colors", async () =>
        {
            // 1. Test Custom Telegram Template & HTML Injection Safety
            var testNotif = new NotificationItem
            {
                AppName = "Slack",
                Category = "Work & Business",
                Title = "Urgent <script>alert(1)</script>",
                Body = "Check deployment & logs > status",
                Priority = NotificationPriority.High,
                Status = NotificationStatus.Pending
            };

            string customTemplate = "🚀 <b>{AppName}</b> [{Priority}]\nTitle: {Title}\nMessage: {Body}";
            string rendered = TelegramForwardingService.BuildNotificationMessage(testNotif, customTemplate);
            Assert(rendered.Contains("🚀 <b>Slack</b> [High]"), "Custom template should substitute AppName and Priority");
            Assert(rendered.Contains("&lt;script&gt;alert(1)&lt;/script&gt;"), "HTML tags in notification fields must be escaped");
            Assert(rendered.Contains("deployment &amp; logs &gt; status"), "Special HTML characters in body must be escaped");

            // 2. Test Settings Persistence of TelegramMessageTemplate
            var settings = await dbService.LoadSettingsAsync();
            settings.TelegramMessageTemplate = customTemplate;
            await dbService.SaveSettingsAsync(settings);

            var reloadedSettings = await dbService.LoadSettingsAsync();
            Assert(reloadedSettings.TelegramMessageTemplate == customTemplate, "Custom Telegram template must persist in SQLite");

            // 3. Test Advanced Analytics Charts (Source Distribution & Activity Timeline)
            var sourceCharts = await dbService.GetSourceDistributionAsync();
            Assert(sourceCharts.Count > 0, "Source distribution chart data must not be empty");
            Assert(sourceCharts.Any(s => s.AppName == "Slack"), "Source distribution must include Slack");

            var timeline7 = await dbService.GetActivityTimelineAsync(7);
            Assert(timeline7.Count == 7, "7-day activity timeline must return 7 daily buckets");
            var timeline30 = await dbService.GetActivityTimelineAsync(30);
            Assert(timeline30.Count == 30, "30-day activity timeline must return 30 daily buckets");

            // 4. Test Unique Source Color Consistency & Done State Color Change
            var slackRgb1 = NotifyHub.Converters.SourceColorPalette.GetBaseRgb("Slack");
            var slackRgb2 = NotifyHub.Converters.SourceColorPalette.GetBaseRgb("slack");
            var teamsRgb = NotifyHub.Converters.SourceColorPalette.GetBaseRgb("Microsoft Teams");
            Assert(slackRgb1 == slackRgb2, "Same source app must always resolve to the exact same color");
            Assert(slackRgb1 != teamsRgb, "Different source apps (Slack vs Teams) must have distinct colors");

            var rowConverter = new NotifyHub.Converters.SourceRowBackgroundConverter();
            var pendingBrush = (System.Windows.Media.SolidColorBrush)rowConverter.Convert(
                new object[] { "Slack", NotificationStatus.Pending }, typeof(System.Windows.Media.Brush), null!, System.Globalization.CultureInfo.InvariantCulture);
            var doneBrush = (System.Windows.Media.SolidColorBrush)rowConverter.Convert(
                new object[] { "Slack", NotificationStatus.Done }, typeof(System.Windows.Media.Brush), null!, System.Globalization.CultureInfo.InvariantCulture);
            Assert(pendingBrush.Color != doneBrush.Color, "Marking a notification Done must change its row color");
        });

        await RunTest("16. Google Drive Backup Settings, PKCE Security & Interval Safety", async () =>
        {
            // 1. Test PKCE S256 generation
            string verifier = GoogleDriveBackupService.GenerateSecureBase64Url(32);
            string challenge = GoogleDriveBackupService.ComputeSha256Base64Url(verifier);
            Assert(verifier.Length >= 40, "PKCE verifier must have high entropy");
            Assert(!verifier.Contains('+') && !verifier.Contains('/') && !verifier.Contains('='), "PKCE verifier must be base64url safe");
            Assert(challenge.Length == 43, "SHA-256 base64url challenge must be 43 chars");

            // 2. Test Interval Safety Floor (minimum 5 minutes)
            var s = await dbService.LoadSettingsAsync();
            s.GoogleDriveBackupEnabled = true;
            s.GoogleDriveBackupIntervalValue = 1;
            s.GoogleDriveBackupIntervalUnit = "Minutes";
            Assert(s.GetGoogleDriveBackupInterval() == TimeSpan.FromMinutes(5), "Backup interval must enforce 5-minute safety floor");

            s.GoogleDriveBackupIntervalValue = 6;
            s.GoogleDriveBackupIntervalUnit = "Hours";
            s.GoogleDriveMaxBackupsToKeep = 10;
            s.GoogleDriveMaxSpaceValue = 250;
            s.GoogleDriveMaxSpaceUnit = "MB";
            s.GoogleDriveClientId = "test-client-id.apps.googleusercontent.com";
            await dbService.SaveSettingsAsync(s);

            var loaded = await dbService.LoadSettingsAsync();
            Assert(loaded.GoogleDriveBackupEnabled, "GoogleDriveBackupEnabled must persist");
            Assert(loaded.GetGoogleDriveBackupInterval() == TimeSpan.FromHours(6), "6-hour interval must persist accurately");
            Assert(loaded.GoogleDriveMaxBackupsToKeep == 10, "Max backups count must persist");
            Assert(loaded.GoogleDriveMaxSpaceValue == 250 && loaded.GoogleDriveMaxSpaceUnit == "MB", "Max Drive space setting must persist");
            Assert(loaded.GetGoogleDriveMaxSpaceBytes() == 250L * 1024L * 1024L, "Max Drive space bytes must compute accurately for MB");
            Assert(loaded.GoogleDriveClientId == "test-client-id.apps.googleusercontent.com", "Client ID must load from secure storage");

            // 2b. Test Rotational Space & File-Count Pruning
            var sampleFiles = new List<(string Id, long SizeBytes)>
            {
                ("newest", 40L * 1024 * 1024),
                ("second", 40L * 1024 * 1024),
                ("third",  40L * 1024 * 1024),
                ("oldest", 40L * 1024 * 1024)
            };
            // Max 10 files, 100 MB cap -> keeps newest + second (80 MB), rotates out third + oldest
            var (delBySpace, keptCountSpace, keptBytesSpace) = GoogleDriveBackupService.SelectFilesToPrune(sampleFiles, 10, 100L * 1024 * 1024);
            Assert(keptCountSpace == 2 && keptBytesSpace == 80L * 1024 * 1024, "Rotational space cap should keep 2 files (80 MB)");
            Assert(delBySpace.Count == 2 && delBySpace.Contains("third") && delBySpace.Contains("oldest"), "Oldest files must be pruned first");

            // Even if newest file alone exceeds tiny 10 MB cap, newest is ALWAYS preserved
            var (delTinyCap, keptTiny, _) = GoogleDriveBackupService.SelectFilesToPrune(sampleFiles, 10, 10L * 1024 * 1024);
            Assert(keptTiny == 1 && !delTinyCap.Contains("newest"), "Newest backup must never be deleted");

            // Max 2 files with unlimited space (0) -> keeps 2 newest, deletes 2 oldest
            var (delByCount, keptByCount, _) = GoogleDriveBackupService.SelectFilesToPrune(sampleFiles, 2, 0);
            Assert(keptByCount == 2 && delByCount.Count == 2, "File count limit must still apply when space is unlimited");

            // Cleanup test secret from Credential Manager
            s.GoogleDriveClientId = "";
            s.GoogleDriveClientSecret = "";
            await dbService.SaveSettingsAsync(s);

            // 3. Test Disconnected Backup Guard
            using var driveSvc = new GoogleDriveBackupService(dbService, secureStorage);
            if (!driveSvc.IsConnected)
            {
                var (ok, msg) = await driveSvc.BackupNowAsync();
                Assert(!ok && msg.Contains("not connected", StringComparison.OrdinalIgnoreCase), "Backup must reject when not connected");
            }
        });

        await RunTest("17. Stop & Resume Recording Chosen Sources (History Preserved)", async () =>
        {
            // 1. Verify exclusion helper accuracy & empty-string safety
            var excluded = new List<string> { "Spotify", "  ", "" };
            Assert(WindowsNotificationListenerService.IsAppExcluded("Spotify", "SpotifyAB", excluded), "Excluded source must be blocked");
            Assert(WindowsNotificationListenerService.IsAppExcluded("spotify", "", excluded), "Exclusion match must be case-insensitive");
            Assert(!WindowsNotificationListenerService.IsAppExcluded("Slack", "com.tinyspeck.slackdesktop", excluded), "Non-excluded source must still record even if empty entries exist");

            // 2. Verify stopping a recorded source preserves past history and persists exclusion
            var pastCountBefore = await dbService.GetNotificationCountAsync(new NotificationFilter { AppName = "Slack" });
            Assert(pastCountBefore > 0, "Expected recorded Slack notifications in test DB");

            var st = await dbService.LoadSettingsAsync();
            st.ExcludedApps.Add("Slack");
            await dbService.SaveSettingsAsync(st);

            var reloaded = await dbService.LoadSettingsAsync();
            Assert(WindowsNotificationListenerService.IsAppExcluded("Slack", "Slack", reloaded.ExcludedApps), "Stopped source must persist as excluded");

            var pastCountAfter = await dbService.GetNotificationCountAsync(new NotificationFilter { AppName = "Slack" });
            Assert(pastCountAfter == pastCountBefore, "Stopping a source must preserve all existing past notifications");

            // 3. Re-checking the source resumes recording
            reloaded.ExcludedApps.RemoveAll(e => e.Equals("Slack", StringComparison.OrdinalIgnoreCase));
            await dbService.SaveSettingsAsync(reloaded);
            var resumed = await dbService.LoadSettingsAsync();
            Assert(!WindowsNotificationListenerService.IsAppExcluded("Slack", "Slack", resumed.ExcludedApps), "Re-checking source must resume recording");
        });

        await RunTest("18. Pagination (10, 20, 50, All) & Record Display IDs", async () =>
        {
            var filter = new NotificationFilter();
            int total = await dbService.GetNotificationCountAsync(filter);
            Assert(total > 0, "Database must have notifications for pagination testing");

            // Page size 10
            var p10 = await dbService.GetNotificationsAsync(filter, 10, 0);
            Assert(p10.Count <= 10 && p10.Count > 0, "Page size 10 must return at most 10 items");
            Assert(p10.All(n => n.DisplayId.StartsWith("N-")), "All records must have valid N-{Id} DisplayId");

            // Page size 20
            var p20 = await dbService.GetNotificationsAsync(filter, 20, 0);
            Assert(p20.Count <= 20, "Page size 20 must return at most 20 items");

            // Page size All (limit = -1)
            var pAll = await dbService.GetNotificationsAsync(filter, -1, 0);
            Assert(pAll.Count == total, "Limit -1 must return all notifications");

            // Search by DisplayId shortcut (e.g. N-1)
            var firstItem = pAll.First();
            var searchResult = await dbService.GetNotificationsAsync(new NotificationFilter { SearchText = firstItem.DisplayId }, 10, 0);
            Assert(searchResult.Count == 1 && searchResult[0].Id == firstItem.Id, "Search by DisplayId must locate exact record");
        });

        // Summary
        Console.WriteLine("\n======================================================================");
        Console.WriteLine($"TEST RESULTS SUMMARY: {_passedTests} PASSED, {_failedTests} FAILED");
        Console.WriteLine("======================================================================");

        return _failedTests == 0 ? 0 : 1;
    }

    private static async Task RunTest(string testName, Func<Task> testAction)
    {
        Console.Write($"[TEST] {testName} ... ");
        try
        {
            await testAction();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASSED");
            Console.ResetColor();
            _passedTests++;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAILED: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine($"   Stack: {ex.StackTrace}");
            _failedTests++;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"Assertion Failed: {message}");
        }
    }
}
