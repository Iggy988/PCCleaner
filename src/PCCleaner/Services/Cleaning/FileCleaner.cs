using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using PCCleaner.Models;
using PCCleaner.Services.Native;

namespace PCCleaner.Services.Cleaning;

/// <summary>
/// The deletion engine shared by all folder-based cleanup categories.
/// Safety rules: deletes files only, never follows junctions/symlinks, never deletes a target's root folder
/// (empty sub-folders are removed one at a time, non-recursively), honors <see cref="FolderTarget.MinAge"/> and
/// excluded paths, re-validates every file right before deleting it, and skips (counts) files it cannot delete.
/// </summary>
public static class FileCleaner
{
    internal const int MaxRecordedSkips = 200;

    private static readonly EnumerationOptions ListOptions = new()
    {
        // Inaccessible folders are counted (and reported) instead of silently ignored.
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        RecurseSubdirectories = false,
        MatchType = MatchType.Win32,
    };

    internal enum DeleteOutcome
    {
        Deleted,
        AlreadyGone,
        InUse,
        AccessDenied,
        Unsafe,
        Failed,
    }

    internal sealed class EnumerationStats
    {
        public int Inaccessible { get; set; }

        public bool RootAccessDenied { get; set; }
    }

    /// <param name="probeLocks">Also check whether each file can be opened for deletion (slower; reports locked files).</param>
    public static ScanResult Scan(IEnumerable<FolderTarget> targets, CancellationToken cancellationToken, bool probeLocks = false)
    {
        long bytes = 0, lockedBytes = 0;
        int count = 0, locked = 0;
        var stats = new EnumerationStats();

        foreach (var target in targets)
        {
            foreach (var file in EnumerateFiles(ResolvedTarget.Create(target), cancellationToken, stats))
            {
                if (probeLocks)
                {
                    switch (ProbeDelete(file.FullName))
                    {
                        case DeleteOutcome.InUse:
                            locked++;
                            lockedBytes += file.Length;
                            continue;
                        case DeleteOutcome.AccessDenied:
                            stats.Inaccessible++;
                            continue;
                        case DeleteOutcome.AlreadyGone:
                            continue;
                    }
                }

                bytes += file.Length;
                count++;
            }
        }

        return new ScanResult(bytes, count)
        {
            LockedFiles = locked,
            LockedBytes = lockedBytes,
            InaccessibleItems = stats.Inaccessible,
            RootAccessDenied = stats.RootAccessDenied,
        };
    }

    /// <param name="progress">Receives running counters (throttled to ~10 updates per second, plus a final one).</param>
    public static CleanResult Clean(IEnumerable<FolderTarget> targets, IProgress<CleanProgress>? progress, CancellationToken cancellationToken)
    {
        long freed = 0;
        int processed = 0, deleted = 0, inUse = 0, denied = 0, unsafeCount = 0, failed = 0, gone = 0;
        var skippedItems = new List<SkippedFile>();
        var throttle = Stopwatch.StartNew();

        void Report() => progress?.Report(new CleanProgress(processed, deleted, inUse + denied + unsafeCount + failed, freed));

        void Skip(FileInfo file, SkipReason reason)
        {
            if (skippedItems.Count < MaxRecordedSkips) skippedItems.Add(new SkippedFile(file.FullName, reason));
        }

        foreach (var target in targets)
        {
            var resolved = ResolvedTarget.Create(target);
            foreach (var file in EnumerateFiles(resolved, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;

                var length = file.Length;
                switch (TryDelete(file, resolved))
                {
                    case DeleteOutcome.Deleted:
                        freed += length;
                        deleted++;
                        break;
                    case DeleteOutcome.AlreadyGone:
                        gone++;
                        break;
                    case DeleteOutcome.InUse:
                        inUse++;
                        Skip(file, SkipReason.InUse);
                        break;
                    case DeleteOutcome.AccessDenied:
                        denied++;
                        Skip(file, SkipReason.AccessDenied);
                        break;
                    case DeleteOutcome.Unsafe:
                        unsafeCount++;
                        Skip(file, SkipReason.Unsafe);
                        break;
                    default:
                        failed++;
                        Skip(file, SkipReason.Error);
                        break;
                }

                if (throttle.ElapsedMilliseconds >= 100)
                {
                    Report();
                    throttle.Restart();
                }
            }

            if (target.Recursive)
            {
                var root = new DirectoryInfo(resolved.Root);
                if (root.Exists && !root.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    RemoveEmptyDirectories(root, isRoot: true, resolved, cancellationToken);
            }
        }

        Report();
        return new CleanResult(freed, deleted, inUse + denied + unsafeCount + failed)
        {
            SkippedInUse = inUse,
            SkippedAccessDenied = denied,
            SkippedUnsafe = unsafeCount,
            Failed = failed,
            AlreadyGone = gone,
            SkippedItems = skippedItems,
        };
    }

    internal static IEnumerable<FileInfo> EnumerateFiles(FolderTarget target, CancellationToken cancellationToken) =>
        EnumerateFiles(ResolvedTarget.Create(target), cancellationToken);

    internal static IEnumerable<FileInfo> EnumerateFiles(ResolvedTarget target, CancellationToken cancellationToken, EnumerationStats? stats = null)
    {
        var root = new DirectoryInfo(target.Root);
        // A missing root, or a root that is itself a junction/symlink, yields nothing.
        if (!root.Exists || root.Attributes.HasFlag(FileAttributes.ReparsePoint)) yield break;

        var options = target.Target;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            FileInfo[] files;
            try
            {
                files = dir.GetFiles(options.SearchPattern, ListOptions);
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                if (stats is not null)
                {
                    if (ex is UnauthorizedAccessException or SecurityException)
                    {
                        stats.Inaccessible++;
                        if (dir == root) stats.RootAccessDenied = true;
                    }
                }

                continue;
            }

            foreach (var file in files)
            {
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (options.MinAge > TimeSpan.Zero && PathSafety.IsNewerThan(file, target.CutoffUtc)) continue;
                yield return file;
            }

            if (!options.Recursive) continue;

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
                if (target.Excluded.Contains(PathSafety.Normalize(sub.FullName))) continue;
                pending.Push(sub);
            }
        }
    }

