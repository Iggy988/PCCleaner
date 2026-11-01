using System.IO;
using System.Text.RegularExpressions;
using PCCleaner.Models;
using PCCleaner.Services.Cleaning;

namespace PCCleaner.Services.Security;

/// <summary>What the scanner knows about one startup entry, scheduled task or script.</summary>
internal readonly record struct Evidence(
    string? Command,
    string? FilePath,
    bool FileExists,
    bool IsScript,
    SignatureState Signature,
    AmsiVerdict Amsi,
    string? ScriptContent = null);

/// <summary>
/// Heuristics for the suspicious-items scan. Pure functions (no file system access) so they can be unit-tested.
/// Each rule yields a string-resource key (<c>Sec_Reason_*</c>).
/// </summary>
internal static partial class SuspiciousRules
{
    public const string ObfuscatedCommand = "Sec_Reason_ObfuscatedCommand";
    public const string ObfuscatedScript = "Sec_Reason_ObfuscatedScript";
    public const string DoubleExtension = "Sec_Reason_DoubleExtension";
    public const string ScriptHost = "Sec_Reason_ScriptHost";
    public const string UnsignedUserFolder = "Sec_Reason_UnsignedUserFolder";
    public const string MissingTarget = "Sec_Reason_MissingTarget";
    public const string AmsiDetected = "Sec_Reason_AmsiDetected";

    private static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static readonly HashSet<string> HighReasons = [ObfuscatedCommand, DoubleExtension, AmsiDetected];
    private static readonly HashSet<string> MediumReasons = [ObfuscatedScript, ScriptHost, UnsignedUserFolder];

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf",
        ".jpg", ".jpeg", ".png", ".gif", ".mp3", ".mp4", ".avi", ".zip", ".rar",
    };

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".scr", ".com", ".pif", ".bat", ".cmd", ".js", ".jse", ".vbs", ".vbe", ".wsf", ".hta", ".ps1", ".lnk",
    };

    // Command lines that hide or download what they run. PowerShell switches only count after powershell/pwsh.
    [GeneratedRegex(
        @"(powershell|pwsh).*\s-(e(nc(odedcommand)?)?\s+[A-Za-z0-9+/=]{16,}|w(indowstyle)?\s+hidden)" +
        @"|FromBase64String|\bIEX\b|Invoke-Expression|DownloadString|DownloadFile" +
        @"|mshta(\.exe)?[""']?\s+[""']?(https?|javascript|vbscript):" +
        @"|regsvr32(\.exe)?[""']?\s.*/i:https?:" +
        @"|rundll32(\.exe)?[""']?\s+javascript:" +
        @"|certutil(\.exe)?[""']?\s.*-urlcache",
        RegexOptions.IgnoreCase)]
    private static partial Regex ObfuscatedCommandPattern();

    [GeneratedRegex(@"\b(wscript|cscript|mshta)(\.exe)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptHostPattern();

    // Script content: legitimate scripts use some of these, so a script needs two different indicators.
    private static readonly Regex[] ScriptIndicators =
    [
        new(@"FromBase64String\s*\(", RegexOptions.IgnoreCase),
        new(@"\bIEX\b|Invoke-Expression", RegexOptions.IgnoreCase),
        new(@"DownloadString\s*\(|DownloadFile\s*\(|Net\.WebClient", RegexOptions.IgnoreCase),
        new(@"-EncodedCommand|-enc\s+[A-Za-z0-9+/=]{40,}", RegexOptions.IgnoreCase),
        new(@"-WindowStyle\s+Hidden|-w\s+hidden", RegexOptions.IgnoreCase),
        new(@"String\.fromCharCode\s*\((\s*\d+\s*,){20,}", RegexOptions.IgnoreCase),
        new(@"\beval\s*\(\s*(unescape|atob)\s*\(", RegexOptions.IgnoreCase),
        new(@"[A-Za-z0-9+/]{400,}={0,2}"),
    ];

    /// <summary>Folders a normal user (or malware running as that user) can write to.</summary>
    public static IReadOnlyList<string> DefaultUserWritableRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
            {
                Path.GetTempPath(),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetEnvironmentVariable("PUBLIC") ?? "",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                string.IsNullOrEmpty(profile) ? "" : Path.Combine(profile, "Downloads"),
            }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(PathSafety.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsInUserWritableLocation(string path, IReadOnlyList<string> roots) =>
        roots.Any(root => PathSafety.IsStrictlyInside(path, root));

    public static bool HasObfuscatedCommand(string? command) =>
        !string.IsNullOrWhiteSpace(command) && ObfuscatedCommandPattern().IsMatch(command);

    public static bool HasObfuscatedScriptContent(string? content) =>
        !string.IsNullOrEmpty(content) && ScriptIndicators.Count(r => r.IsMatch(content)) >= 2;

    /// <summary>"invoice.pdf.exe", "photo.jpg.js"...</summary>
    public static bool HasDoubleExtension(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var name = Path.GetFileName(path);
        return ExecutableExtensions.Contains(Path.GetExtension(name))
               && DocumentExtensions.Contains(Path.GetExtension(Path.GetFileNameWithoutExtension(name)));
    }

    /// <summary>wscript/cscript/mshta running a script that sits in a user-writable folder.</summary>
    public static bool IsScriptHostFromUserFolder(string? command, IReadOnlyList<string> roots)
    {
        if (string.IsNullOrWhiteSpace(command) || !ScriptHostPattern().IsMatch(command)) return false;
        var expanded = Environment.ExpandEnvironmentVariables(command);
        return roots.Any(root => expanded.Contains(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public static (List<string> Reasons, SuspicionSeverity Severity) Evaluate(Evidence e, IReadOnlyList<string> roots)
    {
        var reasons = new List<string>();
        var trustedFile = e.Signature == SignatureState.SignedByMicrosoft;

        if (HasObfuscatedCommand(e.Command)) reasons.Add(ObfuscatedCommand);
        if (e.IsScript && HasObfuscatedScriptContent(e.ScriptContent)) reasons.Add(ObfuscatedScript);
        if (!trustedFile && HasDoubleExtension(e.FilePath)) reasons.Add(DoubleExtension);
        if (IsScriptHostFromUserFolder(e.Command, roots)) reasons.Add(ScriptHost);

        if (!e.IsScript && e.FilePath is not null)
        {
            if (!e.FileExists)
            {
                // Leftover Windows tasks pointing into System32/DriverStore are harmless and not actionable.
                if (Path.IsPathFullyQualified(e.FilePath) && !PathSafety.IsStrictlyInside(e.FilePath, WindowsDirectory))
                    reasons.Add(MissingTarget);
            }
            else if (e.Signature == SignatureState.Unsigned && IsInUserWritableLocation(e.FilePath, roots))
            {
                reasons.Add(UnsignedUserFolder);
            }
        }

        if (e.Amsi == AmsiVerdict.Malware) reasons.Add(AmsiDetected);

        return (reasons, SeverityOf(reasons));
    }

    public static SuspicionSeverity SeverityOf(IEnumerable<string> reasons)
    {
        var severity = SuspicionSeverity.Low;
        foreach (var reason in reasons)
        {
            if (HighReasons.Contains(reason)) return SuspicionSeverity.High;
            if (MediumReasons.Contains(reason)) severity = SuspicionSeverity.Medium;
        }

        return severity;
    }
}
