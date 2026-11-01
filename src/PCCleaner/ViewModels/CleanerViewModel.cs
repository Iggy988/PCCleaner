using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Models;
using PCCleaner.Services;
using PCCleaner.Services.Cleaning;
using PCCleaner.Services.Localization;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace PCCleaner.ViewModels;

public sealed partial class CleanerViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private CancellationTokenSource? _cts;
    private CleanResult? _lastResult;

    public CleanerViewModel(CleanupCatalog catalog, SettingsService settings, ISnackbarService snackbar, IContentDialogService dialogs)
    {
        _settings = settings;
        _snackbar = snackbar;
        _dialogs = dialogs;

        var saved = settings.Current.RememberSelections ? settings.Current.CategorySelections : [];
        Items = catalog.Providers
            .Select(p => new CleanupItemViewModel(p, saved.TryGetValue(p.Id, out var isChecked) ? isChecked : p.IsRecommended))
            .ToList();
        foreach (var item in Items) item.PropertyChanged += OnItemChanged;

        Groups = new[]
            {
                new CleanupGroupViewModel("Cleaner_Group_Windows", SymbolRegular.Desktop24, ItemsIn(CleanupGroup.System)),
                new CleanupGroupViewModel("Cleaner_Group_Browsers", SymbolRegular.Globe24, ItemsIn(CleanupGroup.Browsers)),
                new CleanupGroupViewModel("Cleaner_Group_Apps", SymbolRegular.Apps24, ItemsIn(CleanupGroup.Applications)),
            }
            .Where(g => g.Items.Count > 0)
            .ToList();

        LocalizationService.Instance.LanguageChanged += (_, _) => RefreshText();
    }

    /// <summary>Raised after a cleaning run finishes (used by the dashboard to refresh its stats).</summary>
    public event EventHandler<CleanResult>? CleaningCompleted;

    public IReadOnlyList<CleanupItemViewModel> Items { get; }

    public IReadOnlyList<CleanupGroupViewModel> Groups { get; }

    public bool HasAdminOnlyItems => Items.Any(i => !i.IsAvailable);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand))]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    [ObservableProperty]
    public partial bool ShowResult { get; set; }

    /// <summary>Bytes freed by the last run (the view counts up to it).</summary>
    [ObservableProperty]
    public partial long FreedBytes { get; set; }

    [ObservableProperty]
    public partial bool ResultIsPartial { get; set; }

    [ObservableProperty]
    public partial string? ResultDetails { get; set; }

    [ObservableProperty]
    public partial string? BlockingWarning { get; set; }

    public bool IsBusy => IsScanning || IsCleaning;

    public int SelectedCount => Items.Count(i => i.IsChecked);

    /// <summary>Junk found in the selected categories by the last scan.</summary>
    public long SelectedBytes => Items.Where(i => i.IsChecked && i.Status == CleanupStatus.Scanned).Sum(i => i.FoundBytes);

    public string SelectionSummary => HasScanned
        ? Loc.F("Cleaner_SummaryScanned", Loc.Count("Count_Categories", SelectedCount), ByteSize.Format(SelectedBytes))
        : Loc.F("Cleaner_SummaryNotScanned", Loc.Count("Count_Categories", SelectedCount));

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task ScanAsync() => ScanItemsAsync(Items.Where(i => i.IsAvailable).ToList());

    private bool CanScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (!HasScanned) await ScanItemsAsync(Items.Where(i => i.IsAvailable).ToList());

        var selected = Items.Where(i => i.IsChecked && i.HasFoundJunk).ToList();
        if (selected.Count == 0)
        {
            _snackbar.Show(Loc.T("Cleaner_NothingTitle"), Loc.T("Cleaner_NothingMessage"), ControlAppearance.Info,
                new SymbolIcon(SymbolRegular.CheckmarkCircle24), TimeSpan.FromSeconds(3));
            return;
        }

        UpdateBlockingWarning();
        var answer = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = Loc.T("Cleaner_ConfirmTitle"),
            Content = Loc.F("Cleaner_ConfirmMessage", ByteSize.Format(selected.Sum(i => i.FoundBytes)), Loc.Count("Count_Categories", selected.Count)),
            PrimaryButtonText = Loc.T("Common_CleanNow"),
            CloseButtonText = Loc.T("Common_Cancel"),
        });

        if (answer == ContentDialogResult.Primary)
            await CleanItemsAsync(selected);
    }

    private bool CanClean() => !IsBusy && SelectedCount > 0;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var item in Items.Where(i => i.IsAvailable)) item.IsChecked = item.Provider.IsRecommended;
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Items.Where(i => i.IsAvailable)) item.IsChecked = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in Items) item.IsChecked = false;
    }

    public async Task ScanItemsAsync(IReadOnlyList<CleanupItemViewModel> items)
    {
        if (IsBusy) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsScanning = true;
        ShowResult = false;
        ProgressText = Loc.T("Cleaner_Analyzing");

        try
        {
            await Task.WhenAll(items.Select(async item =>
            {
                item.Status = CleanupStatus.Scanning;
                try
                {
                    var result = await item.Provider.ScanAsync(token);
                    item.FoundBytes = result.Bytes;
                    item.FoundFiles = result.FileCount;
                    item.Status = CleanupStatus.Scanned;
                }
                catch (OperationCanceledException)
                {
                    item.Status = CleanupStatus.NotScanned;
                }
                catch (Exception ex)
                {
                    item.Status = CleanupStatus.Failed;
                    AppLog.Write($"Scan of {item.Provider.Id} failed: {ex}");
                }
            }));

            HasScanned = !token.IsCancellationRequested;
        }
        finally
        {
            IsScanning = false;
            ProgressText = null;
            RaiseSelectionChanged();
            UpdateBlockingWarning();
        }
    }

    public async Task<CleanResult> CleanItemsAsync(IReadOnlyList<CleanupItemViewModel> items, bool notify = true)
    {
        if (IsBusy || items.Count == 0) return CleanResult.Empty;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsCleaning = true;
        ShowResult = false;
        Progress = 0;

        long totalBytes = Math.Max(1, items.Sum(i => i.FoundBytes));
        long completedBytes = 0, freed = 0;
        int deleted = 0, skipped = 0, failedCategories = 0;

        try
        {
            foreach (var item in items)
            {
                if (token.IsCancellationRequested) break;

                ProgressText = Loc.F("Cleaner_CleaningItem", item.Name);
                item.FreedBytes = 0;
                item.Status = CleanupStatus.Cleaning;
                var baseBytes = completedBytes;
                var progress = new Progress<CleanProgress>(p =>
                {
                    item.FreedBytes = p.BytesFreed;
                    Progress = Math.Min(100, (baseBytes + Math.Min(p.BytesFreed, item.FoundBytes)) * 100.0 / totalBytes);
                });

                try
                {
                    var result = await item.Provider.CleanAsync(progress, token);
                    item.FreedBytes = result.BytesFreed;
                    item.SkippedFiles = result.FilesSkipped;
                    item.Status = CleanupStatus.Cleaned;
                    freed += result.BytesFreed;
                    deleted += result.FilesDeleted;
                    skipped += result.FilesSkipped;
                }
                catch (OperationCanceledException)
                {
                    item.Status = CleanupStatus.NotScanned;
                }
                catch (Exception ex)
                {
                    item.Status = CleanupStatus.Failed;
                    failedCategories++;
                    AppLog.Write($"Cleaning {item.Provider.Id} failed: {ex}");
                }

                completedBytes += item.FoundBytes;
                Progress = Math.Min(100, completedBytes * 100.0 / totalBytes);
            }
        }
        finally
        {
            IsCleaning = false;
            ProgressText = null;
            RaiseSelectionChanged();
        }

        var summary = new CleanResult(freed, deleted, skipped);
        _settings.Current.LastCleaned = DateTime.Now;
        _settings.Current.TotalBytesFreed += freed;
        _settings.Save();
        AppLog.Write($"Cleaned {items.Count} categories: freed {freed:N0} bytes, {deleted:N0} files deleted, {skipped:N0} skipped, {failedCategories} failed.");

        _lastResult = summary;
        ResultIsPartial = skipped > 0 || failedCategories > 0 || token.IsCancellationRequested;
        ResultDetails = BuildResultDetails(summary);
        FreedBytes = freed;
        ShowResult = true;

        if (notify)
        {
            _snackbar.Show(
                Loc.T(ResultIsPartial ? "Cleaner_DonePartialTitle" : "Cleaner_DoneTitle"),
                Loc.F("Cleaner_DoneMessage", ByteSize.Format(freed)),
                ResultIsPartial ? ControlAppearance.Caution : ControlAppearance.Success,
                new SymbolIcon(ResultIsPartial ? SymbolRegular.Warning24 : SymbolRegular.CheckmarkCircle24), TimeSpan.FromSeconds(4));
        }

        CleaningCompleted?.Invoke(this, summary);
        return summary;
    }

    private static string BuildResultDetails(CleanResult result) => result.FilesSkipped > 0
        ? $"{Loc.Count("Count_FilesRemoved", result.FilesDeleted)} · {Loc.Count("Count_SkippedInUse", result.FilesSkipped)}"
        : Loc.Count("Count_FilesRemoved", result.FilesDeleted);

    private void UpdateBlockingWarning()
    {
        var running = Items
            .Where(i => i.IsChecked && i.Provider.BlockingProcesses.Any(p => Process.GetProcessesByName(p).Length > 0))
            .Select(i => i.Provider.BlockingAppName ?? i.Name)
            .ToList();

        BlockingWarning = running.Count == 0 ? null : Loc.F("Cleaner_BlockingWarning", string.Join(", ", running));
    }

    private void RefreshText()
    {
        foreach (var item in Items) item.RefreshText();
        foreach (var group in Groups) group.RefreshText();
        if (_lastResult is not null) ResultDetails = BuildResultDetails(_lastResult);
        if (BlockingWarning is not null) UpdateBlockingWarning();
        RaiseSelectionChanged();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupItemViewModel.IsChecked) && sender is CleanupItemViewModel item)
        {
            if (_settings.Current.RememberSelections)
            {
                _settings.Current.CategorySelections[item.Provider.Id] = item.IsChecked;
                _settings.Save();
            }

            RaiseSelectionChanged();
            CleanCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName is nameof(CleanupItemViewModel.Status) or nameof(CleanupItemViewModel.FoundBytes))
        {
            RaiseSelectionChanged();
        }
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private List<CleanupItemViewModel> ItemsIn(CleanupGroup group) => Items.Where(i => i.Provider.Group == group).ToList();
}
