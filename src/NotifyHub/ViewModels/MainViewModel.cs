using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WpfMessageBox = System.Windows.MessageBox;
using NotifyHub.Models;
using NotifyHub.Services;

namespace NotifyHub.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IDatabaseService _databaseService;
    private readonly INotificationListenerService _listenerService;
    private readonly IExportService _exportService;

    private SettingsViewModel? _settingsVm;
    public SettingsViewModel? SettingsVm
    {
        get => _settingsVm;
        set => SetProperty(ref _settingsVm, value);
    }

    private KpiSummary _kpiSummary = new();
    public KpiSummary KpiSummary
    {
        get => _kpiSummary;
        set => SetProperty(ref _kpiSummary, value);
    }

    // Main Table & Filter Collections
    public ObservableCollection<NotificationItem> Notifications { get; } = new();
    public ObservableCollection<string> AppFilterList { get; } = new();
    public ObservableCollection<string> CategoryFilterList { get; } = new();
    public List<string> StatusFilterList { get; } = new() { "All", "Pending", "In Progress", "Done", "Archived" };
    public List<string> PriorityFilterList { get; } = new() { "All", "Urgent", "High", "Normal", "Low" };
    public List<string> DateFilterList { get; } = new() { "All Time", "Today", "Yesterday", "Last 7 Days", "Last 30 Days" };

    // Advanced Analytics Chart Collections
    public ObservableCollection<SourceChartItem> SourceChartItems { get; } = new();
    public ObservableCollection<TimelineChartItem> TimelineChartItems { get; } = new();

    private int _timelineDays = 7;
    public int TimelineDays
    {
        get => _timelineDays;
        set
        {
            if (SetProperty(ref _timelineDays, value))
            {
                OnPropertyChanged(nameof(TimelineTitle));
                _ = RefreshAnalyticsAsync();
            }
        }
    }

    public string TimelineTitle => $"ACTIVITY TIMELINE (LAST {TimelineDays} DAYS)";

    // Filter Properties
    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) _ = RefreshNotificationsAsync(); }
    }

    private string _selectedApp = "All";
    public string SelectedApp
    {
        get => _selectedApp;
        set { if (SetProperty(ref _selectedApp, value)) _ = RefreshNotificationsAsync(); }
    }

    private string _selectedCategory = "All";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) _ = RefreshNotificationsAsync(); }
    }

    private string _selectedStatus = "All";
    public string SelectedStatus
    {
        get => _selectedStatus;
        set { if (SetProperty(ref _selectedStatus, value)) _ = RefreshNotificationsAsync(); }
    }

    private string _selectedPriority = "All";
    public string SelectedPriority
    {
        get => _selectedPriority;
        set { if (SetProperty(ref _selectedPriority, value)) _ = RefreshNotificationsAsync(); }
    }

    private string _selectedDateFilter = "All Time";
    public string SelectedDateFilter
    {
        get => _selectedDateFilter;
        set { if (SetProperty(ref _selectedDateFilter, value)) _ = RefreshNotificationsAsync(); }
    }

    private bool _sortDescending = true;
    public bool SortDescending
    {
        get => _sortDescending;
        set { if (SetProperty(ref _sortDescending, value)) _ = RefreshNotificationsAsync(); }
    }

    // Selected Notification & Details Panel
    private NotificationItem? _selectedNotification;
    public NotificationItem? SelectedNotification
    {
        get => _selectedNotification;
        set
        {
            if (SetProperty(ref _selectedNotification, value))
            {
                IsDetailsOpen = _selectedNotification != null;
                _ = LoadSelectedDetailsAsync();
            }
        }
    }

    private bool _isDetailsOpen;
    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        set => SetProperty(ref _isDetailsOpen, value);
    }

    private string _editingNotes = string.Empty;
    public string EditingNotes
    {
        get => _editingNotes;
        set => SetProperty(ref _editingNotes, value);
    }

    public ObservableCollection<ForwardingLog> NotificationForwardingLogs { get; } = new();

    private string _monitoringStatusText = "Active";
    public string MonitoringStatusText
    {
        get => _monitoringStatusText;
        set => SetProperty(ref _monitoringStatusText, value);
    }

    public bool IsPaused => _listenerService.IsPaused;
    public bool IsPermissionNeeded => _listenerService.AccessStatus != "Allowed";

    private int _selectedCount;
    public int SelectedCount
    {
        get => _selectedCount;
        set => SetProperty(ref _selectedCount, value);
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand SetTimeline7DaysCommand { get; }
    public ICommand SetTimeline30DaysCommand { get; }
    public ICommand MarkAsDoneCommand { get; }
    public ICommand MarkAsInProgressCommand { get; }
    public ICommand MarkAsArchivedCommand { get; }
    public ICommand DeleteNotificationCommand { get; }
    public ICommand BulkMarkDoneCommand { get; }
    public ICommand BulkArchiveCommand { get; }
    public ICommand BulkDeleteCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand TogglePauseMonitoringCommand { get; }
    public ICommand TriggerManualSyncCommand { get; }
    public ICommand SaveNotesCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand OpenSettingsPermissionCommand { get; }
    public ICommand OpenSourceAppCommand { get; }
    public ICommand CopyNotificationFullCommand { get; }
    public ICommand CopyNotificationBodyCommand { get; }
    public ICommand MuteSourceCommand { get; }

    public MainViewModel(
        IDatabaseService databaseService,
        INotificationListenerService listenerService,
        IExportService exportService)
    {
        _databaseService = databaseService;
        _listenerService = listenerService;
        _exportService = exportService;

        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync);
        SetTimeline7DaysCommand = new RelayCommand(() => TimelineDays = 7);
        SetTimeline30DaysCommand = new RelayCommand(() => TimelineDays = 30);

        MarkAsDoneCommand = new AsyncRelayCommand(async n => { if (n is NotificationItem item) await SetStatusAsync(item, NotificationStatus.Done); });
        MarkAsInProgressCommand = new AsyncRelayCommand(async n => { if (n is NotificationItem item) await SetStatusAsync(item, NotificationStatus.InProgress); });
        MarkAsArchivedCommand = new AsyncRelayCommand(async n => { if (n is NotificationItem item) await SetStatusAsync(item, NotificationStatus.Archived); });
        DeleteNotificationCommand = new AsyncRelayCommand(async n => { if (n is NotificationItem item) await DeleteNotificationAsync(item); });

        BulkMarkDoneCommand = new AsyncRelayCommand(() => BulkSetStatusAsync(NotificationStatus.Done));
        BulkArchiveCommand = new AsyncRelayCommand(() => BulkSetStatusAsync(NotificationStatus.Archived));
        BulkDeleteCommand = new AsyncRelayCommand(BulkDeleteAsync);
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
        DeselectAllCommand = new RelayCommand(() => SetAllSelected(false));

        TogglePauseMonitoringCommand = new RelayCommand(TogglePauseMonitoring);
        TriggerManualSyncCommand = new AsyncRelayCommand(async () => await _listenerService.TriggerManualSyncAsync());
        SaveNotesCommand = new AsyncRelayCommand(SaveNotesAsync);
        CloseDetailsCommand = new RelayCommand(() => IsDetailsOpen = false);

        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync);
        ExportExcelCommand = new AsyncRelayCommand(ExportExcelAsync);
        OpenSettingsPermissionCommand = new RelayCommand(() => _listenerService.OpenNotificationSettings());

        OpenSourceAppCommand = new RelayCommand(param =>
        {
            var item = param as NotificationItem ?? SelectedNotification;
            if (item != null && !AppActivationService.TryNavigateToNotification(item, _databaseService))
            {
                WpfMessageBox.Show(
                    $"Could not open or navigate to the message in '{item.AppName}'.\n\nPlease ensure the application is installed or running.",
                    "Open Source Application", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        });

        CopyNotificationFullCommand = new RelayCommand(param =>
        {
            var item = param as NotificationItem ?? SelectedNotification;
            if (item != null)
            {
                CopyToClipboardSafe($"[{item.AppName}] {item.Title}\n{item.Body}");
            }
        });

        CopyNotificationBodyCommand = new RelayCommand(param =>
        {
            var item = param as NotificationItem ?? SelectedNotification;
            if (item != null)
            {
                CopyToClipboardSafe(item.Body);
            }
        });

        MuteSourceCommand = new AsyncRelayCommand(async param =>
        {
            var item = param as NotificationItem ?? SelectedNotification;
            if (item != null) await StopRecordingSourceAsync(item.AppName);
        });

        _listenerService.NotificationCaptured += ListenerService_NotificationCaptured;
        _listenerService.MonitoringStatusChanged += ListenerService_MonitoringStatusChanged;
    }

    public async Task InitializeAsync()
    {
        await LoadFilterOptionsAsync();
        await RefreshAllAsync();
    }

    private async void ListenerService_NotificationCaptured(object? sender, NotificationItem e)
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            e.PropertyChanged += NotificationItem_PropertyChanged;
            if (SortDescending) Notifications.Insert(0, e);
            else Notifications.Add(e);

            if (!AppFilterList.Contains(e.AppName))
            {
                AppFilterList.Add(e.AppName);
            }

            await RefreshKpiAsync();
            await RefreshAnalyticsAsync();
        });
    }

    private void ListenerService_MonitoringStatusChanged(object? sender, string status)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            MonitoringStatusText = status;
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(IsPermissionNeeded));
        });
    }

    public async Task RefreshAllAsync()
    {
        await RefreshKpiAsync();
        await RefreshNotificationsAsync();
        await RefreshAnalyticsAsync();
    }

    public async Task RefreshKpiAsync()
    {
        KpiSummary = await _databaseService.GetKpiSummaryAsync();
    }

    public async Task RefreshAnalyticsAsync()
    {
        var sources = await _databaseService.GetSourceDistributionAsync();
        SourceChartItems.Clear();
        foreach (var s in sources) SourceChartItems.Add(s);

        var timeline = await _databaseService.GetActivityTimelineAsync(TimelineDays);
        TimelineChartItems.Clear();
        foreach (var t in timeline) TimelineChartItems.Add(t);
    }

    public async Task RefreshNotificationsAsync()
    {
        var filter = BuildCurrentFilter();
        var items = await _databaseService.GetNotificationsAsync(filter, 250, 0);

        Notifications.Clear();
        foreach (var item in items)
        {
            item.PropertyChanged += NotificationItem_PropertyChanged;
            Notifications.Add(item);
        }

        UpdateSelectedCount();
    }

    private void NotificationItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationItem.IsSelected))
        {
            UpdateSelectedCount();
        }
    }

    private void UpdateSelectedCount() => SelectedCount = Notifications.Count(n => n.IsSelected);

    private async Task LoadFilterOptionsAsync()
    {
        AppFilterList.Clear();
        AppFilterList.Add("All");
        foreach (var app in await _databaseService.GetDistinctAppNamesAsync())
            AppFilterList.Add(app);

        CategoryFilterList.Clear();
        CategoryFilterList.Add("All");
        foreach (var cat in await _databaseService.GetCategoriesAsync())
            CategoryFilterList.Add(cat.Name);
    }

    private NotificationFilter BuildCurrentFilter()
    {
        var filter = new NotificationFilter
        {
            SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            AppName = SelectedApp == "All" ? null : SelectedApp,
            Category = SelectedCategory == "All" ? null : SelectedCategory,
            SortDescending = SortDescending
        };

        if (SelectedStatus != "All" && Enum.TryParse<NotificationStatus>(SelectedStatus.Replace(" ", ""), out var status))
            filter.Status = status;

        if (SelectedPriority != "All" && Enum.TryParse<NotificationPriority>(SelectedPriority, out var priority))
            filter.Priority = priority;

        switch (SelectedDateFilter)
        {
            case "Today": filter.FromDate = DateTime.Today; break;
            case "Yesterday": filter.FromDate = DateTime.Today.AddDays(-1); filter.ToDate = DateTime.Today; break;
            case "Last 7 Days": filter.FromDate = DateTime.Today.AddDays(-7); break;
            case "Last 30 Days": filter.FromDate = DateTime.Today.AddDays(-30); break;
        }

        return filter;
    }

    public async Task SetStatusAsync(NotificationItem item, NotificationStatus status)
    {
        item.Status = status;
        item.LastUpdatedAt = DateTime.Now;
        await _databaseService.UpdateNotificationStatusAsync(item.Id, status);
        await RefreshKpiAsync();
        await RefreshAnalyticsAsync();
    }

    private async Task DeleteNotificationAsync(NotificationItem item)
    {
        await _databaseService.DeleteNotificationAsync(item.Id);
        Notifications.Remove(item);
        if (SelectedNotification?.Id == item.Id)
        {
            SelectedNotification = null;
            IsDetailsOpen = false;
        }
        await RefreshKpiAsync();
        await RefreshAnalyticsAsync();
    }

    private async Task BulkSetStatusAsync(NotificationStatus status)
    {
        var selected = Notifications.Where(n => n.IsSelected).ToList();
        if (selected.Count == 0) return;

        await _databaseService.BulkUpdateStatusAsync(selected.Select(s => s.Id), status);
        foreach (var item in selected)
        {
            item.Status = status;
            item.IsSelected = false;
        }

        await RefreshKpiAsync();
        await RefreshAnalyticsAsync();
        UpdateSelectedCount();
    }

    private async Task BulkDeleteAsync()
    {
        var selected = Notifications.Where(n => n.IsSelected).ToList();
        if (selected.Count == 0) return;

        if (WpfMessageBox.Show($"Are you sure you want to permanently delete {selected.Count} notification(s)?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await _databaseService.BulkDeleteAsync(selected.Select(s => s.Id));
        foreach (var item in selected) Notifications.Remove(item);

        if (SelectedNotification != null && selected.Any(s => s.Id == SelectedNotification.Id))
        {
            SelectedNotification = null;
            IsDetailsOpen = false;
        }

        await RefreshKpiAsync();
        await RefreshAnalyticsAsync();
        UpdateSelectedCount();
    }

    private void SetAllSelected(bool isSelected)
    {
        foreach (var n in Notifications) n.IsSelected = isSelected;
        UpdateSelectedCount();
    }

    private void TogglePauseMonitoring()
    {
        if (_listenerService.IsPaused) _listenerService.Resume();
        else _listenerService.Pause();
        OnPropertyChanged(nameof(IsPaused));
    }

    private async Task LoadSelectedDetailsAsync()
    {
        NotificationForwardingLogs.Clear();
        if (SelectedNotification == null) return;

        EditingNotes = SelectedNotification.UserNotes ?? string.Empty;
        foreach (var log in await _databaseService.GetForwardingLogsAsync(20, SelectedNotification.Id))
        {
            NotificationForwardingLogs.Add(log);
        }
    }

    private async Task SaveNotesAsync()
    {
        if (SelectedNotification == null) return;

        SelectedNotification.UserNotes = EditingNotes;
        await _databaseService.UpdateNotificationNotesAsync(SelectedNotification.Id, EditingNotes);
        WpfMessageBox.Show("Notes saved successfully.", "Notify Hub", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task ExportCsvAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv",
            FileName = $"NotifyHub_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dlg.ShowDialog() == true)
        {
            await _exportService.ExportToCsvAsync(Notifications, dlg.FileName);
            WpfMessageBox.Show($"Successfully exported {Notifications.Count} notifications to CSV.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public async Task StopRecordingSourceAsync(string appName)
    {
        string cleanName = (appName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cleanName)) return;

        var settings = await _databaseService.LoadSettingsAsync();
        if (!settings.ExcludedApps.Contains(cleanName, StringComparer.OrdinalIgnoreCase))
        {
            settings.ExcludedApps.Add(cleanName);
            await _databaseService.SaveSettingsAsync(settings);
        }

        if (SettingsVm != null)
        {
            await SettingsVm.InitializeAsync();
        }

        WpfMessageBox.Show(
            $"Stopped recording new notifications from '{cleanName}'.\n\nExisting past notifications are kept in your history. You can re-check '{cleanName}' anytime under Settings → Monitoring & Apps to resume recording.",
            "Source Recording Stopped", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void CopyToClipboardSafe(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { System.Windows.Clipboard.SetText(text); } catch { }
    }

    private async Task ExportExcelAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel XML (*.xml)|*.xml|All Files (*.*)|*.*",
            FileName = $"NotifyHub_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xml"
        };

        if (dlg.ShowDialog() == true)
        {
            await _exportService.ExportToExcelXmlAsync(Notifications, dlg.FileName);
            WpfMessageBox.Show($"Successfully exported {Notifications.Count} notifications to Excel format.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
