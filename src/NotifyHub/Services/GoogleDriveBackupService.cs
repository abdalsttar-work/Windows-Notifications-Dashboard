using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NotifyHub.Models;

namespace NotifyHub.Services;

public interface IGoogleDriveBackupService : IDisposable
{
    bool IsConnected { get; }
    event EventHandler<string>? BackupStatusChanged;

    Task<(bool Success, string Message)> ConnectAsync(string clientId, string? clientSecret, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<(bool Success, string Message)> BackupNowAsync(CancellationToken cancellationToken = default);
    void StartScheduler();
    void StopScheduler();
}

public class GoogleDriveBackupService : IGoogleDriveBackupService
{
    private const string DriveScope = "https://www.googleapis.com/auth/drive.file";
    private const string BackupFolderName = "NotifyHub Backups";
    private const string BackupFilePrefix = "NotifyHub_Backup_";

    private const string KeyRefreshToken = "GoogleDriveRefreshToken";
    private const string KeyAccessToken = "GoogleDriveAccessToken";
    private const string KeyTokenExpiry = "GoogleDriveTokenExpiryUtc";
    private const string KeyFolderId = "GoogleDriveBackupFolderId";

    private readonly IDatabaseService _databaseService;
    private readonly ISecureStorageService _secureStorage;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _backupLock = new(1, 1);
    private System.Threading.Timer? _schedulerTimer;

    public bool IsConnected => !string.IsNullOrWhiteSpace(_secureStorage.GetSecret(KeyRefreshToken));
    public event EventHandler<string>? BackupStatusChanged;

    public GoogleDriveBackupService(
        IDatabaseService databaseService,
        ISecureStorageService secureStorage,
        HttpClient? httpClient = null)
    {
        _databaseService = databaseService;
        _secureStorage = secureStorage;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    public async Task<(bool Success, string Message)> ConnectAsync(string clientId, string? clientSecret, CancellationToken cancellationToken = default)
    {
        string cleanClientId = (clientId ?? string.Empty).Trim();
        string cleanClientSecret = (clientSecret ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(cleanClientId))
        {
            return (false, "Please enter your Google OAuth Desktop Client ID first.");
        }

        // Generate PKCE verifier/challenge and CSRF state nonce
        string codeVerifier = GenerateSecureBase64Url(32);
        string codeChallenge = ComputeSha256Base64Url(codeVerifier);
        string expectedState = GenerateSecureBase64Url(16);

        int port = GetFreeLoopbackPort();
        string redirectUri = $"http://127.0.0.1:{port}/callback/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);

        try
        {
            listener.Start();

            string authUrl =
                "https://accounts.google.com/o/oauth2/v2/auth" +
                $"?client_id={Uri.EscapeDataString(cleanClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                "&response_type=code" +
                $"&scope={Uri.EscapeDataString(DriveScope)}" +
                $"&state={Uri.EscapeDataString(expectedState)}" +
                $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                "&code_challenge_method=S256" +
                "&access_type=offline" +
                "&prompt=consent";

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));

            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, timeoutCts.Token));
            if (completed != contextTask)
            {
                return (false, "Google sign-in timed out. Please try again.");
            }

            var context = await contextTask;
            string? returnedState = context.Request.QueryString["state"];
            string? authCode = context.Request.QueryString["code"];
            string? error = context.Request.QueryString["error"];

