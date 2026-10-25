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
}

public sealed record CleanResult(long BytesFreed, int FilesDeleted, int FilesSkipped)
{
    public static readonly CleanResult Empty = new(0, 0, 0);
}

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
