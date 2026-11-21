using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WpfMessageBox = System.Windows.MessageBox;
using NotifyHub.Models;
using NotifyHub.Services;

namespace NotifyHub.ViewModels;

public class RecordedSourceToggle : ViewModelBase
{
    private bool _isRecording;
    private readonly Action<RecordedSourceToggle>? _onToggled;

    public string AppName { get; }

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
                _onToggled?.Invoke(this);
            }
        }
    }

    public string StatusLabel => IsRecording ? "✅ Recording" : "🚫 Stopped";

    public RecordedSourceToggle(string appName, bool isRecording, Action<RecordedSourceToggle>? onToggled)
    {
        AppName = appName;
        _isRecording = isRecording;
        _onToggled = onToggled;
    }

    public void SetRecordingSilent(bool isRecording)
    {
        if (SetProperty(ref _isRecording, isRecording, nameof(IsRecording)))
        {
            OnPropertyChanged(nameof(StatusLabel));
        }
    }
}

public class SettingsViewModel : ViewModelBase
{
    private readonly IDatabaseService _databaseService;
    private readonly INotificationListenerService _listenerService;
    private readonly ITelegramForwardingService _telegramService;
    private readonly IEmailForwardingService _emailService;
    private readonly IStartupManager _startupManager;
    private readonly IGoogleDriveBackupService _googleDriveService;

    private AppSettings _settings = new();

    // Active Settings Tab: "General", "Monitoring", "Categories", "Telegram", "Email", "Data", "Logs"
    private string _selectedTab = "General";
    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    // General
    public bool LaunchOnStartup
    {
        get => _settings.LaunchOnStartup;
        set
        {
            if (_settings.LaunchOnStartup != value)
            {
                _settings.LaunchOnStartup = value;
                OnPropertyChanged();
                _startupManager.SetStartup(value);
            }
        }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set
        {
            if (_settings.MinimizeToTray != value)
            {
                _settings.MinimizeToTray = value;
                OnPropertyChanged();
            }
        }
    }

    public List<string> ThemeOptions { get; } = new() { "Dark", "Light" };

