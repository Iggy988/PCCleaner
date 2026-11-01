using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Services;
using PCCleaner.Services.Localization;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace PCCleaner.ViewModels;

public sealed partial class UpdateItemViewModel(PendingUpdate update) : ObservableObject
{
    public PendingUpdate Update { get; } = update;

    public string Title => Update.Title;

    /// <summary>"KB5031234 · 312 MB · Downloaded · Optional"</summary>
    public string Details => string.Join(" · ", new[]
    {
        Update.KbArticle,
        Update.SizeBytes > 0 ? ByteSize.Format(Update.SizeBytes) : null,
        Update.IsDownloaded ? Loc.T("System_UpdateDownloaded") : null,
        Update.IsOptional ? Loc.T("System_UpdateOptional") : null,
    }.Where(p => p is not null));

    /// <summary>Optional (browse-only) updates such as feature upgrades aren't preselected, matching Settings.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; } = !update.IsOptional;

    public void RefreshText() => OnPropertyChanged(nameof(Details));
}

public sealed partial class SystemViewModel : ObservableObject
{
    private readonly ComputerNameService _names;
    private readonly WindowsUpdateService _updates;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private Func<string> _updateStatus = () => Loc.T("System_UpdatesIdle");
    private CancellationTokenSource? _cts;

