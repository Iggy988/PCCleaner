using System.IO;
using System.Security;

namespace PCCleaner.Services.Cleaning.Temp;

/// <summary>
/// Resolves the temporary-file folders from Windows environment variables (%WINDIR%, %LOCALAPPDATA%), with the
/// shell's known folders as fallback. Nothing is hardcoded: Windows may live on any drive, under any user name.
/// Every result is validated; a location that can't be resolved safely is reported and never cleaned.
/// </summary>
public sealed class TempLocationResolver
{
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<Environment.SpecialFolder, string> _getFolderPath;

    public TempLocationResolver()
        : this(Environment.GetEnvironmentVariable, Environment.GetFolderPath)
    {
    }

    /// <summary>Test seam: supply fake environment variables and known folders.</summary>
    public TempLocationResolver(Func<string, string?> getEnvironmentVariable, Func<Environment.SpecialFolder, string> getFolderPath)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
        _getFolderPath = getFolderPath;
    }

    public IReadOnlyList<TempLocation> ResolveAll(TimeSpan tempMinAge) =>
    [
        Resolve(TempLocationKind.UserTemp, tempMinAge),
        Resolve(TempLocationKind.WindowsTemp, tempMinAge),
        Resolve(TempLocationKind.Prefetch, tempMinAge),
    ];

    public TempLocation Resolve(TempLocationKind kind, TimeSpan tempMinAge = default)
    {
        var (baseDir, leaf) = kind switch
        {
            TempLocationKind.UserTemp => (ResolveBase(["LOCALAPPDATA"], Environment.SpecialFolder.LocalApplicationData), "Temp"),
            TempLocationKind.WindowsTemp => (ResolveBase(["WINDIR", "SystemRoot"], Environment.SpecialFolder.Windows), "Temp"),
            _ => (ResolveBase(["WINDIR", "SystemRoot"], Environment.SpecialFolder.Windows), "Prefetch"),
        };

        if (baseDir is null)
            return new TempLocation(kind, null, "The base folder could not be resolved from the environment.");

        var path = Path.Combine(baseDir, leaf);
        if (!TryValidateLocation(path, leaf, out var error))
            return new TempLocation(kind, null, error);

        return new TempLocation(kind, path)
        {
            MinAge = kind == TempLocationKind.Prefetch ? TimeSpan.Zero : tempMinAge,
            // Self-extracting .NET apps (including this one) cache their native files here.
            ExcludedPaths = kind == TempLocationKind.UserTemp ? [Path.Combine(path, ".net")] : [],
        };
    }

    private string? ResolveBase(string[] variables, Environment.SpecialFolder fallback)
    {
        foreach (var name in variables)
        {
            if (IsValidBaseDirectory(_getEnvironmentVariable(name), out var normalized)) return normalized;
        }

        return IsValidBaseDirectory(SafeGetFolderPath(fallback), out var folder) ? folder : null;
    }

    private string? SafeGetFolderPath(Environment.SpecialFolder folder)
    {
        try
        {
            return _getFolderPath(folder);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// A base directory must be a plain, fully-qualified local path (no UNC/device paths, no relative or ".." segments)
    /// and must not be a drive root.
    /// </summary>
    internal static bool IsValidBaseDirectory(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal)) return false; // UNC or \\?\ device path
        if (!Path.IsPathFullyQualified(trimmed)) return false;
        if (trimmed.Split('\\', '/').Any(segment => segment is ".." or ".")) return false;

        string full;
        try
        {
            full = PathSafety.Normalize(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (string.Equals(Path.GetPathRoot(full)?.TrimEnd('\\'), full.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;

        normalized = full;
        return true;
    }

    internal static bool TryValidateLocation(string path, string expectedLeaf, out string? error)
    {
        error = null;
        if (!string.Equals(Path.GetFileName(path), expectedLeaf, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unexpected folder name: {path}";
            return false;
        }

        try
        {
            // The folder itself must not be a junction or symlink pointing somewhere else.
            var info = new DirectoryInfo(path);
            if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                error = $"{path} is a link to another location.";
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Can't read attributes (e.g. no rights): scanning will report AccessDenied.
        }

        return true;
    }
}
