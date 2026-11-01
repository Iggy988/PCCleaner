using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Services;
using PCCleaner.Services.Cleaning;
using PCCleaner.Services.Localization;
using PCCleaner.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace PCCleaner.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly SystemInfoService _info;
    private readonly SystemActions _actions;
    private readonly SettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly DispatcherTimer _timer;
    private Func<string>? _boostStatus;
    private Func<string>? _feedbackMessage;

    public DashboardViewModel(SystemInfoService info, SystemActions actions, SettingsService settings,
        CleanerViewModel cleaner, TempCleanerViewModel tempCleaner, INavigationService navigation, ISnackbarService snackbar,
        IContentDialogService dialogs)
    {
        _info = info;
        _actions = actions;
        _settings = settings;
        _navigation = navigation;
        _snackbar = snackbar;
        _dialogs = dialogs;
        Cleaner = cleaner;
        TempCleaner = tempCleaner;
        Cleaner.CleaningCompleted += (_, _) => UpdateStats();
        TempCleaner.CleaningCompleted += (_, _) => UpdateStats();
        Cleaner.PropertyChanged += OnCleanerChanged;
        TempCleaner.PropertyChanged += OnCleanerChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _timer.Tick += (_, _) => RefreshMetrics();

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(string.Empty);
            RefreshMetrics();
            UpdateStats();
        };
        UpdateStats();
    }

    public CleanerViewModel Cleaner { get; }

    public TempCleanerViewModel TempCleaner { get; }

    public string Greeting => Loc.T(DateTime.Now.Hour switch
    {
        < 12 => "Dashboard_GoodMorning",
        < 18 => "Dashboard_GoodAfternoon",
        _ => "Dashboard_GoodEvening",
    });

    public string MachineName => _info.MachineName;

    public string OsName => _info.OsName;

    public string CpuName => _info.CpuName;

    public string BoostStatus => _boostStatus?.Invoke() ?? Loc.T("Dashboard_BoostDefault");

    /// <summary>
    /// Junk found by the last scans: Temp Files selection + Cleaner selection, minus the Cleaner categories that cover
    /// the same temp folders (so nothing is counted twice).
    /// </summary>
    public long JunkFoundBytes =>
        TempCleaner.SelectedBytes +
        Cleaner.Items
            .Where(i => i.IsChecked && i.Status == CleanupStatus.Scanned && !CleanupCatalog.TempLocationIds.Contains(i.Provider.Id))
            .Sum(i => i.FoundBytes);

    public bool HasScanned => Cleaner.HasScanned || TempCleaner.HasScanned;

    public bool IsScanning => Cleaner.IsScanning || TempCleaner.IsScanning;

    [ObservableProperty]
    public partial string UptimeText { get; set; } = "";

    [ObservableProperty]
    public partial double CpuPercent { get; set; }

    [ObservableProperty]
    public partial double RamPercent { get; set; }

    [ObservableProperty]
    public partial string RamText { get; set; } = "";

    [ObservableProperty]
    public partial double DiskPercent { get; set; }

    [ObservableProperty]
    public partial string DiskText { get; set; } = "";

    [ObservableProperty]
    public partial string DiskLabel { get; set; } = "";

    [ObservableProperty]
    public partial string LastCleanedText { get; set; } = "";

    [ObservableProperty]
    public partial long TotalFreedBytes { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BoostCommand), nameof(ScanJunkCommand))]
    public partial bool IsBoosting { get; set; }

    [ObservableProperty]
    public partial bool ShowFeedback { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial string? FeedbackTitle { get; set; }

    public string? FeedbackMessage => _feedbackMessage?.Invoke();

    public void Activate()
    {
        OnPropertyChanged(nameof(Greeting));
        RefreshMetrics();
        _timer.Start();
    }

    public void Deactivate() => _timer.Stop();

    [RelayCommand(CanExecute = nameof(CanBoost))]
    private async Task BoostAsync()
    {
        IsBoosting = true;
        ShowFeedback = false;
        try
        {
            var selected = Cleaner.Items.Where(i => i.IsChecked && i.IsAvailable).ToList();

            SetBoostStatus(() => Loc.T("Dashboard_BoostScanning"));
            await Cleaner.ScanItemsAsync(selected);

            // Never delete silently: show what Boost will remove and ask first.
            var toClean = selected.Where(i => i.HasFoundJunk).ToList();
            if (toClean.Count > 0)
            {
                var answer = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
                {
                    Title = Loc.T("Dashboard_BoostConfirmTitle"),
                    Content = Loc.F("Dashboard_BoostConfirmMessage", ByteSize.Format(toClean.Sum(i => i.FoundBytes)), Loc.Count("Count_Categories", toClean.Count)),
                    PrimaryButtonText = Loc.T("Dashboard_BoostRun"),
                    CloseButtonText = Loc.T("Common_Cancel"),
                });
                if (answer != ContentDialogResult.Primary)
                {
                    SetBoostStatus(() => Loc.T("Dashboard_BoostCancelled"));
                    return;
                }
            }

            SetBoostStatus(() => Loc.T("Dashboard_BoostCleaning"));
            var cleaned = await Cleaner.CleanItemsAsync(toClean, notify: false);

            SetBoostStatus(() => Loc.T("Dashboard_BoostDns"));
            await _actions.FlushDnsAsync();

            SetBoostStatus(() => Loc.T("Dashboard_BoostRam"));
            var ram = await _actions.FreeMemoryAsync();

            RefreshMetrics();
            Func<string> done = () => Loc.F("Dashboard_BoostDone", ByteSize.Format(cleaned.BytesFreed), ByteSize.Format(ram));
            SetBoostStatus(done);
            SetFeedback(cleaned.FilesSkipped > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                Loc.T(cleaned.FilesSkipped > 0 ? "Dashboard_BoostDonePartialTitle" : "Dashboard_BoostDoneTitle"),
                cleaned.FilesSkipped > 0 ? () => done() + " " + Loc.Count("Count_SkippedInUse", cleaned.FilesSkipped) : done);
            _snackbar.Show(FeedbackTitle ?? "", FeedbackMessage ?? "", ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Rocket24), TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Boost failed: {ex}");
            SetBoostStatus(null);
            SetFeedback(InfoBarSeverity.Error, Loc.T("Dashboard_BoostFailedTitle"), () => ex.Message);
            _snackbar.Show(Loc.T("Dashboard_BoostFailedTitle"), ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsBoosting = false;
        }
    }

    private bool CanBoost() => !IsBoosting;

    [RelayCommand(CanExecute = nameof(CanBoost))]
    private async Task ScanJunkAsync()
    {
        ShowFeedback = false;
        await Task.WhenAll(
            Cleaner.ScanItemsAsync(Cleaner.Items.Where(i => i.IsAvailable).ToList()),
            TempCleaner.ScanAsync());

        SetFeedback(InfoBarSeverity.Informational, Loc.T("Dashboard_ScanDoneTitle"),
            () => Loc.F("Dashboard_ScanDoneMessage", ByteSize.Format(JunkFoundBytes)));
    }

    [RelayCommand]
    private void OpenCleaner() => _navigation.Navigate(typeof(CleanerPage));

    [RelayCommand]
    private void OpenTempFiles() => _navigation.Navigate(typeof(TempCleanerPage));

    [RelayCommand]
    private void OpenOptimize() => _navigation.Navigate(typeof(OptimizePage));

    private void SetBoostStatus(Func<string>? status)
    {
        _boostStatus = status;
        OnPropertyChanged(nameof(BoostStatus));
    }

    private void SetFeedback(InfoBarSeverity severity, string title, Func<string> message)
    {
        FeedbackSeverity = severity;
        FeedbackTitle = title;
        _feedbackMessage = message;
        OnPropertyChanged(nameof(FeedbackMessage));
        ShowFeedback = true;
    }

    private void RefreshMetrics()
    {
        CpuPercent = _info.GetCpuUsage();

        var memory = _info.GetMemory();
        RamPercent = memory.Percent;
        RamText = Loc.F("Dashboard_RamText", ByteSize.Format((double)memory.Used), ByteSize.Format((double)memory.Total));

        var drive = _info.GetSystemDrive();
        DiskPercent = drive.Percent;
        DiskLabel = Loc.F("Dashboard_DriveLabel", drive.Name);
        DiskText = Loc.F("Dashboard_DiskText", ByteSize.Format(drive.Free), ByteSize.Format(drive.Total));

        var up = _info.Uptime;
        UptimeText = up.TotalDays >= 1
            ? Loc.F("Dashboard_UptimeDays", (int)up.TotalDays, up.Hours, up.Minutes)
            : Loc.F("Dashboard_UptimeHours", up.Hours, up.Minutes);
    }

    private void UpdateStats()
    {
        var last = _settings.Current.LastCleaned;
        LastCleanedText = last is null ? Loc.T("Dashboard_Never") : last.Value.ToString("g", LocalizationService.Instance.Culture);
        TotalFreedBytes = _settings.Current.TotalBytesFreed;
    }

    private void OnCleanerChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CleanerViewModel.SelectedBytes) or nameof(TempCleanerViewModel.FoundBytes):
                OnPropertyChanged(nameof(JunkFoundBytes));
                break;
            case nameof(CleanerViewModel.HasScanned):
                OnPropertyChanged(nameof(HasScanned));
                break;
            case nameof(CleanerViewModel.IsScanning):
                OnPropertyChanged(nameof(IsScanning));
                break;
        }
    }
}
