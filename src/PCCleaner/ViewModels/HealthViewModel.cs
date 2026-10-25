using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace PCCleaner.ViewModels;

public sealed partial class HealthViewModel(HealthToolsService service, ISnackbarService snackbar) : ObservableObject
{
    private const int MaxLines = 2000;

    // Lines like "Verification 45% complete." or "[=====   20.0%   ]" overwrite each other instead of piling up.
    [GeneratedRegex(@"\d+([.,]\d+)?\s*%")]
    private static partial Regex ProgressLine();

    private readonly List<string> _lines = [];
    private bool _lastWasProgress;
    private CancellationTokenSource? _cts;

    public IReadOnlyList<HealthTool> Tools => service.Tools;

    public bool IsAdministrator => AdminHelper.IsAdministrator;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunToolCommand), nameof(CancelCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string Output { get; set; } = "Pick a tool above to start. Output appears here live.";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Idle";

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunToolAsync(HealthTool tool)
    {
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _lines.Clear();
        _lastWasProgress = false;
        StatusText = $"Running {tool.Name}…";
        AppendLine($"> {tool.FileName} {tool.Arguments}");
        AppendLine("");

        var progress = new Progress<string>(AppendLine);
        try
        {
            var exitCode = await service.RunAsync(tool, line => ((IProgress<string>)progress).Report(line), _cts.Token);
            StatusText = exitCode == 0 ? $"{tool.Name} finished successfully" : $"{tool.Name} finished (exit code {exitCode})";
            snackbar.Show(tool.Name, StatusText, exitCode == 0 ? ControlAppearance.Success : ControlAppearance.Caution,
                new SymbolIcon(SymbolRegular.HeartPulse24), TimeSpan.FromSeconds(4));
        }
        catch (OperationCanceledException)
        {
            StatusText = $"{tool.Name} was cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"{tool.Name} could not start: {ex.Message}";
            AppLog.Write($"Health tool {tool.Name} failed: {ex}");
        }
        finally
        {
            IsRunning = false;
            AppLog.Write(StatusText);
        }
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
