using System.IO;
using System.Text;
using PCCleaner.Services.Native;
using Wpf.Ui.Controls;

namespace PCCleaner.Services;

public sealed record HealthTool(
    string Id,
    string Name,
    string Description,
    SymbolRegular Icon,
    string Duration,
    string FileName,
    string Arguments,
    bool UnicodeOutput = false);

/// <summary>Built-in Windows repair/maintenance tools, run with live output.</summary>
public sealed class HealthToolsService
{
    private static readonly string SystemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

    public IReadOnlyList<HealthTool> Tools { get; } =
    [
        new("sfc", "System File Checker",
            "Scans protected Windows files and repairs corrupted ones (sfc /scannow).",
            SymbolRegular.ShieldCheckmark24, "10–20 min", "sfc.exe", "/scannow", UnicodeOutput: true),
        new("dism-restore", "Repair Windows image",
            "Checks the Windows component store and downloads fresh copies of damaged files (DISM RestoreHealth).",
            SymbolRegular.Wrench24, "10–30 min", "dism.exe", "/Online /Cleanup-Image /RestoreHealth"),
        new("dism-cleanup", "Clean up component store",
            "Removes superseded Windows update components from WinSxS to free disk space.",
            SymbolRegular.Broom24, "5–15 min", "dism.exe", "/Online /Cleanup-Image /StartComponentCleanup"),
        new("defrag", "Optimize system drive",
            $"Runs TRIM on SSDs or defragments hard drives for {SystemDrive}.",
            SymbolRegular.Gauge24, "1–10 min", "defrag.exe", $"{SystemDrive} /O /U /V"),
        new("chkdsk", "Check disk for errors",
            $"Scans {SystemDrive} for file system errors online, without making changes (chkdsk /scan).",
            SymbolRegular.Storage24, "2–10 min", "chkdsk.exe", $"{SystemDrive} /scan"),
    ];

    public Task<int> RunAsync(HealthTool tool, Action<string> onLine, CancellationToken cancellationToken)
    {
        // sfc writes UTF-16 when redirected; the others use the console's OEM code page.
        var encoding = tool.UnicodeOutput ? Encoding.Unicode : Encoding.GetEncoding((int)NativeMethods.GetOEMCP());
        var path = Path.Combine(Environment.SystemDirectory, tool.FileName);
        return ProcessRunner.RunAsync(path, tool.Arguments, onLine, encoding, cancellationToken);
    }
}
