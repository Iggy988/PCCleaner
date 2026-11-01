using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Services;
using PCCleaner.Services.Localization;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace PCCleaner.ViewModels;

public sealed partial class HealthViewModel : ObservableObject
{
    private const int MaxLines = 2000;

    private readonly HealthToolsService service;
    private readonly ISnackbarService snackbar;
    private Func<string> _status = () => Loc.T("Health_Idle");

    public HealthViewModel(HealthToolsService service, ISnackbarService snackbar)
    {
        this.service = service;
        this.snackbar = snackbar;
        Output = Loc.T("Health_OutputPlaceholder");
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            if (_lines.Count == 0 && !IsRunning) Output = Loc.T("Health_OutputPlaceholder");
            // A new list instance makes the tool cards re-read their localized text.
            OnPropertyChanged(nameof(Tools));
            OnPropertyChanged(nameof(StatusText));
        };
    }

    // Lines like "Verification 45% complete." or "[=====   20.0%   ]" overwrite each other instead of piling up.
    [GeneratedRegex(@"\d+([.,]\d+)?\s*%")]
    private static partial Regex ProgressLine();

    private readonly List<string> _lines = [];
    private bool _lastWasProgress;
    private CancellationTokenSource? _cts;

    public IReadOnlyList<HealthTool> Tools => service.Tools.ToList();

    public bool IsAdministrator => AdminHelper.IsAdministrator;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunToolCommand), nameof(CancelCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string Output { get; set; }

    public string StatusText => _status();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunToolAsync(HealthTool tool)
    {
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _lines.Clear();
        _lastWasProgress = false;
        SetStatus(() => Loc.F("Health_Running", tool.Name));
        AppendLine($"> {tool.FileName} {tool.Arguments}");
        AppendLine("");

        var progress = new Progress<string>(AppendLine);
        try
        {
            var exitCode = await service.RunAsync(tool, line => ((IProgress<string>)progress).Report(line), _cts.Token);
            SetStatus(() => exitCode == 0 ? Loc.F("Health_Finished", tool.Name) : Loc.F("Health_FinishedCode", tool.Name, exitCode));
            snackbar.Show(tool.Name, StatusText, exitCode == 0 ? ControlAppearance.Success : ControlAppearance.Caution,
                new SymbolIcon(SymbolRegular.HeartPulse24), TimeSpan.FromSeconds(4));
        }
        catch (OperationCanceledException)
        {
            SetStatus(() => Loc.F("Health_Cancelled", tool.Name));
        }
        catch (Exception ex)
        {
            SetStatus(() => Loc.F("Health_CouldNotStart", tool.Name, ex.Message));
            AppLog.Write($"Health tool {tool.Name} failed: {ex}");
        }
        finally
        {
            IsRunning = false;
            AppLog.Write($"Health tool {tool.Id} ended.");
        }
    }

    private void SetStatus(Func<string> status)
    {
        _status = status;
        OnPropertyChanged(nameof(StatusText));
    }

    private bool CanRun(HealthTool? tool) => !IsRunning && AdminHelper.IsAdministrator;

    [RelayCommand(CanExecute = nameof(IsRunning))]
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

    private void AppendLine(string line)
    {
        line = line.TrimEnd('\0', ' ');
        var isProgress = ProgressLine().IsMatch(line) && line.Length < 120;
        if (string.IsNullOrWhiteSpace(line) && _lines.Count > 0 && string.IsNullOrWhiteSpace(_lines[^1])) return;

        if (isProgress && _lastWasProgress && _lines.Count > 0) _lines[^1] = line;
        else _lines.Add(line);

        _lastWasProgress = isProgress;
        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        Output = string.Join(Environment.NewLine, _lines);
    }
}
