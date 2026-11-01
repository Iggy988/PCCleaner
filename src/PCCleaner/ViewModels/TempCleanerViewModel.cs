using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Models;
using PCCleaner.Services;
using PCCleaner.Services.Cleaning.Temp;
using PCCleaner.Services.Localization;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace PCCleaner.ViewModels;

public enum TempItemState
{
    NotScanned,
    Scanning,
    Ready,
    Empty,
    Missing,
    AccessDenied,
    ResolveError,
    NeedsAdmin,
    Cleaning,
    Cleaned,
    Cancelled,
    Failed,
}

public sealed partial class TempLocationViewModel : ObservableObject
{
    public TempLocationViewModel(TempLocation location, bool isChecked)
    {
        Location = location;
        IsAvailable = !location.RequiresAdmin || AdminHelper.IsAdministrator;
        IsChecked = isChecked && IsAvailable;
        State = !location.IsResolved ? TempItemState.ResolveError : IsAvailable ? TempItemState.NotScanned : TempItemState.NeedsAdmin;
    }

    public TempLocation Location { get; internal set; }

    public TempLocationKind Kind => Location.Kind;

    public string SettingsKey => "temp-" + Kind.ToString().ToLowerInvariant();

    public SymbolRegular Icon => Kind switch
    {
        TempLocationKind.UserTemp => SymbolRegular.PersonClock24,
        TempLocationKind.WindowsTemp => SymbolRegular.Window24,
        _ => SymbolRegular.Flash24,
    };

    public string Title => Loc.T($"Temp_{Kind}_Name");

    public string Description => Loc.T($"Temp_{Kind}_Desc");

    /// <summary>Extra caution (Prefetch only).</summary>
    public string? Note => Kind == TempLocationKind.Prefetch ? Loc.T("Temp_Prefetch_Note") : null;

    public string DisplayPath => Location.Path ?? Loc.T("Temp_PathUnresolved");

    public bool IsAvailable { get; }

    /// <summary>Shown with a "Restart as administrator" button.</summary>
    public bool NeedsElevation => !IsAvailable || State == TempItemState.AccessDenied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(DetailText), nameof(IsBusy), nameof(CanClean), nameof(NeedsElevation), nameof(Tone))]
    public partial TempItemState State { get; set; }

    [ObservableProperty]
    public partial int EligibleFiles { get; set; }

    [ObservableProperty]
    public partial long EligibleBytes { get; set; }

    [ObservableProperty]
    public partial int LockedFiles { get; set; }

    [ObservableProperty]
    public partial int InaccessibleItems { get; set; }

    [ObservableProperty]
    public partial int DeletedFiles { get; set; }

    [ObservableProperty]
    public partial long FreedBytes { get; set; }

    [ObservableProperty]
    public partial int SkippedFiles { get; set; }

    public bool IsBusy => State is TempItemState.Scanning or TempItemState.Cleaning;

    /// <summary>Selected, scanned, and has something to delete.</summary>
    public bool CanClean => IsChecked && IsAvailable && State == TempItemState.Ready && EligibleFiles > 0;

    /// <summary>Colour hint for the status label: Success, Accent, Caution, Critical or Neutral.</summary>
    public string Tone => State switch
    {
        TempItemState.Ready => "Accent",
        TempItemState.Empty or TempItemState.Cleaned when SkippedFiles == 0 => "Success",
        TempItemState.Cleaned or TempItemState.Cancelled or TempItemState.AccessDenied or TempItemState.NeedsAdmin => "Caution",
        TempItemState.ResolveError or TempItemState.Failed => "Critical",
        _ => "Neutral",
    };

    public string StatusText => State switch
    {
        TempItemState.Scanning => Loc.T("Status_Scanning"),
        TempItemState.Ready => ByteSize.Format(EligibleBytes),
        TempItemState.Empty => Loc.T("Status_Clean"),
        TempItemState.Missing => Loc.T("Temp_State_Missing"),
        TempItemState.AccessDenied => Loc.T("Temp_State_AccessDenied"),
        TempItemState.ResolveError => Loc.T("Temp_State_ResolveError"),
        TempItemState.NeedsAdmin => Loc.T("Status_RequiresAdmin"),
        TempItemState.Cleaning => FreedBytes > 0 ? ByteSize.Format(FreedBytes) : Loc.T("Status_Cleaning"),
        TempItemState.Cleaned => "−" + ByteSize.Format(FreedBytes),
        TempItemState.Cancelled => Loc.T("Temp_State_Cancelled"),
        TempItemState.Failed => Loc.T("Status_Error"),
        _ => Loc.T("Temp_State_NotScanned"),
    };

    public string? DetailText
    {
        get
        {
            var parts = new List<string>();
            switch (State)
            {
                case TempItemState.Ready or TempItemState.Empty:
                    parts.Add(Loc.Count("Count_EligibleFiles", EligibleFiles));
                    if (LockedFiles > 0) parts.Add(Loc.Count("Count_InUse", LockedFiles));
                    if (InaccessibleItems > 0) parts.Add(Loc.Count("Count_Inaccessible", InaccessibleItems));
                    break;
                case TempItemState.Cleaned or TempItemState.Cancelled or TempItemState.Cleaning:
                    parts.Add(Loc.Count("Count_FilesRemoved", DeletedFiles));
                    if (SkippedFiles > 0) parts.Add(Loc.Count("Count_Skipped", SkippedFiles));
                    break;
                default:
                    return null;
            }

            return string.Join(" · ", parts);
        }
    }

    public void RefreshText() => OnPropertyChanged(string.Empty);
}

