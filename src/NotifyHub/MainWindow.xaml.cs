using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using NotifyHub.Services;
using NotifyHub.ViewModels;

namespace NotifyHub;

public partial class MainWindow : Window
{
    private readonly ISecureStorageService _secureStorage;
    private readonly IDatabaseService _databaseService;
    private readonly ICategorizationService _categorizationService;
    private readonly ITelegramForwardingService _telegramService;
    private readonly IEmailForwardingService _emailService;
    private readonly IStartupManager _startupManager;
    private readonly IExportService _exportService;
    private readonly IGoogleDriveBackupService _googleDriveService;
    private readonly INotificationListenerService _listenerService;

    public MainViewModel MainVm { get; }
    public SettingsViewModel SettingsVm { get; }

    private NotifyIcon? _trayIcon;
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();

        _secureStorage = new WindowsCredentialManagerService();
        _databaseService = new SqliteDatabaseService(_secureStorage);
        _categorizationService = new SmartCategorizationService(_databaseService);
        _telegramService = new TelegramForwardingService(_databaseService);
        _emailService = new EmailForwardingService(_databaseService);
        _startupManager = new WindowsStartupService();
        _exportService = new ExportService();
        _googleDriveService = new GoogleDriveBackupService(_databaseService, _secureStorage);

        _listenerService = new WindowsNotificationListenerService(
            _databaseService,
            _categorizationService,
            _telegramService,
            _emailService);

        MainVm = new MainViewModel(_databaseService, _listenerService, _exportService);
        SettingsVm = new SettingsViewModel(
            _databaseService,
            _listenerService,
            _telegramService,
            _emailService,
            _startupManager,
            _googleDriveService);
        MainVm.SettingsVm = SettingsVm;

        DataContext = MainVm;

        InitializeSystemTray();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _databaseService.InitializeDatabaseAsync();
            await _categorizationService.ReloadRulesAsync();
            await MainVm.InitializeAsync();
            await SettingsVm.InitializeAsync();
            App.ApplyTheme(SettingsVm.Theme);

            var settings = await _databaseService.LoadSettingsAsync();
            if (settings.MonitoringEnabled)
            {
                await _listenerService.StartAsync();
            }

            _googleDriveService.StartScheduler();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Initialization warning: {ex.Message}", "Notify Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InitializeSystemTray()
    {
        Icon trayAppIcon = LoadAppIcon();
        _trayIcon = new NotifyIcon
        {
            Text = "Notify Hub — Active Monitoring",
            Visible = true,
            Icon = trayAppIcon
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(new ToolStripMenuItem("Open Dashboard", null, (s, e) => { ShowAndActivate(); ShowMainTableView(); }));
        contextMenu.Items.Add(new ToolStripMenuItem("Analytics Charts", null, (s, e) => { ShowAndActivate(); ShowAnalyticsView(); }));
        contextMenu.Items.Add(new ToolStripMenuItem("Pause / Resume Monitoring", null, (s, e) => MainVm.TogglePauseMonitoringCommand.Execute(null)));
        contextMenu.Items.Add(new ToolStripMenuItem("Settings", null, (s, e) => { ShowAndActivate(); ShowSettingsView(); }));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("Exit Application", null, (s, e) =>
        {
            _isExplicitExit = true;
            _googleDriveService.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            System.Windows.Application.Current.Shutdown();
        }));

        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (s, e) => ShowAndActivate();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var resourceInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/notifyhub.ico"));
            if (resourceInfo?.Stream != null)
            {
                using var stream = resourceInfo.Stream;
                return new Icon(stream);
            }
        }
        catch { }

