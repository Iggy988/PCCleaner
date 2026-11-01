using System.IO;
using PCCleaner.Models;
using PCCleaner.Services.Cleaning;

namespace PCCleaner.Tests;

public sealed class PathSafetyTests : IDisposable
{
    private readonly string _root = TestFileSystem.NewRoot();
    private readonly string _target;

    public PathSafetyTests()
    {
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_target);
    }

    public void Dispose() => TestFileSystem.TryDeleteTree(_root);

    [Theory]
    [InlineData(@"C:\Temp\a.tmp", @"C:\Temp", true)]
    [InlineData(@"C:\Temp\sub\a.tmp", @"C:\Temp\", true)]
    [InlineData(@"C:\TEMP\A.TMP", @"c:\temp", true)]
    [InlineData(@"C:\Temp", @"C:\Temp", false)]               // the root itself
    [InlineData(@"C:\Temp2\a.tmp", @"C:\Temp", false)]        // prefix trick
    [InlineData(@"C:\Temp\..\Windows\a.dll", @"C:\Temp", false)] // traversal
    [InlineData(@"C:\Other\a.tmp", @"C:\Temp", false)]
    public void IsStrictlyInside_HandlesPrefixesAndTraversal(string candidate, string root, bool expected) =>
        Assert.Equal(expected, PathSafety.IsStrictlyInside(candidate, root));

    [Fact]
    public void RegularFile_IsSafe()
    {
        var file = new FileInfo(TestFileSystem.CreateFile(_target, @"sub\a.tmp", 10));

        Assert.True(PathSafety.IsSafeToDelete(file, Resolved(), out var vanished));
        Assert.False(vanished);
    }

    [Fact]
    public void DeletedFile_IsReportedAsVanished()
    {
        var file = new FileInfo(TestFileSystem.CreateFile(_target, "a.tmp", 10));
        File.Delete(file.FullName);

        Assert.False(PathSafety.IsSafeToDelete(file, Resolved(), out var vanished));
        Assert.True(vanished);
    }

    [Fact]
    public void FileTouchedAfterScan_FailsTheAgeRecheck()
    {
        var path = TestFileSystem.CreateFile(_target, "a.tmp", 10, age: TimeSpan.FromDays(3));
        var file = new FileInfo(path);
        var target = Resolved(minAge: TimeSpan.FromHours(24));
        Assert.True(PathSafety.IsSafeToDelete(file, target, out _));

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

        Assert.False(PathSafety.IsSafeToDelete(file, target, out var vanished));
        Assert.False(vanished);
    }

    [Fact]
    public void FolderSwappedForJunction_AfterScan_IsRejected()
    {
        // Scan saw target\sub\a.tmp; before deletion "sub" is replaced by a junction pointing outside the target.
        var file = new FileInfo(TestFileSystem.CreateFile(_target, @"sub\a.tmp", 10));
        var outside = Path.Combine(_root, "outside");
        TestFileSystem.CreateFile(outside, "a.tmp", 10);
        Directory.Delete(Path.Combine(_target, "sub"), recursive: true);
        TestFileSystem.CreateJunction(Path.Combine(_target, "sub"), outside);
        try
        {
            Assert.False(PathSafety.IsSafeToDelete(file, Resolved(), out _));
            Assert.Equal(FileCleaner.DeleteOutcome.Unsafe, FileCleaner.TryDelete(file, Resolved()));
            Assert.True(File.Exists(Path.Combine(outside, "a.tmp")));
        }
        finally
        {
            TestFileSystem.RemoveJunction(Path.Combine(_target, "sub"));
        }
    }

    [Fact]
    public void FileOutsideTarget_IsRejected()
    {
        var outside = new FileInfo(TestFileSystem.CreateFile(_root, "elsewhere.tmp", 10));

        Assert.False(PathSafety.IsSafeToDelete(outside, Resolved(), out _));
    }

    [Fact]
    public void NonRecursiveTarget_RejectsNestedFiles_AndPatternMismatch()
    {
        var nested = new FileInfo(TestFileSystem.CreateFile(_target, @"sub\APP.pf", 10));
        var other = new FileInfo(TestFileSystem.CreateFile(_target, "Layout.ini", 10));
        var match = new FileInfo(TestFileSystem.CreateFile(_target, "APP-1234.pf", 10));
        var target = ResolvedTarget.Create(new FolderTarget { Path = _target, SearchPattern = "*.pf", Recursive = false });

        Assert.False(PathSafety.IsSafeToDelete(nested, target, out _));
        Assert.False(PathSafety.IsSafeToDelete(other, target, out _));
        Assert.True(PathSafety.IsSafeToDelete(match, target, out _));
    }

    [Fact]
    public void ExcludedFolder_IsRejected()
    {
        var kept = new FileInfo(TestFileSystem.CreateFile(_target, @".net\app\native.dll", 10));
        var target = ResolvedTarget.Create(new FolderTarget { Path = _target, ExcludedPaths = [Path.Combine(_target, ".net")] });

        Assert.False(PathSafety.IsSafeToDelete(kept, target, out _));
    }

    private ResolvedTarget Resolved(TimeSpan? minAge = null) =>
        ResolvedTarget.Create(new FolderTarget { Path = _target, MinAge = minAge ?? TimeSpan.Zero });
}