public sealed record SkippedItemViewModel(string Path, SkipReason Reason)
{
    public string ReasonText => Loc.T($"Skip_{Reason}");
}

public sealed partial class TempCleanerViewModel : ObservableObject
{
    private readonly TempFileCleaner _cleaner;
    private readonly SettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private CancellationTokenSource? _cts;
    private TempRunSummary? _lastRun;

    public TempCleanerViewModel(TempFileCleaner cleaner, SettingsService settings, ISnackbarService snackbar, IContentDialogService dialogs)
    {
        _cleaner = cleaner;
        _settings = settings;
        _snackbar = snackbar;
        _dialogs = dialogs;

        var saved = settings.Current.RememberSelections ? settings.Current.CategorySelections : [];
        Items = cleaner.GetLocations()
            .Select(location =>
            {
                var item = new TempLocationViewModel(location, isChecked: true);
                // Prefetch is opt-in: clearing it doesn't speed Windows up.
                item.IsChecked = item.IsAvailable && (saved.TryGetValue(item.SettingsKey, out var c) ? c : location.Kind != TempLocationKind.Prefetch);
                item.PropertyChanged += OnItemChanged;
                return item;
            })
            .ToList();

        LocalizationService.Instance.LanguageChanged += (_, _) => RefreshText();
    }

    /// <summary>Raised after a clean finishes (the dashboard refreshes its totals).</summary>
    public event EventHandler<CleanResult>? CleaningCompleted;

    public IReadOnlyList<TempLocationViewModel> Items { get; }

    public bool IsAdministrator => AdminHelper.IsAdministrator;

