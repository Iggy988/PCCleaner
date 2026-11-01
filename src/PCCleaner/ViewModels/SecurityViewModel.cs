using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PCCleaner.Models;
using PCCleaner.Services;
using PCCleaner.Services.Localization;
using PCCleaner.Services.Security;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace PCCleaner.ViewModels;

public sealed class DefenderThreatViewModel(DefenderThreat threat) : ObservableObject
{
    public DefenderThreat Threat { get; } = threat;

    public string Name => Threat.Name;

    public string Resources => Threat.Resources.Count == 0 ? Loc.T("Sec_NoResources") : string.Join(Environment.NewLine, Threat.Resources);

    public string Details => Loc.F("Sec_ThreatDetails",
        Loc.T(Threat.SeverityId is 1 or 2 or 4 or 5 ? $"Sec_ThreatSev_{Threat.SeverityId}" : "Sec_ThreatSev_0"),
        Threat.DetectedAt?.ToString("g") ?? "—",
        Loc.T(Threat.IsActive ? "Sec_ThreatActive" : "Sec_ThreatResolved"));

    public bool IsActive => Threat.IsActive;

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(Resources));
        OnPropertyChanged(nameof(Details));
    }
}

public sealed partial class SuspiciousItemViewModel(SuspiciousItem item) : ObservableObject
{
    public SuspiciousItem Item { get; } = item;

    public string Name => Item.DisplayName;

    public string Source => Loc.T("Sec_Source_" + Item.Source);

    public string Path => Item.FilePath ?? Item.Command ?? "";

    public string? Command => Item.Command;

    public bool HasCommand => !string.IsNullOrEmpty(Item.Command) && Item.Command != Item.FilePath;

    public string Severity => Loc.T("Sec_Level_" + Item.Severity);

    public bool IsHigh => Item.Severity == SuspicionSeverity.High;

    public bool IsMedium => Item.Severity == SuspicionSeverity.Medium;

    public string Reasons
    {
        get
        {
            var reasons = Item.Reasons.Select(Loc.T).ToList();
            if (Item.VirusTotalPositives is { } positives)
                reasons.Add(positives < 0 ? Loc.T("Sec_VtUnknown") : Loc.F("Sec_VtResult", positives));
            return string.Join(" · ", reasons);
        }
    }

    public bool FileExists => Item.FileExists;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(Severity));
        OnPropertyChanged(nameof(Reasons));
        OnPropertyChanged(nameof(IsHigh));
        OnPropertyChanged(nameof(IsMedium));
        OnPropertyChanged(nameof(FileExists));
    }
}

public sealed class QuarantineEntryViewModel(QuarantineEntry entry) : ObservableObject
{
    public QuarantineEntry Entry { get; } = entry;

    public string FileName => System.IO.Path.GetFileName(Entry.OriginalPath);

    public string OriginalPath => Entry.OriginalPath;

    public string Details => Loc.F("Sec_QuarantineDetails", Entry.QuarantinedAt.ToString("g"), ByteSize.Format(Entry.Size),
        string.Join(" · ", Entry.Reasons.Select(Loc.T)));

    public bool PendingReboot => Entry.PendingReboot;

    internal void RefreshText() => OnPropertyChanged(nameof(Details));
}

public sealed partial class SecurityViewModel : ObservableObject
{
    private const int MaxLines = 2000;

    private readonly DefenderService _defender;
    private readonly SuspiciousItemScanner _scanner;
    private readonly AmsiScanner _amsi;
    private readonly QuarantineService _quarantine;
    private readonly VirusTotalClient _virusTotal;
    private readonly RestorePointService _restorePoints;
    private readonly SettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly List<string> _lines = [];
    private Func<string> _status = () => Loc.T("Sec_Idle");
    private CancellationTokenSource? _cts;
    private bool _loaded;

