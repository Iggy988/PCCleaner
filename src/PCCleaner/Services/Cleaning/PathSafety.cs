using System.IO;
using System.IO.Enumeration;
using System.Security;
using PCCleaner.Models;

namespace PCCleaner.Services.Cleaning;

/// <summary>A <see cref="FolderTarget"/> with its root resolved and normalized once, used for enumeration and re-validation.</summary>
public sealed class ResolvedTarget
{
    private ResolvedTarget(FolderTarget target)
    {
        Target = target;
        Root = PathSafety.Normalize(Environment.ExpandEnvironmentVariables(target.Path));
        CutoffUtc = DateTime.UtcNow - target.MinAge;
        Excluded = target.ExcludedPaths
            .Select(p => PathSafety.Normalize(Environment.ExpandEnvironmentVariables(p)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public FolderTarget Target { get; }

    public string Root { get; }

    public DateTime CutoffUtc { get; }

    public IReadOnlySet<string> Excluded { get; }

    public static ResolvedTarget Create(FolderTarget target) => new(target);
}

/// <summary>
/// Path checks that every deletion goes through. Files are re-validated immediately before deletion, so a file that
/// was swapped for a link, moved, or touched again since the scan is left alone.
/// </summary>
public static class PathSafety
{
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>True if <paramref name="candidate"/> is below <paramref name="root"/> (not the root itself). <c>C:\Temp2</c> is not inside <c>C:\Temp</c>.</summary>
    public static bool IsStrictlyInside(string candidate, string root)
    {
        var c = Normalize(candidate);
        var r = Normalize(root) + Path.DirectorySeparatorChar;
        return c.Length > r.Length && c.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True if the root or any folder between it and <paramref name="filePath"/> is a junction/symlink, or can't be checked.
    /// </summary>
    public static bool HasReparsePointBetween(string filePath, string root)
    {
        var normalizedRoot = Normalize(root);
        var dir = Path.GetDirectoryName(Normalize(filePath));
        try
        {
            while (dir is not null)
            {
                if (File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint)) return true;
                if (string.Equals(dir, normalizedRoot, StringComparison.OrdinalIgnoreCase)) return false;
                if (!IsStrictlyInside(dir, normalizedRoot)) return true;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return true;
        }

        return true;
    }

    /// <summary>
    /// The last check before deleting <paramref name="file"/>: it must still exist, be a regular file inside the target,
    /// match the target's pattern/depth/age rules, and not be reached through a link.
    /// </summary>
    /// <param name="vanished">Set when the file no longer exists (not a safety problem).</param>
    public static bool IsSafeToDelete(FileInfo file, ResolvedTarget target, out bool vanished)
    {
        vanished = false;
        try
        {
            file.Refresh();
            if (!file.Exists)
            {
                vanished = true;
                return false;
            }

            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.Attributes.HasFlag(FileAttributes.Directory)) return false;

            var full = Normalize(file.FullName);
            if (!IsStrictlyInside(full, target.Root)) return false;

            var parent = Path.GetDirectoryName(full);
            if (!target.Target.Recursive && !string.Equals(parent, target.Root, StringComparison.OrdinalIgnoreCase)) return false;
            if (!FileSystemName.MatchesWin32Expression(target.Target.SearchPattern, file.Name)) return false;
            if (target.Excluded.Any(excluded => IsStrictlyInside(full, excluded))) return false;
            if (target.Target.MinAge > TimeSpan.Zero && IsNewerThan(file, target.CutoffUtc)) return false;

            return !HasReparsePointBetween(full, target.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return false;
        }
    }

    // Extracted/copied files can keep an old LastWriteTime, so the creation time counts too.
    internal static bool IsNewerThan(FileSystemInfo item, DateTime cutoffUtc) =>
        item.LastWriteTimeUtc > cutoffUtc || item.CreationTimeUtc > cutoffUtc;
}
