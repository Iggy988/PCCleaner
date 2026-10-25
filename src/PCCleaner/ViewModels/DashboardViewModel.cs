using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Services;
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
    private readonly DispatcherTimer _timer;

    public DashboardViewModel(SystemInfoService info, SystemActions actions, SettingsService settings,
        CleanerViewModel cleaner, INavigationService navigation, ISnackbarService snackbar)
    {
        _info = info;
        _actions = actions;
        _settings = settings;
        _navigation = navigation;
        _snackbar = snackbar;
        Cleaner = cleaner;
        Cleaner.CleaningCompleted += (_, _) => UpdateStats();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _timer.Tick += (_, _) => RefreshMetrics();

        BoostStatus = DefaultBoostStatus;
        UpdateStats();
    }

    private const string DefaultBoostStatus = "Cleans your selected junk, flushes DNS and frees up RAM in one click.";

    public CleanerViewModel Cleaner { get; }

    public string Greeting => DateTime.Now.Hour switch
    {
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening",
    };

    public string MachineName => _info.MachineName;

    public string OsName => _info.OsName;

    public string CpuName => _info.CpuName;

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
    public partial string DiskLabel { get; set; } = "C:";

    [ObservableProperty]
    public partial string LastCleanedText { get; set; } = "";

    [ObservableProperty]
    public partial string TotalFreedText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BoostCommand), nameof(ScanJunkCommand))]
    public partial bool IsBoosting { get; set; }

    [ObservableProperty]
    public partial string BoostStatus { get; set; }

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
        try
        {
            var selected = Cleaner.Items.Where(i => i.IsChecked && i.IsAvailable).ToList();

            BoostStatus = "Scanning for junk files…";
            await Cleaner.ScanItemsAsync(selected);

            BoostStatus = "Removing junk files…";
            var cleaned = await Cleaner.CleanItemsAsync(selected.Where(i => i.HasFoundJunk).ToList(), notify: false);

            BoostStatus = "Flushing DNS cache…";
            await _actions.FlushDnsAsync();

            BoostStatus = "Freeing up memory…";
            var ram = await _actions.FreeMemoryAsync();

            RefreshMetrics();
            BoostStatus = $"Done! Freed {ByteSize.Format(cleaned.BytesFreed)} of disk space and {ByteSize.Format(ram)} of RAM.";
            _snackbar.Show("Boost complete", BoostStatus, ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Rocket24), TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Boost failed: {ex}");
            BoostStatus = DefaultBoostStatus;
            _snackbar.Show("Boost failed", ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsBoosting = false;
        }
    }

    private bool CanBoost() => !IsBoosting;

    [RelayCommand(CanExecute = nameof(CanBoost))]
    private Task ScanJunkAsync() => Cleaner.ScanItemsAsync(Cleaner.Items.Where(i => i.IsAvailable).ToList());

    [RelayCommand]
    private void OpenCleaner() => _navigation.Navigate(typeof(CleanerPage));

    private void RefreshMetrics()
    {
        CpuPercent = _info.GetCpuUsage();

        var memory = _info.GetMemory();
        RamPercent = memory.Percent;
        RamText = $"{ByteSize.Format((double)memory.Used)} of {ByteSize.Format((double)memory.Total)} in use";

        var drive = _info.GetSystemDrive();
        DiskPercent = drive.Percent;
        DiskLabel = $"Drive {drive.Name}";
        DiskText = $"{ByteSize.Format(drive.Free)} free of {ByteSize.Format(drive.Total)}";

        var up = _info.Uptime;
        UptimeText = up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h {up.Minutes}m" : $"{up.Hours}h {up.Minutes}m";
    }

    private void UpdateStats()
    {
        var last = _settings.Current.LastCleaned;
        LastCleanedText = last is null ? "Never" : last.Value.ToString("g");
        TotalFreedText = ByteSize.Format(_settings.Current.TotalBytesFreed);
    }
}
