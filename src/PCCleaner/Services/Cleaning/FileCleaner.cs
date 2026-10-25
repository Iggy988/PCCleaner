using System.Diagnostics;
using System.IO;
using System.Security;
using PCCleaner.Models;

namespace PCCleaner.Services.Cleaning;

/// <summary>
/// The deletion engine shared by all folder-based cleanup categories.
/// Safety rules: never follows junctions/symlinks, never deletes a target's root folder,
/// honors <see cref="FolderTarget.MinAge"/> and excluded paths, and skips (counts) files it cannot delete.
/// </summary>
public static class FileCleaner
{
    private static readonly EnumerationOptions ListOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        RecurseSubdirectories = false,
        MatchType = MatchType.Win32,
    };

    public static ScanResult Scan(IEnumerable<FolderTarget> targets, CancellationToken cancellationToken)
    {
        long bytes = 0;
        var count = 0;
        foreach (var target in targets)
        {
            foreach (var file in EnumerateFiles(target, cancellationToken))
            {
                bytes += file.Length;
                count++;
            }
        }

        return new ScanResult(bytes, count);
    }

    /// <param name="progress">Receives the running total of bytes freed (throttled to ~10 updates per second).</param>
    public static CleanResult Clean(IEnumerable<FolderTarget> targets, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        long freed = 0;
        int deleted = 0, skipped = 0;
        var throttle = Stopwatch.StartNew();

        foreach (var target in targets)
        {
            foreach (var file in EnumerateFiles(target, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var length = file.Length;
                if (TryDelete(file))
                {
                    freed += length;
                    deleted++;
                }
                else
                {
                    skipped++;
                }

                if (throttle.ElapsedMilliseconds >= 100)
                {
                    progress?.Report(freed);
                    throttle.Restart();
                }
            }

            if (target.Recursive)
            {
                var root = new DirectoryInfo(Environment.ExpandEnvironmentVariables(target.Path));
                if (root.Exists)
                    RemoveEmptyDirectories(root, isRoot: true, DateTime.UtcNow - target.MinAge, Excluded(target), cancellationToken);
            }
        }

        progress?.Report(freed);
        return new CleanResult(freed, deleted, skipped);
    }

    internal static IEnumerable<FileInfo> EnumerateFiles(FolderTarget target, CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(Environment.ExpandEnvironmentVariables(target.Path));
        if (!root.Exists) yield break;

        var cutoff = DateTime.UtcNow - target.MinAge;
        var excluded = Excluded(target);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            FileInfo[] files;
            try
            {
                files = dir.GetFiles(target.SearchPattern, ListOptions);
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                continue;
            }

            foreach (var file in files)
            {
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (target.MinAge > TimeSpan.Zero && IsNewerThan(file, cutoff)) continue;
                yield return file;
            }

            if (!target.Recursive) continue;

            DirectoryInfo[] subDirs;
            try
            {
                subDirs = dir.GetDirectories("*", ListOptions);
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (excluded.Contains(Normalize(sub.FullName))) continue;
                pending.Push(sub);
            }
        }
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            if (file.IsReadOnly) file.IsReadOnly = false;
            file.Delete();
            return true;
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            return false;
        }
    }

    private static void RemoveEmptyDirectories(DirectoryInfo dir, bool isRoot, DateTime cutoff,
        HashSet<string> excluded, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo[] subDirs;
        try
        {
            subDirs = dir.GetDirectories("*", ListOptions);
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            return;
        }

        foreach (var sub in subDirs)
        {
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            if (excluded.Contains(Normalize(sub.FullName))) continue;
            RemoveEmptyDirectories(sub, isRoot: false, cutoff, excluded, cancellationToken);
        }

        if (isRoot) return;

        try
        {
            dir.Refresh();
            if (dir.CreationTimeUtc > cutoff) return;
            if (dir.EnumerateFileSystemInfos("*", ListOptions).Any()) return;
            dir.Attributes = FileAttributes.Directory;
            dir.Delete(recursive: false);
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            // In use or no permission: leave it.
        }
    }

    // Extracted/copied files can keep an old LastWriteTime, so the creation time counts too.
    private static bool IsNewerThan(FileInfo file, DateTime cutoffUtc) =>
        file.LastWriteTimeUtc > cutoffUtc || file.CreationTimeUtc > cutoffUtc;

    private static HashSet<string> Excluded(FolderTarget target) =>
        target.ExcludedPaths.Select(p => Normalize(Environment.ExpandEnvironmentVariables(p))).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsFileSystemError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException;
}