        return SystemIcons.Information;
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // Pressing Close (X) hides to System Tray when enabled; Minimize (_) minimizes normally to taskbar.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit && SettingsVm.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _trayIcon?.ShowBalloonTip(2000, "Notify Hub", "Running in the system tray. Double-click the icon to reopen.", ToolTipIcon.Info);
        }
        else
        {
            _googleDriveService.Dispose();
            _trayIcon?.Dispose();
            base.OnClosing(e);
        }
    }

    private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && !row.IsSelected)
        {
            row.IsSelected = true;
        }
    }

    private void BtnAnalyticsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (BorderAnalyticsView.Visibility == Visibility.Visible)
            ShowMainTableView();
        else
            ShowAnalyticsView();
    }

    private void BtnSettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (BorderSettingsView.Visibility == Visibility.Visible)
            ShowMainTableView();
        else
            ShowSettingsView();
    }

    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e) => ShowMainTableView();

    private void ShowMainTableView()
    {
        BorderSettingsView.Visibility = Visibility.Collapsed;
        BorderAnalyticsView.Visibility = Visibility.Collapsed;
        GridDashboardView.Visibility = Visibility.Visible;
        BtnAnalyticsToggle.Content = "📊 Analytics";
        BtnSettingsToggle.Content = "⚙️ Settings";
        _ = MainVm.RefreshAllAsync();
    }

    private void ShowAnalyticsView()
    {
        GridDashboardView.Visibility = Visibility.Collapsed;
        BorderSettingsView.Visibility = Visibility.Collapsed;
        BorderAnalyticsView.Visibility = Visibility.Visible;
        BtnAnalyticsToggle.Content = "← Notifications";
        BtnSettingsToggle.Content = "⚙️ Settings";
        _ = MainVm.RefreshAnalyticsAsync();
    }

    private void ShowSettingsView()
    {
        GridDashboardView.Visibility = Visibility.Collapsed;
        BorderAnalyticsView.Visibility = Visibility.Collapsed;
        BorderSettingsView.Visibility = Visibility.Visible;
        BtnAnalyticsToggle.Content = "📊 Analytics";
        BtnSettingsToggle.Content = "← Notifications";
        _ = SettingsVm.InitializeAsync();
    }

    private void SettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (SubpanelGeneral == null) return;

        SubpanelGeneral.Visibility = Visibility.Collapsed;
        SubpanelMonitoring.Visibility = Visibility.Collapsed;
        SubpanelCategories.Visibility = Visibility.Collapsed;
        SubpanelTelegram.Visibility = Visibility.Collapsed;
        SubpanelEmail.Visibility = Visibility.Collapsed;
        SubpanelData.Visibility = Visibility.Collapsed;
        SubpanelLogs.Visibility = Visibility.Collapsed;

        if (TabGeneral.IsChecked == true)
        {
            TxtSettingsTitle.Text = "General Preferences";
            SubpanelGeneral.Visibility = Visibility.Visible;
        }
        else if (TabMonitoring.IsChecked == true)
        {
            TxtSettingsTitle.Text = "Notification Monitoring Diagnostics";
            SubpanelMonitoring.Visibility = Visibility.Visible;
        }
        else if (TabCategories.IsChecked == true)
        {
            TxtSettingsTitle.Text = "Categories & Categorization Rules";
            SubpanelCategories.Visibility = Visibility.Visible;
        }
        else if (TabTelegram.IsChecked == true)
        {
            TxtSettingsTitle.Text = "Instant Telegram Forwarding Integration";
            SubpanelTelegram.Visibility = Visibility.Visible;
        }
        else if (TabEmail.IsChecked == true)
        {
            TxtSettingsTitle.Text = "Instant Email Forwarding Integration";
            SubpanelEmail.Visibility = Visibility.Visible;
        }
        else if (TabData.IsChecked == true)
        {
            TxtSettingsTitle.Text = "Local SQLite Storage & Google Drive Backups";
            SubpanelData.Visibility = Visibility.Visible;
        }
        else if (TabLogs.IsChecked == true)
        {
            TxtSettingsTitle.Text = "Forwarding Delivery Logs";
            SubpanelLogs.Visibility = Visibility.Visible;
            _ = SettingsVm.RefreshLogsAsync();
        }
    }
}