    internal static DeleteOutcome TryDelete(FileInfo file, ResolvedTarget target)
    {
        if (!PathSafety.IsSafeToDelete(file, target, out var vanished))
            return vanished ? DeleteOutcome.AlreadyGone : DeleteOutcome.Unsafe;

        var clearedReadOnly = false;
        try
        {
            if (file.IsReadOnly)
            {
                file.IsReadOnly = false;
                clearedReadOnly = true;
            }

            file.Delete();
            return DeleteOutcome.Deleted;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return DeleteOutcome.AlreadyGone;
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            if (clearedReadOnly) RestoreReadOnly(file);
            return ex switch
            {
                UnauthorizedAccessException or SecurityException => DeleteOutcome.AccessDenied,
                IOException io when IsSharingViolation(io) => DeleteOutcome.InUse,
                _ => DeleteOutcome.Failed,
            };
        }
    }

    /// <summary>Checks whether a file could be opened for deletion right now, without changing it.</summary>
    internal static DeleteOutcome ProbeDelete(string path)
    {
        using var handle = NativeMethods.CreateFile(path, NativeMethods.DELETE,
            FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (!handle.IsInvalid) return DeleteOutcome.Deleted;

        return new Win32Exception().NativeErrorCode switch
        {
            NativeMethods.ERROR_SHARING_VIOLATION or NativeMethods.ERROR_LOCK_VIOLATION => DeleteOutcome.InUse,
            NativeMethods.ERROR_ACCESS_DENIED => DeleteOutcome.AccessDenied,
            NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND => DeleteOutcome.AlreadyGone,
            _ => DeleteOutcome.Failed,
        };
    }

    private static void RemoveEmptyDirectories(DirectoryInfo dir, bool isRoot, ResolvedTarget target, CancellationToken cancellationToken)
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
            if (target.Excluded.Contains(PathSafety.Normalize(sub.FullName))) continue;
            RemoveEmptyDirectories(sub, isRoot: false, target, cancellationToken);
        }

        if (isRoot) return;

        try
        {
            dir.Refresh();
            if (!PathSafety.IsStrictlyInside(dir.FullName, target.Root)) return;
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            if (dir.CreationTimeUtc > target.CutoffUtc) return;
            if (dir.EnumerateFileSystemInfos("*", ListOptions).Any()) return;
            dir.Attributes = FileAttributes.Directory;
            dir.Delete(recursive: false);
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            // In use or no permission: leave it.
        }
    }

    private static void RestoreReadOnly(FileInfo file)
    {
        try { file.IsReadOnly = true; }
        catch (Exception ex) when (IsFileSystemError(ex)) { }
    }

    private static bool IsSharingViolation(IOException ex) =>
        (ex.HResult & 0xFFFF) is NativeMethods.ERROR_SHARING_VIOLATION or NativeMethods.ERROR_LOCK_VIOLATION;

    private static bool IsFileSystemError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException;
}