    public bool HasLocationsNeedingAdmin => Items.Any(i => !i.IsAvailable);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand))]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    [ObservableProperty]
    public partial string? ProgressCounts { get; set; }

    /// <summary>Live and final bytes freed (the view counts up to it).</summary>
    [ObservableProperty]
    public partial long FreedBytes { get; set; }

    [ObservableProperty]
    public partial int DeletedFiles { get; set; }

    [ObservableProperty]
    public partial bool ShowResult { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity ResultSeverity { get; set; }

    [ObservableProperty]
    public partial string? ResultTitle { get; set; }

    [ObservableProperty]
    public partial string? ResultMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkippedItems))]
    public partial IReadOnlyList<SkippedItemViewModel> SkippedItems { get; set; } = [];

    public bool HasSkippedItems => SkippedItems.Count > 0;

    public bool IsBusy => IsScanning || IsCleaning;

    public IEnumerable<TempLocationViewModel> Selected => Items.Where(i => i.CanClean);

    /// <summary>Reclaimable space found in the selected locations by the last scan.</summary>
    public long SelectedBytes => Selected.Sum(i => i.EligibleBytes);

    public int SelectedFiles => Selected.Sum(i => i.EligibleFiles);

    /// <summary>Reclaimable space in all scanned, accessible locations (for the dashboard).</summary>
    public long FoundBytes => Items.Where(i => i.State == TempItemState.Ready).Sum(i => i.EligibleBytes);

    public string Summary => HasScanned
        ? Loc.F("Temp_SummaryScanned", Loc.Count("Count_Files", SelectedFiles), ByteSize.Format(SelectedBytes))
        : Loc.T("Temp_SummaryNotScanned");

    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task ScanAsync()
    {
        if (IsBusy) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsScanning = true;
        ShowResult = false;
        ProgressText = Loc.T("Temp_Scanning");

        // Re-resolve so the "skip recent files" setting and any environment changes apply.
        var locations = _cleaner.GetLocations().ToDictionary(l => l.Kind);

        try
        {
            await Task.WhenAll(Items.Select(async item =>
            {
                item.Location = locations[item.Kind];
                item.DeletedFiles = 0;
                item.FreedBytes = 0;
                item.SkippedFiles = 0;
                if (!item.Location.IsResolved)
                {
                    item.State = TempItemState.ResolveError;
                    return;
                }

                if (!item.IsAvailable)
                {
                    item.State = TempItemState.NeedsAdmin;
                    return;
                }

                item.State = TempItemState.Scanning;
                try
                {
                    var result = await _cleaner.ScanAsync(item.Location, token);
                    item.EligibleFiles = result.Scan.FileCount;
                    item.EligibleBytes = result.Scan.Bytes;
                    item.LockedFiles = result.Scan.LockedFiles;
                    item.InaccessibleItems = result.Scan.InaccessibleItems;
                    item.State = result.Status switch
                    {
                        TempLocationStatus.Missing => TempItemState.Missing,
                        TempLocationStatus.AccessDenied => TempItemState.AccessDenied,
                        TempLocationStatus.ResolveError => TempItemState.ResolveError,
                        _ => result.Scan.FileCount > 0 ? TempItemState.Ready : TempItemState.Empty,
                    };
                }
                catch (OperationCanceledException)
                {
                    item.State = TempItemState.NotScanned;
                }
                catch (Exception ex)
                {
                    item.State = TempItemState.Failed;
                    AppLog.Write($"Temp scan of {item.Kind} failed: {ex}");
                }
            }));

            HasScanned = !token.IsCancellationRequested;
        }
        finally
        {
            IsScanning = false;
            ProgressText = null;
            RaiseTotalsChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CleanAsync()
    {
        if (!HasScanned) await ScanAsync();

        var selected = Selected.ToList();
        if (selected.Count == 0)
        {
            _snackbar.Show(Loc.T("Cleaner_NothingTitle"), Loc.T("Temp_NothingMessage"), ControlAppearance.Info,
                new SymbolIcon(SymbolRegular.CheckmarkCircle24), TimeSpan.FromSeconds(3));
            return;
        }

        if (!await ConfirmAsync(selected)) return;
        await CleanItemsAsync(selected);
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private static void RestartAsAdministrator()
    {
        if (AdminHelper.StartElevatedInstance())
            Application.Current.Shutdown();
    }

    [RelayCommand]
    private static void OpenFolder(TempLocationViewModel? item)
    {
        if (item?.Location.Path is { } path && Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private async Task<bool> ConfirmAsync(IReadOnlyList<TempLocationViewModel> selected)
    {
        var content = new StringBuilder();
        content.AppendLine(Loc.T("Temp_ConfirmIntro"));
        content.AppendLine();
        foreach (var item in selected)
            content.AppendLine($"•  {item.Title}: {Loc.Count("Count_Files", item.EligibleFiles)} · {ByteSize.Format(item.EligibleBytes)}");
        content.AppendLine();
        content.AppendLine(Loc.F("Temp_ConfirmTotal", ByteSize.Format(selected.Sum(i => i.EligibleBytes))));
        content.AppendLine();
        content.Append(Loc.T("Temp_ConfirmSafety"));
        if (selected.Any(i => i.Kind == TempLocationKind.Prefetch))
        {
            content.AppendLine();
            content.AppendLine();
            content.Append(Loc.T("Temp_Prefetch_Note"));
        }

        var answer = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = Loc.T("Temp_ConfirmTitle"),
            Content = content.ToString(),
            PrimaryButtonText = Loc.T("Temp_ConfirmButton"),
            CloseButtonText = Loc.T("Common_Cancel"),
        });
        return answer == ContentDialogResult.Primary;
    }

    private async Task CleanItemsAsync(IReadOnlyList<TempLocationViewModel> items)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsCleaning = true;
        ShowResult = false;
        Progress = 0;
        FreedBytes = 0;
        DeletedFiles = 0;

        // Locked files are attempted (and skipped) too, so they count towards progress.
        var totalFiles = Math.Max(1, items.Sum(i => i.EligibleFiles + i.LockedFiles));
        var run = new TempRunSummary();
        var cancelled = false;

        try
        {
            foreach (var item in items)
            {
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                ProgressText = Loc.F("Cleaner_CleaningItem", item.Title);
                item.State = TempItemState.Cleaning;
                var before = run.Snapshot();
                var last = default(CleanProgress);
                var progress = new Progress<CleanProgress>(p =>
                {
                    last = p;
                    item.DeletedFiles = p.Deleted;
                    item.FreedBytes = p.BytesFreed;
                    item.SkippedFiles = p.Skipped;
                    UpdateLiveCounters(before, p, totalFiles);
                });

                try
                {
                    var result = await _cleaner.CleanAsync(item.Location, progress, token);
                    run.Add(result);
                    item.DeletedFiles = result.FilesDeleted;
                    item.FreedBytes = result.BytesFreed;
                    item.SkippedFiles = result.FilesSkipped;
                    item.State = TempItemState.Cleaned;
                }
                catch (OperationCanceledException)
                {
                    // Report what was actually deleted before the cancel, from the last progress update.
                    cancelled = true;
                    run.AddPartial(last);
                    item.State = TempItemState.Cancelled;
                }
                catch (Exception ex)
                {
                    run.Errors++;
                    item.State = TempItemState.Failed;
                    AppLog.Write($"Temp clean of {item.Kind} failed: {ex}");
                }

                UpdateLiveCounters(run.Snapshot(), default, totalFiles);
            }
        }
        finally
        {
            IsCleaning = false;
            ProgressText = null;
            ProgressCounts = null;
            RaiseTotalsChanged();
        }

        run.Cancelled = cancelled;
        _lastRun = run;
        _settings.Current.LastCleaned = DateTime.Now;
        _settings.Current.TotalBytesFreed += run.BytesFreed;
        _settings.Save();

        ShowRunResult(run);
        _snackbar.Show(ResultTitle ?? "", ResultMessage ?? "",
            ResultSeverity switch
            {
                InfoBarSeverity.Success => ControlAppearance.Success,
                InfoBarSeverity.Error => ControlAppearance.Danger,
                _ => ControlAppearance.Caution,
            },
            new SymbolIcon(ResultSeverity == InfoBarSeverity.Success ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.Warning24),
            TimeSpan.FromSeconds(5));

        CleaningCompleted?.Invoke(this, new CleanResult(run.BytesFreed, run.Deleted, run.Skipped));
    }

    private void UpdateLiveCounters(TempRunSummary.Counts before, CleanProgress current, int totalFiles)
    {
        var processed = before.Processed + current.Processed;
        DeletedFiles = before.Deleted + current.Deleted;
        FreedBytes = before.BytesFreed + current.BytesFreed;
        Progress = Math.Min(100, processed * 100.0 / totalFiles);
        ProgressCounts = Loc.F("Temp_ProgressCounts",
            processed.ToString("N0", LocalizationService.Instance.Culture),
            totalFiles.ToString("N0", LocalizationService.Instance.Culture),
            Loc.Count("Count_FilesRemoved", DeletedFiles),
            Loc.Count("Count_Skipped", before.Skipped + current.Skipped));
    }

    private void ShowRunResult(TempRunSummary run)
    {
        FreedBytes = run.BytesFreed;
        DeletedFiles = run.Deleted;

        var nothingDeleted = run.Deleted == 0;
        ResultSeverity = run.Errors > 0 && nothingDeleted ? InfoBarSeverity.Error
            : run.Skipped > 0 || run.Errors > 0 || run.Cancelled ? InfoBarSeverity.Warning
            : InfoBarSeverity.Success;

        ResultTitle = run.Cancelled ? Loc.T("Temp_ResultCancelled")
            : ResultSeverity == InfoBarSeverity.Success ? Loc.T("Temp_ResultSuccess")
            : ResultSeverity == InfoBarSeverity.Error ? Loc.T("Temp_ResultFailed")
            : Loc.T("Temp_ResultPartial");

        var details = new List<string> { Loc.F("Temp_ResultFreed", Loc.Count("Count_FilesRemoved", run.Deleted), ByteSize.Format(run.BytesFreed)) };
        if (run.InUse > 0) details.Add(Loc.Count("Count_InUse", run.InUse));
        if (run.AccessDenied > 0) details.Add(Loc.Count("Count_AccessDenied", run.AccessDenied));
        if (run.Unsafe > 0) details.Add(Loc.Count("Count_SkippedSafety", run.Unsafe));
        if (run.Failed > 0) details.Add(Loc.Count("Count_Errors", run.Failed));
        if (run.OtherSkipped > 0) details.Add(Loc.Count("Count_Skipped", run.OtherSkipped));
        if (run.AlreadyGone > 0) details.Add(Loc.Count("Count_AlreadyGone", run.AlreadyGone));
        if (run.Errors > 0) details.Add(Loc.T("Temp_ResultLocationError"));
        ResultMessage = string.Join(" · ", details);

        SkippedItems = run.SkippedItems.Select(s => new SkippedItemViewModel(s.Path, s.Reason)).ToList();
        ShowResult = true;
    }

    private void RefreshText()
    {
        foreach (var item in Items) item.RefreshText();
        if (_lastRun is not null && ShowResult) ShowRunResult(_lastRun);
        RaiseTotalsChanged();
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TempLocationViewModel.IsChecked) && sender is TempLocationViewModel item && _settings.Current.RememberSelections)
        {
            _settings.Current.CategorySelections[item.SettingsKey] = item.IsChecked;
            _settings.Save();
        }

        if (e.PropertyName is nameof(TempLocationViewModel.IsChecked) or nameof(TempLocationViewModel.State))
            RaiseTotalsChanged();
    }

    private void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedFiles));
        OnPropertyChanged(nameof(FoundBytes));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Accumulates per-location results into one accurate report.</summary>
    private sealed class TempRunSummary
    {
        private readonly List<SkippedFile> _skipped = [];

        public readonly record struct Counts(int Processed, int Deleted, int Skipped, long BytesFreed);

        public long BytesFreed { get; private set; }

        public int Deleted { get; private set; }

        public int Processed { get; private set; }

        public int InUse { get; private set; }

        public int AccessDenied { get; private set; }

        public int Unsafe { get; private set; }

        public int Failed { get; private set; }

        public int AlreadyGone { get; private set; }

        /// <summary>Skipped files of a cancelled location, whose individual reasons weren't reported.</summary>
        public int OtherSkipped { get; private set; }

        /// <summary>Locations that failed outright.</summary>
        public int Errors { get; set; }

        public bool Cancelled { get; set; }

        public int Skipped => InUse + AccessDenied + Unsafe + Failed + OtherSkipped;

        public IReadOnlyList<SkippedFile> SkippedItems => _skipped;

        public Counts Snapshot() => new(Processed, Deleted, Skipped, BytesFreed);

        public void Add(CleanResult result)
        {
            BytesFreed += result.BytesFreed;
            Deleted += result.FilesDeleted;
            Processed += result.FilesDeleted + result.FilesSkipped + result.AlreadyGone;
            InUse += result.SkippedInUse;
            AccessDenied += result.SkippedAccessDenied;
            Unsafe += result.SkippedUnsafe;
            Failed += result.Failed;
            AlreadyGone += result.AlreadyGone;
            _skipped.AddRange(result.SkippedItems.Take(Math.Max(0, 200 - _skipped.Count)));
        }

        /// <summary>Counts from the last progress report of a cancelled location.</summary>
        public void AddPartial(CleanProgress progress)
        {
            BytesFreed += progress.BytesFreed;
            Deleted += progress.Deleted;
            Processed += progress.Processed;
            OtherSkipped += progress.Skipped;
        }
    }
}
