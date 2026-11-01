using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Services;
using PCCleaner.Services.Localization;
using PCCleaner.Services.Tweaks;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace PCCleaner.ViewModels;

public sealed partial class TweakItemViewModel(ITweak tweak, OptimizeViewModel owner) : ObservableObject
{
    private bool _isApplied;

    public ITweak Tweak { get; } = tweak;

    public string Name => Tweak.Name;

    public string Description => Tweak.Description;

    public SymbolRegular Icon => Tweak.Icon;

    public bool IsAvailable => !Tweak.RequiresAdmin || AdminHelper.IsAdministrator;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Bound to the switch. Setting it applies or reverts the tweak; the real state is re-read afterwards.</summary>
    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            if (value == _isApplied || IsBusy) return;
            _isApplied = value;
            _ = owner.ToggleAsync(this, value);
        }
    }

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }

    internal void Refresh()
    {
        try
        {
            _isApplied = Tweak.IsApplied();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Reading state of {Name} failed: {ex.Message}");
        }

        OnPropertyChanged(nameof(IsApplied));
    }
}

public sealed partial class OptimizeViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly RestorePointService _restorePoints;
    private readonly SystemActions _actions;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;

    public OptimizeViewModel(TweakCatalog catalog, SettingsService settings, RestorePointService restorePoints,
        SystemActions actions, ISnackbarService snackbar, IContentDialogService dialogs)
    {
        _settings = settings;
        _restorePoints = restorePoints;
        _actions = actions;
        _snackbar = snackbar;
        _dialogs = dialogs;
        Tweaks = catalog.Tweaks.Select(t => new TweakItemViewModel(t, this)).ToList();
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            foreach (var tweak in Tweaks) tweak.RefreshText();
        };
    }

    public IReadOnlyList<TweakItemViewModel> Tweaks { get; }

    public bool IsAdministrator => AdminHelper.IsAdministrator;

    [ObservableProperty]
    public partial bool IsCreatingRestorePoint { get; set; }

    [ObservableProperty]
    public partial string? RestorePointResult { get; set; }

    [ObservableProperty]
    public partial bool IsFlushingDns { get; set; }

    [ObservableProperty]
    public partial string? DnsResult { get; set; }

    [ObservableProperty]
    public partial bool IsFreeingRam { get; set; }

    [ObservableProperty]
    public partial string? RamResult { get; set; }

    public void Activate()
    {
        foreach (var tweak in Tweaks) tweak.Refresh();
    }

    internal async Task ToggleAsync(TweakItemViewModel item, bool apply)
    {
        item.IsBusy = true;
        try
        {
            if (apply && !await EnsureRestorePointAsync()) return;

            await Task.Run(() =>
            {
                if (apply) item.Tweak.Apply();
                else item.Tweak.Revert();
            });

            AppLog.Write($"{(apply ? "Applied" : "Reverted")} tweak: {item.Name}");
            _snackbar.Show(Loc.T(apply ? "Optimize_Applied" : "Optimize_Restored"), item.Name, ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.CheckmarkCircle24), TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Tweak {item.Name} failed: {ex}");
            _snackbar.Show(Loc.T("Optimize_ChangeFailed"), ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(5));
        }
        finally
        {
            item.IsBusy = false;
            // Power plans are mutually exclusive, so re-read every switch.
            foreach (var tweak in Tweaks) tweak.Refresh();
        }
    }

    private async Task<bool> EnsureRestorePointAsync()
    {
        if (!_settings.Current.CreateRestorePoint || _restorePoints.CreatedThisSession || !AdminHelper.IsAdministrator)
            return true;

        IsCreatingRestorePoint = true;
        RestorePointResult = Loc.T("Optimize_RestoreCreatingFirst");
        var (success, message) = await _restorePoints.CreateAsync(Loc.F("Optimize_RestoreDescBefore", Loc.T("App_Name")));
        IsCreatingRestorePoint = false;
        RestorePointResult = message;

        if (success) return true;

        var answer = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = Loc.T("Optimize_NoRestoreTitle"),
            Content = Loc.F("Optimize_NoRestoreMessage", message),
            PrimaryButtonText = Loc.T("Optimize_ApplyAnyway"),
            CloseButtonText = Loc.T("Common_Cancel"),
        });
        return answer == ContentDialogResult.Primary;
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        IsCreatingRestorePoint = true;
        RestorePointResult = Loc.T("Optimize_RestoreCreating");
        var (success, message) = await _restorePoints.CreateAsync(Loc.F("Optimize_RestoreDescManual", Loc.T("App_Name")));
        IsCreatingRestorePoint = false;
        RestorePointResult = message;
        _snackbar.Show(Loc.T(success ? "Optimize_RestoreCreatedTitle" : "Optimize_RestoreFailedTitle"), message,
            success ? ControlAppearance.Success : ControlAppearance.Danger,
            new SymbolIcon(success ? SymbolRegular.ShieldCheckmark24 : SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private async Task FlushDnsAsync()
    {
        IsFlushingDns = true;
        var ok = await _actions.FlushDnsAsync();
        IsFlushingDns = false;
        DnsResult = ok ? Loc.F("Optimize_DnsFlushed", DateTime.Now.ToString("t", LocalizationService.Instance.Culture)) : Loc.T("Optimize_DnsFailed");
    }

    [RelayCommand]
    private async Task FreeRamAsync()
    {
        IsFreeingRam = true;
        var freed = await _actions.FreeMemoryAsync();
        IsFreeingRam = false;
        RamResult = Loc.F("Optimize_RamFreed", ByteSize.Format(freed), DateTime.Now.ToString("t", LocalizationService.Instance.Culture));
        _snackbar.Show(Loc.T("Optimize_RamTitle"), RamResult, ControlAppearance.Success,
            new SymbolIcon(SymbolRegular.DeveloperBoard24), TimeSpan.FromSeconds(3));
    }
}