    public SystemViewModel(ComputerNameService names, WindowsUpdateService updates, ISnackbarService snackbar,
        IContentDialogService dialogs)
    {
        _names = names;
        _updates = updates;
        _snackbar = snackbar;
        _dialogs = dialogs;
        Updates.CollectionChanged += (_, _) => InstallUpdatesCommand.NotifyCanExecuteChanged();
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            foreach (var item in Updates) item.RefreshText();
            OnPropertyChanged(nameof(CurrentNameText));
            OnPropertyChanged(nameof(PendingNameText));
            OnPropertyChanged(nameof(NameError));
            OnPropertyChanged(nameof(UpdateStatus));
        };
    }

    public bool IsAdministrator => AdminHelper.IsAdministrator;

    // ---------- Computer name ----------

    public string CurrentNameText => Loc.F("System_CurrentName", _names.CurrentName);

    public string? PendingNameText => _names.PendingName is { } pending ? Loc.F("System_PendingName", pending) : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    public partial string NewName { get; set; } = "";

    public string? NameError => ComputerNameService.Validate(NewName.Trim()) is { } key ? Loc.T(key) : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    public partial bool IsRenaming { get; set; }

    public void Activate()
    {
        OnPropertyChanged(nameof(PendingNameText));
        RebootRequired = WindowsUpdateService.IsRebootRequired() || _names.PendingName is not null;
    }

    private bool CanRename()
    {
        var name = NewName.Trim();
        return AdminHelper.IsAdministrator && !IsRenaming && name.Length > 0 && NameError is null
               && !string.Equals(name, _names.PendingName ?? _names.CurrentName, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameAsync()
    {
        var name = NewName.Trim();
        IsRenaming = true;
        try
        {
            await _names.RenameAsync(name);
            NewName = "";
            OnPropertyChanged(nameof(PendingNameText));
            RebootRequired = true;
            _snackbar.Show(Loc.T("System_RenameTitle"), Loc.F("System_Renamed", name), ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Rename24), TimeSpan.FromSeconds(4));
            await AskToRestartAsync();
        }
        catch (Win32Exception ex)
        {
            AppLog.Write($"Renaming the computer to {name} failed: {ex.Message}");
            _snackbar.Show(Loc.T("System_RenameFailed"), ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsRenaming = false;
        }
    }

    // ---------- Restart ----------

    [ObservableProperty]
    public partial bool RebootRequired { get; set; }

    [RelayCommand]
    private Task RestartAsync() => AskToRestartAsync();

    private async Task AskToRestartAsync()
    {
        var answer = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = Loc.T("System_RestartTitle"),
            Content = Loc.T("System_RestartMessage"),
            PrimaryButtonText = Loc.T("System_RestartNow"),
            CloseButtonText = Loc.T("System_RestartLater"),
        });
        if (answer == ContentDialogResult.Primary) ComputerNameService.RestartComputer();
    }

    // ---------- Windows Update ----------

    public ObservableCollection<UpdateItemViewModel> Updates { get; } = [];

    public string UpdateStatus => _updateStatus();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateBusy))]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(InstallUpdatesCommand))]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateBusy))]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(InstallUpdatesCommand), nameof(CancelInstallCommand))]
    public partial bool IsInstalling { get; set; }

    public bool IsUpdateBusy => IsSearching || IsInstalling;

    private bool CanCheck() => !IsUpdateBusy;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckForUpdatesAsync()
    {
        IsSearching = true;
        SetUpdateStatus(() => Loc.T("System_Searching"));
        try
        {
            var found = await _updates.SearchAsync();
            Updates.Clear();
            foreach (var update in found)
            {
                var item = new UpdateItemViewModel(update);
                item.PropertyChanged += (_, _) => InstallUpdatesCommand.NotifyCanExecuteChanged();
                Updates.Add(item);
            }

            var checkedAt = DateTime.Now;
            SetUpdateStatus(() =>
            {
                var lastChecked = Loc.F("System_LastChecked", checkedAt.ToString("t", LocalizationService.Instance.Culture));
                var summary = Updates.Count == 0 ? Loc.T("System_UpToDate") : Loc.Count("Count_Updates", Updates.Count);
                return $"{summary} · {lastChecked}";
            });
            RebootRequired |= WindowsUpdateService.IsRebootRequired();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Windows Update search failed: 0x{ex.HResult:X8} {ex.Message}");
            SetUpdateStatus(() => Loc.F("System_SearchFailed", ex.Message));
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanInstall() => AdminHelper.IsAdministrator && !IsUpdateBusy && Updates.Any(u => u.IsSelected);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallUpdatesAsync()
    {
        var selected = Updates.Where(u => u.IsSelected).ToList();
        _cts = new CancellationTokenSource();
        IsInstalling = true;

        var progress = new Progress<UpdateProgress>(p => SetUpdateStatus(() => Loc.F(
            p.Phase == UpdatePhase.Downloading ? "System_Downloading" : "System_Installing", p.Index, p.Total, p.Title)));
        var installed = 0;
        try
        {
            var result = await _updates.InstallAsync(selected.Select(u => u.Update.Id).ToList(), progress, _cts.Token);
            installed = result.Installed;
            RebootRequired |= result.RebootRequired;
            SetUpdateStatus(() => Loc.F("System_InstallDone", result.Installed, selected.Count));
            _snackbar.Show(Loc.T("System_UpdatesTitle"), UpdateStatus,
                result.Failed == 0 ? ControlAppearance.Success : ControlAppearance.Caution,
                new SymbolIcon(SymbolRegular.ArrowSync24), TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            SetUpdateStatus(() => Loc.T("System_InstallCancelled"));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Installing updates failed: 0x{ex.HResult:X8} {ex.Message}");
            SetUpdateStatus(() => Loc.F("System_SearchFailed", ex.Message));
        }
        finally
        {
            IsInstalling = false;
        }

        // Show what is still missing (installed ones drop off the list).
        if (installed > 0) await CheckForUpdatesAsync();
        if (RebootRequired && installed > 0) await AskToRestartAsync();
    }

    [RelayCommand(CanExecute = nameof(IsInstalling))]
    private void CancelInstall() => _cts?.Cancel();

    [RelayCommand]
    private static void OpenWindowsUpdateSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true }); }
        catch (Win32Exception ex) { AppLog.Write($"Opening Windows Update settings failed: {ex.Message}"); }
    }

    private void SetUpdateStatus(Func<string> status)
    {
        _updateStatus = status;
        OnPropertyChanged(nameof(UpdateStatus));
    }
}
