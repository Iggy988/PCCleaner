using PCCleaner.Models;

namespace PCCleaner.Services.Cleaning.Temp;

public enum TempLocationKind
{
    /// <summary>%LOCALAPPDATA%\Temp</summary>
    UserTemp,

    /// <summary>%WINDIR%\Temp</summary>
    WindowsTemp,

    /// <summary>%WINDIR%\Prefetch (*.pf only)</summary>
    Prefetch,
}

public enum TempLocationStatus
{
    Ok,

    /// <summary>The folder doesn't exist (nothing to clean).</summary>
    Missing,

    /// <summary>The folder can't be listed with the current rights (run as administrator).</summary>
    AccessDenied,

    /// <summary>The path couldn't be resolved safely from the environment; the location is never touched.</summary>
    ResolveError,
}

/// <summary>One of the three temporary-file locations, resolved from environment variables.</summary>
public sealed record TempLocation(TempLocationKind Kind, string? Path, string? ResolveError = null)
{
    public bool IsResolved => Path is not null;

    /// <summary>Windows only lets administrators delete files in %WINDIR%\Temp and Prefetch.</summary>
    public bool RequiresAdmin => Kind != TempLocationKind.UserTemp;

    /// <summary>Only Prefetch trace files; Layout.ini, ReadyBoot and the SysMain databases are left alone.</summary>
    public string SearchPattern => Kind == TempLocationKind.Prefetch ? "*.pf" : "*";

    public bool Recursive => Kind != TempLocationKind.Prefetch;

    /// <summary>Files newer than this are skipped (from the "skip recent temp files" setting; not used for Prefetch).</summary>
    public TimeSpan MinAge { get; init; }

    public IReadOnlyCollection<string> ExcludedPaths { get; init; } = [];

    public FolderTarget ToFolderTarget() => new()
    {
        Path = Path ?? throw new InvalidOperationException($"{Kind} could not be resolved: {ResolveError}"),
        SearchPattern = SearchPattern,
        Recursive = Recursive,
        MinAge = MinAge,
        ExcludedPaths = ExcludedPaths,
    };
}