    public SecurityViewModel(DefenderService defender, SuspiciousItemScanner scanner, AmsiScanner amsi, QuarantineService quarantine,
        VirusTotalClient virusTotal, RestorePointService restorePoints, SettingsService settings,
        ISnackbarService snackbar, IContentDialogService dialogs)
    {
        _defender = defender;
        _scanner = scanner;
        _amsi = amsi;
        _quarantine = quarantine;
        _virusTotal = virusTotal;
        _restorePoints = restorePoints;
        _settings = settings;
        _snackbar = snackbar;
        _dialogs = dialogs;
        Output = Loc.T("Sec_OutputPlaceholder");

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            if (_lines.Count == 0 && !IsBusy) Output = Loc.T("Sec_OutputPlaceholder");
            foreach (var threat in Threats) threat.RefreshText();
            foreach (var item in SuspiciousItems) item.Refresh();
            foreach (var entry in QuarantineEntries) entry.RefreshText();
            OnStatusChanged(Status);
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(SuspiciousSummary));
        };
    }

    public bool IsAdministrator => AdminHelper.IsAdministrator;

    public ObservableCollection<DefenderThreatViewModel> Threats { get; } = [];

    public ObservableCollection<SuspiciousItemViewModel> SuspiciousItems { get; } = [];

    public ObservableCollection<QuarantineEntryViewModel> QuarantineEntries { get; } = [];

    public ObservableCollection<DefenderQuarantineItem> DefenderQuarantine { get; } = [];

    [ObservableProperty]
    public partial DefenderStatus? Status { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuickScanCommand), nameof(FullScanCommand), nameof(ScanFolderCommand),
        nameof(UpdateSignaturesCommand), nameof(ScanSuspiciousCommand), nameof(ScanScriptsCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Output { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuspiciousSummary))]
    public partial bool HasScannedSuspicious { get; set; }

    public string StatusText => _status();

    public bool IsProtected => Status is { Available: true, AntivirusEnabled: true, RealTimeProtectionEnabled: true, IsPassive: false };

    public bool DefenderUsable => Status is { Available: true, AntivirusEnabled: true, IsPassive: false };

    public string ProtectionTitle => Status switch
    {
        null => Loc.T("Sec_StatusChecking"),
        { Available: false } or { AntivirusEnabled: false } or { IsPassive: true } when Status.OtherAntivirus is not null
            => Loc.F("Sec_StatusOtherAv", Status.OtherAntivirus),
        { Available: false } => Loc.T("Sec_StatusUnavailable"),
        { AntivirusEnabled: false } => Loc.T("Sec_StatusDisabled"),
        { RealTimeProtectionEnabled: false } => Loc.T("Sec_StatusRealtimeOff"),
        _ => Loc.T("Sec_StatusProtected"),
    };

    public string ProtectionDetail => Status is { Available: true }
        ? Loc.F("Sec_StatusDetail", Status.SignatureVersion ?? "—", Status.SignatureUpdated?.ToString("g") ?? "—",
              Status.LastQuickScan?.ToString("g") ?? Loc.T("Sec_Never"))
          + (Status.TamperProtected ? " · " + Loc.T("Sec_TamperOn") : "")
        : Status?.OtherAntivirus is not null ? Loc.T("Sec_OtherAvDetail") : "";

    public bool VirusTotalEnabled => _virusTotal.IsEnabled;

    public bool HasThreats => Threats.Count > 0;

    public bool HasActiveThreats => Threats.Any(t => t.IsActive);

    public bool HasQuarantine => QuarantineEntries.Count > 0;

    public bool HasDefenderQuarantine => DefenderQuarantine.Count > 0;

    public string SuspiciousSummary => !HasScannedSuspicious
        ? Loc.T("Sec_SuspiciousNotScanned")
        : SuspiciousItems.Count == 0 ? Loc.T("Sec_SuspiciousNone") : Loc.Count("Count_Suspicious", SuspiciousItems.Count);

    partial void OnStatusChanged(DefenderStatus? value)
    {
        OnPropertyChanged(nameof(IsProtected));
        OnPropertyChanged(nameof(DefenderUsable));
        OnPropertyChanged(nameof(ProtectionTitle));
        OnPropertyChanged(nameof(ProtectionDetail));
    }

    public void Activate()
    {
        OnPropertyChanged(nameof(VirusTotalEnabled));
        if (_loaded) return;
        _loaded = true;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            Status = await _defender.GetStatusAsync();
            await LoadThreatsAsync();
            await LoadQuarantineAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Security refresh failed: {ex}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    // ---------- Defender scans ----------

    [RelayCommand(CanExecute = nameof(CanRunAdmin))]
    private Task QuickScanAsync() => RunDefenderAsync(Loc.T("Sec_QuickScan"), (onLine, ct) => _defender.ScanAsync(ScanKind.Quick, null, onLine, ct));

    [RelayCommand(CanExecute = nameof(CanRunAdmin))]
    private Task FullScanAsync() => RunDefenderAsync(Loc.T("Sec_FullScan"), (onLine, ct) => _defender.ScanAsync(ScanKind.Full, null, onLine, ct));

    [RelayCommand(CanExecute = nameof(CanRunAdmin))]
    private Task ScanFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Sec_PickFolder") };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;
        var folder = dialog.FolderName;
        return RunDefenderAsync(Loc.F("Sec_FolderScan", folder), (onLine, ct) => _defender.ScanAsync(ScanKind.Custom, folder, onLine, ct));
    }

    [RelayCommand(CanExecute = nameof(CanRunAdmin))]
    private Task UpdateSignaturesAsync() => RunDefenderAsync(Loc.T("Sec_UpdateSignatures"), _defender.UpdateSignaturesAsync);

    /// <summary>AMSI check of every script in a folder: works with whichever antivirus is registered.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ScanScriptsAsync()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Sec_PickFolder") };
        if (dialog.ShowDialog() != true) return;
        var folder = dialog.FolderName;

        StartRun(Loc.F("Sec_ScriptScanRunning", folder));
        var progress = new Progress<string>(AppendLine);
        try
        {
            var detected = await _amsi.ScanFolderAsync(folder, progress, _cts!.Token);
            AppendLine("");
            SetStatus(() => detected.Count == 0 ? Loc.T("Sec_ScriptScanClean") : Loc.F("Sec_ScriptScanFound", detected.Count));
            Notify(Loc.T("Sec_ScanScripts"), StatusText, detected.Count == 0 ? ControlAppearance.Success : ControlAppearance.Danger);
        }
        catch (OperationCanceledException)
        {
            SetStatus(() => Loc.T("Sec_Cancelled"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(() => Loc.F("Sec_Failed", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunDefenderAsync(string title, Func<Action<string>, CancellationToken, Task<int>> run)
    {
        StartRun(Loc.F("Sec_Running", title));
        var progress = new Progress<string>(AppendLine);
        try
        {
            var exitCode = await run(line => ((IProgress<string>)progress).Report(line), _cts!.Token);
            // MpCmdRun: 0 = nothing found, 2 = threats found (and handled per Defender's settings).
            SetStatus(() => exitCode switch
            {
                0 => Loc.F("Sec_FinishedClean", title),
                2 => Loc.F("Sec_FinishedThreats", title),
                _ => Loc.F("Sec_FinishedCode", title, exitCode),
            });
            Notify(title, StatusText, exitCode switch { 0 => ControlAppearance.Success, 2 => ControlAppearance.Danger, _ => ControlAppearance.Caution });
            AppLog.Write($"{title} finished with {exitCode}");
        }
        catch (OperationCanceledException)
        {
            SetStatus(() => Loc.T("Sec_Cancelled"));
        }
        catch (Exception ex)
        {
            SetStatus(() => Loc.F("Sec_Failed", ex.Message));
            AppLog.Write($"{title} failed: {ex}");
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    private void StartRun(string status)
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        _lines.Clear();
        Output = "";
        SetStatus(() => status);
    }

    private bool CanRun() => !IsBusy;

    private bool CanRunAdmin() => !IsBusy && AdminHelper.IsAdministrator;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void CopyOutput()
    {
        try { Clipboard.SetText(Output); } catch (System.Runtime.InteropServices.COMException) { }
    }

    [RelayCommand]
    private void ClearOutput()
    {
        _lines.Clear();
        Output = "";
    }

    // ---------- Defender detections ----------

    private async Task LoadThreatsAsync()
    {
        var threats = await _defender.GetThreatsAsync();
        Threats.Clear();
        foreach (var threat in threats) Threats.Add(new DefenderThreatViewModel(threat));
        OnPropertyChanged(nameof(HasThreats));
        OnPropertyChanged(nameof(HasActiveThreats));
    }

    [RelayCommand]
    private async Task RemoveActiveThreatsAsync()
    {
        if (!AdminHelper.IsAdministrator) return;
        var (success, output) = await _defender.RemoveActiveThreatsAsync();
        AppLog.Write($"Remove-MpThreat: {success} {output}");
        Notify(Loc.T("Sec_RemoveThreats"), success ? Loc.T("Sec_ThreatsRemoved") : Loc.F("Sec_Failed", output),
            success ? ControlAppearance.Success : ControlAppearance.Danger);
        await LoadThreatsAsync();
    }

    [RelayCommand]
    private async Task AllowThreatAsync(DefenderThreatViewModel threat)
    {
        if (!AdminHelper.IsAdministrator) return;
        if (!await ConfirmAsync(Loc.T("Sec_AllowTitle"), Loc.F("Sec_AllowMessage", threat.Name), Loc.T("Sec_Allow"))) return;

        var (success, output) = await _defender.AllowThreatAsync(threat.Threat.ThreatId);
        AppLog.Write($"Allowed threat {threat.Name}: {success} {output}");
        Notify(Loc.T("Sec_AllowTitle"), success ? Loc.F("Sec_Allowed", threat.Name) : Loc.F("Sec_Failed", output),
            success ? ControlAppearance.Success : ControlAppearance.Danger);
    }

    // ---------- Suspicious items ----------

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ScanSuspiciousAsync()
    {
        StartRun(Loc.T("Sec_SuspiciousRunning"));
        var progress = new Progress<string>(line => { AppendLine(line); SetStatus(() => line); });
        try
        {
            var items = await _scanner.ScanAsync(progress, _cts!.Token);
            SuspiciousItems.Clear();
            foreach (var item in items)
            {
                SuspiciousItems.Add(new SuspiciousItemViewModel(item));
                AppendLine($"[{item.Severity}] {item.DisplayName} — {item.FilePath ?? item.Command}");
            }

            HasScannedSuspicious = true;
            SetStatus(() => SuspiciousSummary);
            Notify(Loc.T("Sec_SuspiciousTitle"), SuspiciousSummary, items.Count == 0 ? ControlAppearance.Success : ControlAppearance.Caution);
        }
        catch (OperationCanceledException)
        {
            SetStatus(() => Loc.T("Sec_Cancelled"));
        }
        catch (Exception ex)
        {
            SetStatus(() => Loc.F("Sec_Failed", ex.Message));
            AppLog.Write($"Suspicious scan failed: {ex}");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(SuspiciousSummary));
        }
    }

    [RelayCommand]
    private Task ScanItemWithDefenderAsync(SuspiciousItemViewModel item)
    {
        if (IsBusy || !AdminHelper.IsAdministrator || !item.FileExists) return Task.CompletedTask;
        var path = item.Item.FilePath!;
        return RunDefenderAsync(Loc.F("Sec_FolderScan", path), (onLine, ct) => _defender.ScanAsync(ScanKind.Custom, path, onLine, ct));
    }

    [RelayCommand]
    private static void OpenItemLocation(SuspiciousItemViewModel item)
    {
        if (item.FileExists) Process.Start("explorer.exe", $"/select,\"{item.Item.FilePath}\"");
    }

    [RelayCommand]
    private async Task CheckVirusTotalAsync(SuspiciousItemViewModel item)
    {
        if (!_virusTotal.IsEnabled || !item.FileExists || item.IsBusy) return;
        item.IsBusy = true;
        try
        {
            item.Item.VirusTotalPositives = await _virusTotal.LookupFileAsync(item.Item.FilePath!, CancellationToken.None);
            if (item.Item.VirusTotalPositives > 0 && item.Item.Severity < SuspicionSeverity.High)
                item.Item.Severity = item.Item.VirusTotalPositives >= 3 ? SuspicionSeverity.High : SuspicionSeverity.Medium;
            item.Refresh();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Notify("VirusTotal", Loc.F("Sec_Failed", ex.Message), ControlAppearance.Danger);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task QuarantineItemAsync(SuspiciousItemViewModel item)
    {
        if (!AdminHelper.IsAdministrator || !item.FileExists || item.IsBusy) return;
        if (!await ConfirmAsync(Loc.T("Sec_QuarantineTitle"), Loc.F("Sec_QuarantineMessage", item.Item.FilePath), Loc.T("Sec_Quarantine")))
            return;
        if (!await EnsureRestorePointAsync()) return;

        item.IsBusy = true;
        try
        {
            var entry = await _quarantine.QuarantineAsync(item.Item.FilePath!, item.Item.Reasons, item.Item.StartupItem);
            SuspiciousItems.Remove(item);
            OnPropertyChanged(nameof(SuspiciousSummary));
            await LoadQuarantineAsync();
            Notify(Loc.T("Sec_QuarantineTitle"),
                Loc.F(entry.PendingReboot ? "Sec_QuarantinedPending" : "Sec_Quarantined", item.Name), ControlAppearance.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            AppLog.Write($"Quarantine of {item.Item.FilePath} failed: {ex}");
            Notify(Loc.T("Sec_QuarantineTitle"), Loc.F("Sec_Failed", ex.Message), ControlAppearance.Danger);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private async Task<bool> EnsureRestorePointAsync()
    {
        if (!_settings.Current.CreateRestorePoint || _restorePoints.CreatedThisSession) return true;

        SetStatus(() => Loc.T("Sec_CreatingRestorePoint"));
        var (success, message) = await _restorePoints.CreateAsync(Loc.F("Sec_RestorePointDesc", Loc.T("App_Name")));
        SetStatus(() => message);
        return success || await ConfirmAsync(Loc.T("Optimize_NoRestoreTitle"), Loc.F("Sec_NoRestoreMessage", message), Loc.T("Sec_Quarantine"));
    }

    // ---------- Quarantine ----------

    private async Task LoadQuarantineAsync()
    {
        var entries = await Task.Run(_quarantine.GetEntries);
        QuarantineEntries.Clear();
        foreach (var entry in entries) QuarantineEntries.Add(new QuarantineEntryViewModel(entry));

        DefenderQuarantine.Clear();
        if (AdminHelper.IsAdministrator)
        {
            foreach (var item in await _defender.GetQuarantineAsync()) DefenderQuarantine.Add(item);
        }

        OnPropertyChanged(nameof(HasQuarantine));
        OnPropertyChanged(nameof(HasDefenderQuarantine));
    }

    [RelayCommand]
    private async Task RestoreQuarantinedAsync(QuarantineEntryViewModel entry)
    {
        if (!AdminHelper.IsAdministrator) return;
        if (!await ConfirmAsync(Loc.T("Sec_RestoreTitle"), Loc.F("Sec_RestoreMessage", entry.OriginalPath), Loc.T("Sec_Restore"))) return;

        try
        {
            var result = await _quarantine.RestoreAsync(entry.Entry.Id, overwrite: false);
            if (result == QuarantineRestoreResult.TargetExists
                && await ConfirmAsync(Loc.T("Sec_RestoreTitle"), Loc.F("Sec_RestoreOverwrite", entry.OriginalPath), Loc.T("Sec_Overwrite")))
                result = await _quarantine.RestoreAsync(entry.Entry.Id, overwrite: true);

            switch (result)
            {
                case QuarantineRestoreResult.Restored:
                    Notify(Loc.T("Sec_RestoreTitle"), Loc.F("Sec_Restored", entry.FileName), ControlAppearance.Success);
                    break;
                case QuarantineRestoreResult.PendingReboot:
                    Notify(Loc.T("Sec_RestoreTitle"), Loc.T("Sec_RestorePendingReboot"), ControlAppearance.Caution);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or System.Text.Json.JsonException)
        {
            AppLog.Write($"Restore of {entry.Entry.Id} failed: {ex}");
            Notify(Loc.T("Sec_RestoreTitle"), Loc.F("Sec_Failed", ex.Message), ControlAppearance.Danger);
        }

        await LoadQuarantineAsync();
    }

    [RelayCommand]
    private async Task DeleteQuarantinedAsync(QuarantineEntryViewModel entry)
    {
        if (!AdminHelper.IsAdministrator) return;
        if (!await ConfirmAsync(Loc.T("Sec_DeleteTitle"), Loc.F("Sec_DeleteMessage", entry.FileName), Loc.T("Sec_Delete"))) return;

        try
        {
            await _quarantine.DeletePermanentlyAsync(entry.Entry.Id);
            QuarantineEntries.Remove(entry);
            OnPropertyChanged(nameof(HasQuarantine));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify(Loc.T("Sec_DeleteTitle"), Loc.F("Sec_Failed", ex.Message), ControlAppearance.Danger);
        }
    }

    [RelayCommand]
    private async Task RestoreDefenderItemAsync(DefenderQuarantineItem item)
    {
        if (!AdminHelper.IsAdministrator) return;
        if (!await ConfirmAsync(Loc.T("Sec_RestoreTitle"), Loc.F("Sec_RestoreDefenderMessage", item.ThreatName), Loc.T("Sec_Restore"))) return;

        var (success, output) = await _defender.RestoreFromQuarantineAsync(item.ThreatName);
        AppLog.Write($"Defender restore {item.ThreatName}: {success} {output}");
        Notify(Loc.T("Sec_RestoreTitle"), success ? Loc.F("Sec_Restored", item.ThreatName) : Loc.F("Sec_Failed", output),
            success ? ControlAppearance.Success : ControlAppearance.Danger);
        await LoadQuarantineAsync();
    }

    [RelayCommand]
    private static void RestartAsAdministrator()
    {
        if (AdminHelper.StartElevatedInstance()) Application.Current.Shutdown();
    }

    // ---------- Helpers ----------

    private async Task<bool> ConfirmAsync(string title, string content, string primary)
    {
        var answer = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = Loc.T("Common_Cancel"),
        });
        return answer == ContentDialogResult.Primary;
    }

    private void Notify(string title, string message, ControlAppearance appearance)
    {
        var icon = appearance switch
        {
            ControlAppearance.Success => SymbolRegular.ShieldCheckmark24,
            ControlAppearance.Danger => SymbolRegular.ShieldError24,
            _ => SymbolRegular.Shield24,
        };
        _snackbar.Show(title, message, appearance, new SymbolIcon(icon), TimeSpan.FromSeconds(5));
    }

    private void SetStatus(Func<string> status)
    {
        _status = status;
        OnPropertyChanged(nameof(StatusText));
    }

    private void AppendLine(string line)
    {
        line = line.TrimEnd('\0', ' ');
        if (string.IsNullOrWhiteSpace(line) && _lines.Count > 0 && string.IsNullOrWhiteSpace(_lines[^1])) return;
        _lines.Add(line);
        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        Output = string.Join(Environment.NewLine, _lines);
    }
}
