namespace PCCleaner.Models;

public enum CleanupGroup
{
    System,
    Browsers,
    Applications,
}

public sealed record ScanResult(long Bytes, int FileCount)
{
    public static readonly ScanResult Empty = new(0, 0);

    /// <summary>Files that match but are open by another process without delete sharing (only when locks are probed).</summary>
    public int LockedFiles { get; init; }

    /// <summary>Bytes of <see cref="LockedFiles"/>; already excluded from <see cref="Bytes"/>.</summary>
    public long LockedBytes { get; init; }

    /// <summary>Folders or files that could not be read or opened because of permissions.</summary>
    public int InaccessibleItems { get; init; }

    /// <summary>True when the target folder itself could not be listed (typically needs administrator rights).</summary>
    public bool RootAccessDenied { get; init; }
}

public enum SkipReason
{
    /// <summary>Open by another process without delete sharing.</summary>
    InUse,

    /// <summary>The account has no permission to delete it.</summary>
    AccessDenied,

    /// <summary>Failed the safety re-check right before deletion (became a link, moved outside the folder, became too new...).</summary>
    Unsafe,

    /// <summary>Any other file-system error.</summary>
    Error,
}

public sealed record SkippedFile(string Path, SkipReason Reason);

public sealed record CleanResult(long BytesFreed, int FilesDeleted, int FilesSkipped)
{
    public static readonly CleanResult Empty = new(0, 0, 0);

    public int SkippedInUse { get; init; }

    public int SkippedAccessDenied { get; init; }

    public int SkippedUnsafe { get; init; }

    public int Failed { get; init; }

    /// <summary>Files that disappeared between scanning and cleaning (e.g. removed by the app that made them). Not an error.</summary>
    public int AlreadyGone { get; init; }

    /// <summary>A capped sample of skipped files (paths only) for display.</summary>
    public IReadOnlyList<SkippedFile> SkippedItems { get; init; } = [];

    public bool IsPartial => FilesSkipped > 0;
}

/// <summary>Live counters reported while cleaning.</summary>
public readonly record struct CleanProgress(int Processed, int Deleted, int Skipped, long BytesFreed);

/// <summary>One folder (or file pattern inside a folder) to clean.</summary>
public sealed class FolderTarget
{
    public required string Path { get; init; }

    public string SearchPattern { get; init; } = "*";

    public bool Recursive { get; init; } = true;

    /// <summary>Files (and empty folders) created or modified more recently than this are left alone.</summary>
    public TimeSpan MinAge { get; init; } = TimeSpan.Zero;

    /// <summary>Full paths of sub-folders that must never be touched.</summary>
    public IReadOnlyCollection<string> ExcludedPaths { get; init; } = [];
}
