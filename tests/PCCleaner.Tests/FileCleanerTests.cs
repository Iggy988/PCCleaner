using System.Diagnostics;
using System.IO;
using PCCleaner.Models;
using PCCleaner.Services.Cleaning;

namespace PCCleaner.Tests;

public sealed class FileCleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PCCleanerTests", Guid.NewGuid().ToString("N"));
    private readonly string _target;

    public FileCleanerTests()
    {
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_target);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void Clean_DeletesFilesAndEmptySubfolders_ButKeepsRootFolder()
    {
        CreateFile("a.tmp", 100);
        CreateFile(@"sub\b.tmp", 200);
        CreateFile(@"sub\deeper\c.tmp", 300);

        var result = FileCleaner.Clean([Target()], null, CancellationToken.None);

        Assert.Equal(600, result.BytesFreed);
        Assert.Equal(3, result.FilesDeleted);
        Assert.Equal(0, result.FilesSkipped);
        Assert.True(Directory.Exists(_target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_target));
    }

    [Fact]
    public void Scan_ReportsTotalSizeAndCount_WithoutDeleting()
    {
        CreateFile("a.tmp", 1000);
        CreateFile(@"sub\b.tmp", 24);

        var result = FileCleaner.Scan([Target()], CancellationToken.None);

        Assert.Equal(1024, result.Bytes);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(2, Directory.GetFiles(_target, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void MinAge_SkipsRecentFiles()
    {
        var oldFile = CreateFile("old.tmp", 10, age: TimeSpan.FromDays(3));
        var newFile = CreateFile("new.tmp", 10);

        var result = FileCleaner.Clean([Target(minAge: TimeSpan.FromHours(24))], null, CancellationToken.None);

        Assert.Equal(1, result.FilesDeleted);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }

    [Fact]
    public void MinAge_UsesCreationTime_ForFilesWithOldWriteTime()
    {
        // e.g. a file just extracted by an installer keeps its original (old) modified date.
        var path = CreateFile("extracted.dll", 10);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddYears(-1));

        var result = FileCleaner.Clean([Target(minAge: TimeSpan.FromHours(24))], null, CancellationToken.None);

        Assert.Equal(0, result.FilesDeleted);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SearchPattern_NonRecursive_OnlyMatchesTopLevelFiles()
    {
        var match = CreateFile("APP-1234.pf", 10);
        var other = CreateFile("layout.ini", 10);
        var nested = CreateFile(@"sub\NESTED.pf", 10);

        var target = new FolderTarget { Path = _target, SearchPattern = "*.pf", Recursive = false };
        FileCleaner.Clean([target], null, CancellationToken.None);

        Assert.False(File.Exists(match));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void LockedFile_IsSkippedAndCounted()
    {
        var locked = CreateFile("locked.tmp", 50);
        CreateFile("free.tmp", 50);

        CleanResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = FileCleaner.Clean([Target()], null, CancellationToken.None);
        }

        Assert.Equal(1, result.FilesDeleted);
        Assert.Equal(1, result.FilesSkipped);
        Assert.Equal(50, result.BytesFreed);
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void ReadOnlyFile_IsDeleted()
    {
        var path = CreateFile("readonly.tmp", 10);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var result = FileCleaner.Clean([Target()], null, CancellationToken.None);

        Assert.Equal(1, result.FilesDeleted);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Junction_IsNeverFollowed()
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var precious = Path.Combine(outside, "precious.txt");
        File.WriteAllText(precious, "do not delete");

        var junction = Path.Combine(_target, "link");
        CreateJunction(junction, outside);
        Assert.True(File.Exists(Path.Combine(junction, "precious.txt")));

        var scan = FileCleaner.Scan([Target()], CancellationToken.None);
        FileCleaner.Clean([Target()], null, CancellationToken.None);

        Assert.Equal(0, scan.FileCount);
        Assert.True(File.Exists(precious));

        RemoveJunction(junction);
    }

    [Fact]
    public void ExcludedPath_IsNotTouched()
    {
        var kept = CreateFile(@".net\app\native.dll", 10);
        var removed = CreateFile("junk.tmp", 10);

        var target = new FolderTarget { Path = _target, ExcludedPaths = [Path.Combine(_target, ".net")] };
        FileCleaner.Clean([target], null, CancellationToken.None);

        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(removed));
    }

    [Fact]
    public void MissingFolder_ReturnsEmptyResults()
    {
        var target = new FolderTarget { Path = Path.Combine(_root, "does-not-exist") };

        Assert.Equal(0, FileCleaner.Scan([target], CancellationToken.None).FileCount);
        Assert.Equal(0, FileCleaner.Clean([target], null, CancellationToken.None).FilesDeleted);
    }

    [Fact]
    public void Cancellation_StopsScan()
    {
        CreateFile("a.tmp", 10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => FileCleaner.Scan([Target()], cts.Token));
    }

    private FolderTarget Target(TimeSpan? minAge = null) => new() { Path = _target, MinAge = minAge ?? TimeSpan.Zero };

    private string CreateFile(string relativePath, int size, TimeSpan? age = null)
    {
        var path = Path.Combine(_target, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        if (age is { } a)
        {
            var time = DateTime.UtcNow - a;
            File.SetCreationTimeUtc(path, time);
            File.SetLastWriteTimeUtc(path, time);
        }

        return path;
    }

    private static void CreateJunction(string link, string target) => RunCmd($"mklink /J \"{link}\" \"{target}\"");

    // rmdir removes only the link itself, never the target's contents.
    private static void RemoveJunction(string link) => RunCmd($"rmdir \"{link}\"");

    private static void RunCmd(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {arguments}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