    public string Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme != value)
            {
                _settings.Theme = value;
                OnPropertyChanged();
                App.ApplyTheme(value);
            }
        }
    }

    public int RetentionDays
    {
        get => _settings.RetentionDays;
        set
        {
            if (_settings.RetentionDays != value)
            {
                _settings.RetentionDays = value;
                OnPropertyChanged();
            }
        }
    }

    // Monitoring
    public bool MonitoringEnabled
    {
        get => _settings.MonitoringEnabled;
        set
        {
            if (_settings.MonitoringEnabled != value)
            {
                _settings.MonitoringEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string MonitoringDiagnostics =>
        $"Listener Status: {_listenerService.AccessStatus}\n" +
        $"Running: {_listenerService.IsRunning}\n" +
        $"Paused: {_listenerService.IsPaused}\n" +
        $"Captured This Session: {_listenerService.TotalCapturedThisSession}\n" +
        $"Last Capture: {_listenerService.LastCaptureTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "None"}\n" +
        $"Database Path: {_databaseService.DatabasePath}";

    public ObservableCollection<string> CapturedApps { get; } = new();
    public ObservableCollection<string> ExcludedApps { get; } = new();
    public ObservableCollection<RecordedSourceToggle> RecordedSources { get; } = new();

    private string _sourceToggleStatus = string.Empty;
    public string SourceToggleStatus
    {
        get => _sourceToggleStatus;
        set => SetProperty(ref _sourceToggleStatus, value);
    }

    private string _newExcludedApp = string.Empty;
    public string NewExcludedApp
    {
        get => _newExcludedApp;
        set => SetProperty(ref _newExcludedApp, value);
    }

    // Categories & Rules
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<CategoryRule> Rules { get; } = new();

    private string _newCategoryName = string.Empty;
    public string NewCategoryName
    {
        get => _newCategoryName;
        set => SetProperty(ref _newCategoryName, value);
    }

    private string _newCategoryDesc = string.Empty;
    public string NewCategoryDesc
    {
        get => _newCategoryDesc;
        set => SetProperty(ref _newCategoryDesc, value);
    }

    private string _newRuleCategory = "Work & Business";
    public string NewRuleCategory
    {
        get => _newRuleCategory;
        set => SetProperty(ref _newRuleCategory, value);
    }

    private RuleMatchType _newRuleType = RuleMatchType.AppNameContains;
    public RuleMatchType NewRuleType
    {
        get => _newRuleType;
        set => SetProperty(ref _newRuleType, value);
    }

    private string _newRulePattern = string.Empty;
    public string NewRulePattern
    {
        get => _newRulePattern;
        set => SetProperty(ref _newRulePattern, value);
    }

    // Telegram
    public bool TelegramEnabled
    {
        get => _settings.TelegramEnabled;
        set
        {
            if (_settings.TelegramEnabled != value)
            {
                _settings.TelegramEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string TelegramBotToken
    {
        get => _settings.TelegramBotToken;
        set
        {
            if (_settings.TelegramBotToken != value)
            {
                _settings.TelegramBotToken = value;
                OnPropertyChanged();
            }
        }
    }

    public string TelegramChatId
    {
        get => _settings.TelegramChatId;
        set
        {
            if (_settings.TelegramChatId != value)
            {
                _settings.TelegramChatId = value;
                OnPropertyChanged();
            }
        }
    }

    public string TelegramMessageTemplate
    {
        get => _settings.TelegramMessageTemplate;
        set
        {
            if (_settings.TelegramMessageTemplate != value)
            {
                _settings.TelegramMessageTemplate = value;
                OnPropertyChanged();
            }
        }
    }

    private string _telegramTestStatus = string.Empty;
    public string TelegramTestStatus
    {
        get => _telegramTestStatus;
        set => SetProperty(ref _telegramTestStatus, value);
    }

    // Email
    public bool EmailEnabled
    {
        get => _settings.EmailEnabled;
        set
        {
            if (_settings.EmailEnabled != value)
            {
                _settings.EmailEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string SmtpHost
    {
        get => _settings.SmtpHost;
        set
        {
            if (_settings.SmtpHost != value)
            {
                _settings.SmtpHost = value;
                OnPropertyChanged();
            }
        }
    }

    public int SmtpPort
    {
        get => _settings.SmtpPort;
        set
        {
            if (_settings.SmtpPort != value)
            {
                _settings.SmtpPort = value;
                OnPropertyChanged();
            }
        }
    }

    public bool SmtpUseSsl
    {
        get => _settings.SmtpUseSsl;
        set
        {
            if (_settings.SmtpUseSsl != value)
            {
                _settings.SmtpUseSsl = value;
                OnPropertyChanged();
            }
        }
    }

    public string SmtpUsername
    {
        get => _settings.SmtpUsername;
        set
        {
            if (_settings.SmtpUsername != value)
            {
                _settings.SmtpUsername = value;
                OnPropertyChanged();
            }
        }
    }

    public string SmtpPassword
    {
        get => _settings.SmtpPassword;
        set
        {
            if (_settings.SmtpPassword != value)
            {
                _settings.SmtpPassword = value;
                OnPropertyChanged();
            }
        }
    }

    public string SmtpFromEmail
    {
        get => _settings.SmtpFromEmail;
        set
        {
            if (_settings.SmtpFromEmail != value)
            {
                _settings.SmtpFromEmail = value;
                OnPropertyChanged();
            }
        }
    }

    public string SmtpToEmail
    {
        get => _settings.SmtpToEmail;
        set
        {
            if (_settings.SmtpToEmail != value)
            {
                _settings.SmtpToEmail = value;
                OnPropertyChanged();
            }
        }
    }

    private string _emailTestStatus = string.Empty;
    public string EmailTestStatus
    {
        get => _emailTestStatus;
        set => SetProperty(ref _emailTestStatus, value);
    }

    // Data Management & Google Drive Backup
    public string DatabasePath => _databaseService.DatabasePath;

    private string _dbStatusMessage = string.Empty;
    public string DbStatusMessage
    {
        get => _dbStatusMessage;
        set => SetProperty(ref _dbStatusMessage, value);
    }

    public bool GoogleDriveBackupEnabled
    {
        get => _settings.GoogleDriveBackupEnabled;
        set { if (_settings.GoogleDriveBackupEnabled != value) { _settings.GoogleDriveBackupEnabled = value; OnPropertyChanged(); } }
    }

    public string GoogleDriveClientId
    {
        get => _settings.GoogleDriveClientId;
        set { if (_settings.GoogleDriveClientId != value) { _settings.GoogleDriveClientId = value; OnPropertyChanged(); } }
    }

    public string GoogleDriveClientSecret
    {
        get => _settings.GoogleDriveClientSecret;
        set { if (_settings.GoogleDriveClientSecret != value) { _settings.GoogleDriveClientSecret = value; OnPropertyChanged(); } }
    }

    public int GoogleDriveBackupIntervalValue
    {
        get => _settings.GoogleDriveBackupIntervalValue;
        set { if (_settings.GoogleDriveBackupIntervalValue != value) { _settings.GoogleDriveBackupIntervalValue = Math.Max(1, value); OnPropertyChanged(); } }
    }

    public List<string> BackupIntervalUnits { get; } = new() { "Minutes", "Hours", "Days" };

    public string GoogleDriveBackupIntervalUnit
    {
        get => _settings.GoogleDriveBackupIntervalUnit;
        set { if (_settings.GoogleDriveBackupIntervalUnit != value) { _settings.GoogleDriveBackupIntervalUnit = value; OnPropertyChanged(); } }
    }

    public int GoogleDriveMaxBackupsToKeep
    {
        get => _settings.GoogleDriveMaxBackupsToKeep;
        set { if (_settings.GoogleDriveMaxBackupsToKeep != value) { _settings.GoogleDriveMaxBackupsToKeep = Math.Clamp(value, 1, 100); OnPropertyChanged(); } }
    }

    public int GoogleDriveMaxSpaceValue
    {
        get => _settings.GoogleDriveMaxSpaceValue;
        set { if (_settings.GoogleDriveMaxSpaceValue != value) { _settings.GoogleDriveMaxSpaceValue = Math.Max(0, value); OnPropertyChanged(); } }
    }

    public List<string> StorageSpaceUnits { get; } = new() { "MB", "GB" };

    public string GoogleDriveMaxSpaceUnit
    {
        get => _settings.GoogleDriveMaxSpaceUnit;
        set { if (_settings.GoogleDriveMaxSpaceUnit != value) { _settings.GoogleDriveMaxSpaceUnit = value; OnPropertyChanged(); } }
    }

    public bool IsGoogleDriveConnected => _googleDriveService.IsConnected;

    public string GoogleDriveConnectionText => IsGoogleDriveConnected
        ? "✅ Connected to Google Drive (drive.file minimal scope)"
        : "⚪ Not Connected";

    public string LastGoogleDriveBackupText => _settings.LastGoogleDriveBackupAt.HasValue
        ? _settings.LastGoogleDriveBackupAt.Value.ToString("MMM dd, yyyy HH:mm:ss")
        : "Never";

    private string _googleDriveStatusMessage = string.Empty;
    public string GoogleDriveStatusMessage
    {
        get => _googleDriveStatusMessage;
        set => SetProperty(ref _googleDriveStatusMessage, value);
    }

    // Delivery Logs
    public ObservableCollection<ForwardingLog> ForwardingLogs { get; } = new();

    // Commands
    public ICommand SaveSettingsCommand { get; }
    public ICommand AddExcludedAppCommand { get; }
    public ICommand RemoveExcludedAppCommand { get; }
    public ICommand AddCategoryCommand { get; }
    public ICommand DeleteCategoryCommand { get; }
    public ICommand AddRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand TestTelegramConnectionCommand { get; }
    public ICommand SendTestTelegramMessageCommand { get; }
    public ICommand ResetTelegramTemplateCommand { get; }
    public ICommand SendTestEmailCommand { get; }
    public ICommand BackupDatabaseCommand { get; }
    public ICommand RestoreDatabaseCommand { get; }
    public ICommand ClearHistoryCommand { get; }
    public ICommand ConnectGoogleDriveCommand { get; }
    public ICommand DisconnectGoogleDriveCommand { get; }
    public ICommand BackupToGoogleDriveNowCommand { get; }
    public ICommand RefreshLogsCommand { get; }
    public ICommand RetryFailedDeliveriesCommand { get; }

    public SettingsViewModel(
        IDatabaseService databaseService,
        INotificationListenerService listenerService,
        ITelegramForwardingService telegramService,
        IEmailForwardingService emailService,
        IStartupManager startupManager,
        IGoogleDriveBackupService googleDriveService)
    {
        _databaseService = databaseService;
        _listenerService = listenerService;
        _telegramService = telegramService;
        _emailService = emailService;
        _startupManager = startupManager;
        _googleDriveService = googleDriveService;

        _googleDriveService.BackupStatusChanged += (s, msg) =>
        {
            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(async () =>
            {
                GoogleDriveStatusMessage = msg;
                _settings = await _databaseService.LoadSettingsAsync();
                OnPropertyChanged(nameof(IsGoogleDriveConnected));
                OnPropertyChanged(nameof(GoogleDriveConnectionText));
                OnPropertyChanged(nameof(LastGoogleDriveBackupText));
            });
        };

        SaveSettingsCommand = new AsyncRelayCommand(SaveAllSettingsAsync);

        AddExcludedAppCommand = new RelayCommand(AddExcludedApp);
        RemoveExcludedAppCommand = new RelayCommand(param =>
        {
            if (param is string app) RemoveExcludedApp(app);
        });

        AddCategoryCommand = new AsyncRelayCommand(AddCategoryAsync);
        DeleteCategoryCommand = new AsyncRelayCommand(async param =>
        {
            if (param is Category cat) await DeleteCategoryAsync(cat);
        });

        AddRuleCommand = new AsyncRelayCommand(AddRuleAsync);
        DeleteRuleCommand = new AsyncRelayCommand(async param =>
        {
            if (param is CategoryRule rule) await DeleteRuleAsync(rule);
        });

        TestTelegramConnectionCommand = new AsyncRelayCommand(TestTelegramConnectionAsync);
        SendTestTelegramMessageCommand = new AsyncRelayCommand(SendTestTelegramMessageAsync);
        ResetTelegramTemplateCommand = new RelayCommand(() => TelegramMessageTemplate = AppSettings.DefaultTelegramTemplate);
        SendTestEmailCommand = new AsyncRelayCommand(SendTestEmailAsync);

        BackupDatabaseCommand = new AsyncRelayCommand(BackupDatabaseAsync);
        RestoreDatabaseCommand = new AsyncRelayCommand(RestoreDatabaseAsync);
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync);
        ConnectGoogleDriveCommand = new AsyncRelayCommand(ConnectGoogleDriveAsync);
        DisconnectGoogleDriveCommand = new AsyncRelayCommand(DisconnectGoogleDriveAsync);
        BackupToGoogleDriveNowCommand = new AsyncRelayCommand(BackupToGoogleDriveNowAsync);

        RefreshLogsCommand = new AsyncRelayCommand(RefreshLogsAsync);
        RetryFailedDeliveriesCommand = new AsyncRelayCommand(RetryFailedDeliveriesAsync);
    }

    public async Task InitializeAsync()
    {
        _settings = await _databaseService.LoadSettingsAsync();
        OnPropertyChanged(string.Empty); // Refresh all properties

        ExcludedApps.Clear();
        foreach (var app in _settings.ExcludedApps) ExcludedApps.Add(app);

        CapturedApps.Clear();
        var distinctApps = await _databaseService.GetDistinctAppNamesAsync();
        foreach (var a in distinctApps) CapturedApps.Add(a);

        RebuildRecordedSourcesList();

        await LoadCategoriesAndRulesAsync();
        await RefreshLogsAsync();
    }

    public void RebuildRecordedSourcesList()
    {
        var allSources = CapturedApps
            .Concat(ExcludedApps)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        RecordedSources.Clear();
        foreach (var app in allSources)
        {
            bool isExcluded = ExcludedApps.Contains(app, StringComparer.OrdinalIgnoreCase);
            RecordedSources.Add(new RecordedSourceToggle(app, !isExcluded, OnSourceRecordingToggled));
        }
    }

    private async void OnSourceRecordingToggled(RecordedSourceToggle toggle)
    {
        try
        {
            if (toggle.IsRecording)
            {
                var existing = ExcludedApps.FirstOrDefault(e => e.Equals(toggle.AppName, StringComparison.OrdinalIgnoreCase));
                if (existing != null) ExcludedApps.Remove(existing);
            }
            else
            {
                if (!ExcludedApps.Contains(toggle.AppName, StringComparer.OrdinalIgnoreCase))
                    ExcludedApps.Add(toggle.AppName);
            }

            _settings.ExcludedApps = ExcludedApps.ToList();
            await _databaseService.SaveSettingsAsync(_settings);
            SourceToggleStatus = toggle.IsRecording
                ? $"✅ Resumed recording notifications from '{toggle.AppName}'."
                : $"🚫 Stopped recording new notifications from '{toggle.AppName}' (past history kept).";
        }
        catch (Exception ex)
        {
            SourceToggleStatus = $"Could not save source preference: {ex.Message}";
        }
    }

    public async Task SaveAllSettingsAsync()
    {
        _settings.ExcludedApps = ExcludedApps.ToList();
        await _databaseService.SaveSettingsAsync(_settings);
        DbStatusMessage = "Settings saved successfully at " + DateTime.Now.ToString("HH:mm:ss");
        WpfMessageBox.Show("Settings saved successfully.", "Notify Hub", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void AddExcludedApp()
    {
        if (string.IsNullOrWhiteSpace(NewExcludedApp)) return;
        string trimmed = NewExcludedApp.Trim();
        if (!ExcludedApps.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            ExcludedApps.Add(trimmed);
            NewExcludedApp = string.Empty;
            _settings.ExcludedApps = ExcludedApps.ToList();
            await _databaseService.SaveSettingsAsync(_settings);
            RebuildRecordedSourcesList();
            SourceToggleStatus = $"🚫 Stopped recording new notifications from '{trimmed}'.";
        }
    }

    private async void RemoveExcludedApp(string app)
    {
        if (ExcludedApps.Remove(app))
        {
            _settings.ExcludedApps = ExcludedApps.ToList();
            await _databaseService.SaveSettingsAsync(_settings);
            RebuildRecordedSourcesList();
            SourceToggleStatus = $"✅ Resumed recording notifications from '{app}'.";
        }
    }

    private async Task LoadCategoriesAndRulesAsync()
    {
        Categories.Clear();
        var cats = await _databaseService.GetCategoriesAsync();
        foreach (var c in cats) Categories.Add(c);

        Rules.Clear();
        var rules = await _databaseService.GetRulesAsync();
        foreach (var r in rules) Rules.Add(r);
    }

    private async Task AddCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) return;
        var cat = new Category
        {
            Name = NewCategoryName.Trim(),
            Description = NewCategoryDesc.Trim(),
            IconGlyph = "📁",
            ColorHex = "#4A90E2",
            IsSystem = false
        };

        int id = await _databaseService.AddCategoryAsync(cat);
        if (id > 0)
        {
            cat.Id = id;
            Categories.Add(cat);
            NewCategoryName = string.Empty;
            NewCategoryDesc = string.Empty;
        }
    }

    private async Task DeleteCategoryAsync(Category cat)
    {
        if (cat.IsSystem)
        {
            WpfMessageBox.Show("System categories cannot be deleted.", "Notify Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (await _databaseService.DeleteCategoryAsync(cat.Id))
        {
            Categories.Remove(cat);
        }
    }

    private async Task AddRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRulePattern)) return;
        var rule = new CategoryRule
        {
            CategoryName = NewRuleCategory,
            MatchType = NewRuleType,
            Pattern = NewRulePattern.Trim(),
            Priority = 5,
            IsActive = true
        };

        int id = await _databaseService.AddRuleAsync(rule);
        if (id > 0)
        {
            rule.Id = id;
            Rules.Add(rule);
            NewRulePattern = string.Empty;
        }
    }

    private async Task DeleteRuleAsync(CategoryRule rule)
    {
        if (await _databaseService.DeleteRuleAsync(rule.Id))
        {
            Rules.Remove(rule);
        }
    }

    private async Task TestTelegramConnectionAsync()
    {
        TelegramTestStatus = "Testing connection...";
        bool ok = await _telegramService.TestConnectionAsync(TelegramBotToken);
        TelegramTestStatus = ok ? "✅ Connection Successful!" : "❌ Connection Failed. Check token.";
    }

    private async Task SendTestTelegramMessageAsync()
    {
        TelegramTestStatus = "Sending test message...";
        bool ok = await _telegramService.SendTestMessageAsync(TelegramBotToken, TelegramChatId, TelegramMessageTemplate);
        TelegramTestStatus = ok ? "✅ Test message delivered!" : "❌ Delivery Failed. Check Chat ID.";
    }

    private async Task SendTestEmailAsync()
    {
        EmailTestStatus = "Sending test email...";
        bool ok = await _emailService.SendTestEmailAsync(_settings);
        EmailTestStatus = ok ? "✅ Test email sent successfully!" : "❌ Delivery Failed. Check SMTP host/credentials.";
    }

    private async Task BackupDatabaseAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "SQLite Database Backup (*.db)|*.db",
            FileName = $"NotifyHub_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.db"
        };

        if (dlg.ShowDialog() == true)
        {
            bool success = await _databaseService.BackupDatabaseAsync(dlg.FileName);
            DbStatusMessage = success ? $"Backup saved to {dlg.FileName}" : "Backup failed.";
        }
    }

    private async Task RestoreDatabaseAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "SQLite Database Backup (*.db)|*.db"
        };

        if (dlg.ShowDialog() == true)
        {
            if (WpfMessageBox.Show("Restoring will replace the current database. Proceed?", "Confirm Restore",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                bool success = await _databaseService.RestoreDatabaseAsync(dlg.FileName);
                DbStatusMessage = success ? "Database restored successfully." : "Restore failed.";
                if (success) await InitializeAsync();
            }
        }
    }

    private async Task ClearHistoryAsync()
    {
        if (WpfMessageBox.Show("Are you sure you want to delete ALL captured notification history? This cannot be undone.",
            "Confirm Delete All", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await _databaseService.DeleteAllHistoryAsync();
            DbStatusMessage = "All history deleted.";
            await RefreshLogsAsync();
        }
    }

    private async Task ConnectGoogleDriveAsync()
    {
        GoogleDriveStatusMessage = "Opening Google sign-in in your browser...";
        var (success, message) = await _googleDriveService.ConnectAsync(GoogleDriveClientId, GoogleDriveClientSecret);
        GoogleDriveStatusMessage = message;
        if (success)
        {
            await _databaseService.SaveSettingsAsync(_settings);
        }
        OnPropertyChanged(nameof(IsGoogleDriveConnected));
        OnPropertyChanged(nameof(GoogleDriveConnectionText));
    }

    private async Task DisconnectGoogleDriveAsync()
    {
        if (WpfMessageBox.Show("Disconnect Google Drive and remove saved OAuth tokens from this device?",
            "Disconnect Google Drive", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await _googleDriveService.DisconnectAsync();
            GoogleDriveBackupEnabled = false;
            await _databaseService.SaveSettingsAsync(_settings);
            GoogleDriveStatusMessage = "Disconnected from Google Drive.";
            OnPropertyChanged(nameof(IsGoogleDriveConnected));
            OnPropertyChanged(nameof(GoogleDriveConnectionText));
        }
    }

    private async Task BackupToGoogleDriveNowAsync()
    {
        GoogleDriveStatusMessage = "Creating snapshot and uploading to Google Drive...";
        await _databaseService.SaveSettingsAsync(_settings);
        var (success, message) = await _googleDriveService.BackupNowAsync();
        GoogleDriveStatusMessage = message;
        if (success)
        {
            _settings = await _databaseService.LoadSettingsAsync();
            OnPropertyChanged(nameof(LastGoogleDriveBackupText));
        }
    }

    public async Task RefreshLogsAsync()
    {
        ForwardingLogs.Clear();
        var logs = await _databaseService.GetForwardingLogsAsync(150);
        foreach (var l in logs) ForwardingLogs.Add(l);
    }

    private async Task RetryFailedDeliveriesAsync()
    {
        var failedLogs = await _databaseService.GetFailedForwardingLogsAsync();
        if (failedLogs.Count == 0)
        {
            WpfMessageBox.Show("No failed deliveries to retry.", "Notify Hub", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int retriedCount = 0;
        foreach (var log in failedLogs)
        {
            var notif = await _databaseService.GetNotificationByIdAsync(log.NotificationId);
            if (notif == null) continue;

            if (log.DestinationType == DestinationType.Telegram && TelegramEnabled)
            {
                bool success = await _telegramService.ForwardNotificationAsync(notif, TelegramBotToken, TelegramChatId, TelegramMessageTemplate);
                if (success) retriedCount++;
            }
            else if (log.DestinationType == DestinationType.Email && EmailEnabled)
            {
                bool success = await _emailService.ForwardNotificationAsync(notif, _settings);
                if (success) retriedCount++;
            }
        }

        await RefreshLogsAsync();
        WpfMessageBox.Show($"Retried {failedLogs.Count} delivery attempts. {retriedCount} succeeded.", "Retry Completed", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
