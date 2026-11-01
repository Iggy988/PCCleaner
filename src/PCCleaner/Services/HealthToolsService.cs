using System.IO;
using System.Text;
using PCCleaner.Services.Localization;
using PCCleaner.Services.Native;
using Wpf.Ui.Controls;

namespace PCCleaner.Services;

/// <summary>A Windows repair tool. Text comes from the string resources: <c>Health_{id}_Name</c> / <c>_Desc</c>.</summary>
public sealed record HealthTool(
    string Id,
    SymbolRegular Icon,
    string Minutes,
    string FileName,
    string Arguments,
    bool UnicodeOutput = false)
{
    private string Key => "Health_" + Id.Replace('-', '_');

    public string Name => Loc.T(Key + "_Name");

    public string Description => Loc.F(Key + "_Desc", HealthToolsService.SystemDrive);

    public string Duration => Loc.F("Health_Duration", Minutes);
}

/// <summary>Built-in Windows repair/maintenance tools, run with live output.</summary>
public sealed class HealthToolsService
{
    internal static readonly string SystemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

    public IReadOnlyList<HealthTool> Tools { get; } =
    [
        new("sfc", SymbolRegular.ShieldCheckmark24, "10–20", "sfc.exe", "/scannow", UnicodeOutput: true),
        new("dism-restore", SymbolRegular.Wrench24, "10–30", "dism.exe", "/Online /Cleanup-Image /RestoreHealth"),
        new("dism-cleanup", SymbolRegular.Broom24, "5–15", "dism.exe", "/Online /Cleanup-Image /StartComponentCleanup"),
        new("defrag", SymbolRegular.Gauge24, "1–10", "defrag.exe", $"{SystemDrive} /O /U /V"),
        new("chkdsk", SymbolRegular.Storage24, "2–10", "chkdsk.exe", $"{SystemDrive} /scan"),
    ];

    public Task<int> RunAsync(HealthTool tool, Action<string> onLine, CancellationToken cancellationToken)
    {
        // sfc writes UTF-16 when redirected; the others use the console's OEM code page.
        var encoding = tool.UnicodeOutput ? Encoding.Unicode : Encoding.GetEncoding((int)NativeMethods.GetOEMCP());
        var path = Path.Combine(Environment.SystemDirectory, tool.FileName);
        return ProcessRunner.RunAsync(path, tool.Arguments, onLine, encoding, cancellationToken);
    }
}