            bool stateValid = !string.IsNullOrEmpty(returnedState) &&
                              FixedTimeEquals(returnedState, expectedState);

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(authCode) || !stateValid)
            {
                await WriteBrowserResponseAsync(context.Response, "Notify Hub Sign-In Failed", "Authorization was cancelled or failed security validation. You may close this tab.");
                return (false, string.IsNullOrEmpty(error) ? "Authorization failed security check." : $"Google sign-in denied: {error}");
            }

            await WriteBrowserResponseAsync(context.Response, "Notify Hub Connected", "Google Drive connected securely! You may close this browser tab and return to Notify Hub.");

            // Exchange authorization code for OAuth tokens
            var form = new Dictionary<string, string>
            {
                ["client_id"] = cleanClientId,
                ["code"] = authCode,
                ["code_verifier"] = codeVerifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            };
            if (!string.IsNullOrEmpty(cleanClientSecret))
            {
                form["client_secret"] = cleanClientSecret;
            }

            using var tokenRes = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), cancellationToken);
            string body = await tokenRes.Content.ReadAsStringAsync(cancellationToken);
            if (!tokenRes.IsSuccessStatusCode)
            {
                return (false, $"Token exchange failed ({tokenRes.StatusCode}). Verify your Client ID and Client Secret.");
            }

            using var doc = JsonDocument.Parse(body);
            string? accessToken = doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            string? refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            int expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3500;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return (false, "Google did not return a valid access token.");
            }

            _secureStorage.SaveSecret(KeyAccessToken, accessToken);
            _secureStorage.SaveSecret(KeyTokenExpiry, DateTime.UtcNow.AddSeconds(expiresIn - 60).ToString("o"));
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                _secureStorage.SaveSecret(KeyRefreshToken, refreshToken);
            }

            // Persist client credentials securely so automatic backups can refresh tokens
            _secureStorage.SaveSecret("GoogleDriveClientId", cleanClientId);
            if (!string.IsNullOrEmpty(cleanClientSecret))
                _secureStorage.SaveSecret("GoogleDriveClientSecret", cleanClientSecret);

            NotifyStatus("✅ Connected to Google Drive securely (drive.file scope).");
            return (true, "✅ Connected to Google Drive successfully!");
        }
        catch (Exception ex)
        {
            return (false, $"Google Drive connection error: {ex.Message}");
        }
        finally
        {
            if (listener.IsListening) listener.Stop();
        }
    }

    public async Task DisconnectAsync()
    {
        string? token = _secureStorage.GetSecret(KeyRefreshToken) ?? _secureStorage.GetSecret(KeyAccessToken);
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("token", token) });
                await _httpClient.PostAsync("https://oauth2.googleapis.com/revoke", content);
            }
            catch
            {
                // Best-effort remote revocation; always clear local tokens regardless
            }
        }

        _secureStorage.DeleteSecret(KeyRefreshToken);
        _secureStorage.DeleteSecret(KeyAccessToken);
        _secureStorage.DeleteSecret(KeyTokenExpiry);
        _secureStorage.DeleteSecret(KeyFolderId);

        NotifyStatus("Disconnected from Google Drive.");
    }

    public async Task<(bool Success, string Message)> BackupNowAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return (false, "Google Drive is not connected.");
        }

        if (!await _backupLock.WaitAsync(0, cancellationToken))
        {
            return (false, "A Google Drive backup is already in progress.");
        }

        string tempFile = Path.Combine(Path.GetTempPath(), $"{BackupFilePrefix}{DateTime.Now:yyyyMMdd_HHmmss}.db");
        try
        {
            var settings = await _databaseService.LoadSettingsAsync();
            string? accessToken = await GetValidAccessTokenAsync(settings, cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return (false, "Session expired. Please reconnect Google Drive.");
            }

            // 1. Create atomic SQLite snapshot
            bool created = await _databaseService.BackupDatabaseAsync(tempFile);
            if (!created || !File.Exists(tempFile))
            {
                return (false, "Failed to create local SQLite backup snapshot.");
            }

            // 2. Ensure backup folder exists in Google Drive
            string folderId = await EnsureBackupFolderAsync(accessToken, cancellationToken);

            // 3. Multipart upload to Google Drive
            string fileName = Path.GetFileName(tempFile);
            await UploadBackupFileAsync(accessToken, folderId, fileName, tempFile, cancellationToken);

            // 4. Rotational cleanup: enforce BOTH max file count AND max Drive space limit (oldest removed first)
            int maxKeep = Math.Clamp(settings.GoogleDriveMaxBackupsToKeep, 1, 100);
            long maxBytes = settings.GetGoogleDriveMaxSpaceBytes();
            var (keptFiles, keptBytes) = await PruneOldBackupsAsync(accessToken, folderId, maxKeep, maxBytes, cancellationToken);

            // 5. Save timestamp
            settings.LastGoogleDriveBackupAt = DateTime.Now;
            await _databaseService.SaveSettingsAsync(settings);

            string usedText = FormatSize(keptBytes);
            string limitText = maxBytes > 0 ? $" / {settings.GoogleDriveMaxSpaceValue} {settings.GoogleDriveMaxSpaceUnit}" : "";
            string msg = $"✅ Backed up '{fileName}' at {DateTime.Now:HH:mm:ss} (Drive usage: {usedText}{limitText} across {keptFiles} backup(s)).";
            NotifyStatus(msg);
            return (true, msg);
        }
        catch (Exception ex)
        {
            string err = $"❌ Google Drive backup failed: {ex.Message}";
            NotifyStatus(err);
            return (false, err);
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            _backupLock.Release();
        }
    }

    public void StartScheduler()
    {
        StopScheduler();
        // Check every 60 seconds if a scheduled backup is due
        _schedulerTimer = new System.Threading.Timer(
            async _ => await RunScheduledCheckSafeAsync(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1));
    }

    public void StopScheduler()
    {
        _schedulerTimer?.Dispose();
        _schedulerTimer = null;
    }

    private async Task RunScheduledCheckSafeAsync()
    {
        try
        {
            if (!IsConnected) return;

            var settings = await _databaseService.LoadSettingsAsync();
            if (!settings.GoogleDriveBackupEnabled) return;

            TimeSpan interval = settings.GetGoogleDriveBackupInterval();
            DateTime lastRun = settings.LastGoogleDriveBackupAt ?? DateTime.MinValue;
            if (DateTime.Now - lastRun >= interval)
            {
                await BackupNowAsync();
            }
        }
        catch
        {
            // Never crash background scheduler on transient network loss
        }
    }

    private async Task<string?> GetValidAccessTokenAsync(AppSettings settings, CancellationToken ct)
    {
        string? cachedToken = _secureStorage.GetSecret(KeyAccessToken);
        string? expiryStr = _secureStorage.GetSecret(KeyTokenExpiry);

        if (!string.IsNullOrWhiteSpace(cachedToken) &&
            DateTime.TryParse(expiryStr, out DateTime expiryUtc) &&
            DateTime.UtcNow < expiryUtc)
        {
            return cachedToken;
        }

        string? refreshToken = _secureStorage.GetSecret(KeyRefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(settings.GoogleDriveClientId))
        {
            return null;
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = settings.GoogleDriveClientId.Trim(),
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };
        if (!string.IsNullOrWhiteSpace(settings.GoogleDriveClientSecret))
        {
            form["client_secret"] = settings.GoogleDriveClientSecret.Trim();
        }

        using var res = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), ct);
        if (!res.IsSuccessStatusCode) return null;

        string json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        string? newAccess = doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        int expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3500;

        if (!string.IsNullOrWhiteSpace(newAccess))
        {
            _secureStorage.SaveSecret(KeyAccessToken, newAccess);
            _secureStorage.SaveSecret(KeyTokenExpiry, DateTime.UtcNow.AddSeconds(expiresIn - 60).ToString("o"));
        }

        return newAccess;
    }

    private async Task<string> EnsureBackupFolderAsync(string accessToken, CancellationToken ct)
    {
        string? cachedFolderId = _secureStorage.GetSecret(KeyFolderId);
        if (!string.IsNullOrWhiteSpace(cachedFolderId))
        {
            // Verify cached folder still exists and is not trashed
            using var checkReq = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(cachedFolderId)}?fields=id,trashed");
            checkReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var checkRes = await _httpClient.SendAsync(checkReq, ct);
            if (checkRes.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await checkRes.Content.ReadAsStringAsync(ct));
                bool trashed = doc.RootElement.TryGetProperty("trashed", out var tr) && tr.GetBoolean();
                if (!trashed) return cachedFolderId;
            }
        }

        // Search for existing NotifyHub Backups folder created by this app
        string q = Uri.EscapeDataString($"name = '{BackupFolderName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false");
        using (var searchReq = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id)&pageSize=1"))
        {
            searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var searchRes = await _httpClient.SendAsync(searchReq, ct);
            if (searchRes.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await searchRes.Content.ReadAsStringAsync(ct));
                var files = doc.RootElement.GetProperty("files");
                if (files.GetArrayLength() > 0)
                {
                    string existingId = files[0].GetProperty("id").GetString()!;
                    _secureStorage.SaveSecret(KeyFolderId, existingId);
                    return existingId;
                }
            }
        }

        // Create the folder
        string createJson = JsonSerializer.Serialize(new
        {
            name = BackupFolderName,
            mimeType = "application/vnd.google-apps.folder"
        });
        using var createReq = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/drive/v3/files?fields=id");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        createReq.Content = new StringContent(createJson, Encoding.UTF8, "application/json");

        using var createRes = await _httpClient.SendAsync(createReq, ct);
        createRes.EnsureSuccessStatusCode();
        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync(ct));
        string newFolderId = createDoc.RootElement.GetProperty("id").GetString()!;
        _secureStorage.SaveSecret(KeyFolderId, newFolderId);
        return newFolderId;
    }

    private async Task UploadBackupFileAsync(string accessToken, string folderId, string fileName, string localFilePath, CancellationToken ct)
    {
        string metadataJson = JsonSerializer.Serialize(new
        {
            name = fileName,
            parents = new[] { folderId }
        });

        using var multipart = new MultipartContent("related");
        var metaPart = new StringContent(metadataJson, Encoding.UTF8, "application/json");
        var fileBytes = await File.ReadAllBytesAsync(localFilePath, ct);
        var filePart = new ByteArrayContent(fileBytes);
        filePart.Headers.ContentType = new MediaTypeHeaderValue("application/x-sqlite3");

        multipart.Add(metaPart);
        multipart.Add(filePart);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = multipart;

        using var res = await _httpClient.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    private async Task<(int KeptFiles, long KeptBytes)> PruneOldBackupsAsync(
        string accessToken, string folderId, int maxToKeep, long maxTotalBytes, CancellationToken ct)
    {
        string q = Uri.EscapeDataString($"'{folderId}' in parents and name contains '{BackupFilePrefix}' and trashed = false");
        string listUrl = $"https://www.googleapis.com/drive/v3/files?q={q}&orderBy=createdTime desc&fields=files(id,name,size)&pageSize=100";

        using var listReq = new HttpRequestMessage(HttpMethod.Get, listUrl);
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var listRes = await _httpClient.SendAsync(listReq, ct);
        if (!listRes.IsSuccessStatusCode) return (0, 0);

        using var doc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync(ct));
        var filesJson = doc.RootElement.GetProperty("files");

        var entries = new List<(string Id, long SizeBytes)>();
        foreach (var f in filesJson.EnumerateArray())
        {
            string? id = f.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;

            long size = 0;
            if (f.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.String)
                long.TryParse(sizeProp.GetString(), out size);

            entries.Add((id, size));
        }

        var (toDelete, keptCount, keptBytes) = SelectFilesToPrune(entries, maxToKeep, maxTotalBytes);
        foreach (string oldId in toDelete)
        {
            using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(oldId)}");
            delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            try { await _httpClient.SendAsync(delReq, ct); } catch { }
        }

        return (keptCount, keptBytes);
    }

    // Rotational retention: always keeps the newest backup (index 0), then keeps newer backups
    // until either maxToKeep OR maxTotalBytes is reached, marking older backups for deletion.
    public static (List<string> ToDelete, int KeptCount, long KeptBytes) SelectFilesToPrune(
        IReadOnlyList<(string Id, long SizeBytes)> filesNewestFirst, int maxToKeep, long maxTotalBytes)
    {
        var toDelete = new List<string>();
        int keptCount = 0;
        long keptBytes = 0;
        int effectiveMaxFiles = Math.Max(1, maxToKeep);

        for (int i = 0; i < filesNewestFirst.Count; i++)
        {
            var (id, size) = filesNewestFirst[i];

            if (i == 0)
            {
                // Always retain the most recent backup so latest data is never lost
                keptCount = 1;
                keptBytes = size;
                continue;
            }

            bool exceedsCount = keptCount >= effectiveMaxFiles;
            bool exceedsSpace = maxTotalBytes > 0 && (keptBytes + size) > maxTotalBytes;

            if (exceedsCount || exceedsSpace)
            {
                toDelete.Add(id);
            }
            else
            {
                keptCount++;
                keptBytes += size;
            }
        }

        return (toDelete, keptCount, keptBytes);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        if (bytes >= 1024L) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private void NotifyStatus(string message) => BackupStatusChanged?.Invoke(this, message);

    private static int GetFreeLoopbackPort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    public static string GenerateSecureBase64Url(int byteCount)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string ComputeSha256Base64Url(string input)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(input));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        byte[] ba = Encoding.UTF8.GetBytes(a);
        byte[] bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, string title, string message)
    {
        string html = $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>{WebUtility.HtmlEncode(title)}</title>" +
                      "<style>body{font-family:Segoe UI,sans-serif;background:#181926;color:#cad3f5;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;}" +
                      ".card{background:#24273a;padding:32px;border-radius:12px;border:1px solid #363a4f;max-width:460px;text-align:center;}</style></head>" +
                      $"<body><div class='card'><h2>{WebUtility.HtmlEncode(title)}</h2><p>{WebUtility.HtmlEncode(message)}</p></div></body></html>";
        byte[] buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.OutputStream.Close();
    }

    public void Dispose() => StopScheduler();
}
