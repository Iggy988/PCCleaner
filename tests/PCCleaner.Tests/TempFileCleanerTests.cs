using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using PCCleaner.Models;
using PCCleaner.Services.Cleaning;
using PCCleaner.Services.Cleaning.Temp;

namespace PCCleaner.Tests;

/// <summary>End-to-end tests of the temp-file cleaner against a throw-away folder that stands in for a Temp location.</summary>
public sealed class TempFileCleanerTests : IDisposable
{
    private readonly string _root = TestFileSystem.NewRoot();
    private readonly string _temp;
    private readonly List<Action> _cleanup = [];

    public TempFileCleanerTests()
    {
        _temp = Path.Combine(_root, "Temp");
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        foreach (var action in _cleanup) action();
        TestFileSystem.TryDeleteTree(_root);
    }

    [Fact]
    public void Scan_ReportsEligibleFilesAndBytes_WithoutDeleting()
    {
        TestFileSystem.CreateFile(_temp, "a.tmp", 1000);
        TestFileSystem.CreateFile(_temp, @"setup\b.log", 24);

        var result = TempFileCleaner.Scan(UserTemp(), CancellationToken.None);

        Assert.Equal(TempLocationStatus.Ok, result.Status);
        Assert.Equal(2, result.Scan.FileCount);
        Assert.Equal(1024, result.Scan.Bytes);
        Assert.Equal(2, Directory.GetFiles(_temp, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void LockedFile_IsReportedByScan_SkippedByClean_AndOthersStillDeleted()
    {
        var locked = TestFileSystem.CreateFile(_temp, "locked.tmp", 50);
        var free1 = TestFileSystem.CreateFile(_temp, "free1.tmp", 10);
        var free2 = TestFileSystem.CreateFile(_temp, @"sub\free2.tmp", 10);

        TempScanResult scan;
        CleanResult clean;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            scan = TempFileCleaner.Scan(UserTemp(), CancellationToken.None);
            clean = TempFileCleaner.Clean(UserTemp(), null, CancellationToken.None);
        }

        Assert.Equal(1, scan.Scan.LockedFiles);
        Assert.Equal(50, scan.Scan.LockedBytes);
        Assert.Equal(2, scan.Scan.FileCount); // reclaimable estimate excludes the locked file
        Assert.Equal(20, scan.Scan.Bytes);

        Assert.Equal(2, clean.FilesDeleted);
        Assert.Equal(1, clean.SkippedInUse);
        Assert.Equal(1, clean.FilesSkipped);
        Assert.True(clean.IsPartial);
        Assert.Equal(20, clean.BytesFreed);
        Assert.Contains(clean.SkippedItems, s => s.Path == locked && s.Reason == SkipReason.InUse);
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(free1));
        Assert.False(File.Exists(free2));
    }

    [Fact]
    public void AccessDenied_IsCounted_AndCleaningContinues()
    {
        var protectedDir = Path.Combine(_temp, "protected");
        var denied = TestFileSystem.CreateFile(protectedDir, "denied.tmp", 30);
        var free = TestFileSystem.CreateFile(_temp, "free.tmp", 10);
        // Deleting needs DELETE on the file or "delete child" on its folder, so deny both.
        Deny(new DirectoryInfo(protectedDir), FileSystemRights.DeleteSubdirectoriesAndFiles);
        Deny(new FileInfo(denied), FileSystemRights.Delete);

        var scan = TempFileCleaner.Scan(UserTemp(), CancellationToken.None);
        var clean = TempFileCleaner.Clean(UserTemp(), null, CancellationToken.None);

        Assert.Equal(1, scan.Scan.InaccessibleItems);
        Assert.Equal(1, scan.Scan.FileCount);
        Assert.Equal(1, clean.FilesDeleted);
        Assert.Equal(1, clean.SkippedAccessDenied);
        Assert.Contains(clean.SkippedItems, s => s.Path == denied && s.Reason == SkipReason.AccessDenied);
        Assert.True(File.Exists(denied));
        Assert.False(File.Exists(free));
    }

    [Fact]
    public void FileThatDisappearsBeforeDeletion_CountsAsAlreadyGone_NotAsError()
    {
        var path = TestFileSystem.CreateFile(_temp, "transient.tmp", 10);
        var target = ResolvedTarget.Create(UserTemp().ToFolderTarget());
        var seenByScan = FileCleaner.EnumerateFiles(target, CancellationToken.None).Single();

        File.Delete(path); // e.g. the app that created it cleaned up after itself

        Assert.Equal(FileCleaner.DeleteOutcome.AlreadyGone, FileCleaner.TryDelete(seenByScan, target));
    }

    [Fact]
    public void MissingFolder_IsReported_AndCleaningIsANoOp()
    {
        var missing = new TempLocation(TempLocationKind.UserTemp, Path.Combine(_root, "NoSuchTemp"));

        Assert.Equal(TempLocationStatus.Missing, TempFileCleaner.Scan(missing, CancellationToken.None).Status);
        Assert.Equal(0, TempFileCleaner.Clean(missing, null, CancellationToken.None).FilesDeleted);
    }

    [Fact]
    public void UnresolvedLocation_IsNeverTouched()
    {
        var unresolved = new TempLocation(TempLocationKind.WindowsTemp, null, "bad WINDIR");

        Assert.Equal(TempLocationStatus.ResolveError, TempFileCleaner.Scan(unresolved, CancellationToken.None).Status);
        Assert.Same(CleanResult.Empty, TempFileCleaner.Clean(unresolved, null, CancellationToken.None));
    }

    [Fact]
    public void RootFolder_IsNeverDeleted_OnlyFilesAndEmptySubfolders()
    {
        TestFileSystem.CreateFile(_temp, @"a\b\c.tmp", 10);
        TestFileSystem.CreateFile(_temp, "d.tmp", 10);

        var result = TempFileCleaner.Clean(UserTemp(), null, CancellationToken.None);

        Assert.Equal(2, result.FilesDeleted);
        Assert.True(Directory.Exists(_temp));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp));
    }

    [Fact]
    public void Junction_InsideTemp_IsNeverFollowed()
    {
        var outside = Path.Combine(_root, "Documents");
        var precious = TestFileSystem.CreateFile(outside, "thesis.docx", 100);
        var link = Path.Combine(_temp, "link");
        TestFileSystem.CreateJunction(link, outside);
        _cleanup.Add(() => TestFileSystem.RemoveJunction(link));

        var scan = TempFileCleaner.Scan(UserTemp(), CancellationToken.None);
        TempFileCleaner.Clean(UserTemp(), null, CancellationToken.None);

        Assert.Equal(0, scan.Scan.FileCount);
        Assert.True(File.Exists(precious));
        Assert.True(Directory.Exists(link));
    }

    [Fact]
    public void TempRootReplacedByJunction_YieldsNothing()
    {
        var outside = Path.Combine(_root, "Documents");
        var precious = TestFileSystem.CreateFile(outside, "thesis.docx", 100);
        Directory.Delete(_temp);
        TestFileSystem.CreateJunction(_temp, outside);
        _cleanup.Add(() => TestFileSystem.RemoveJunction(_temp));

        var clean = TempFileCleaner.Clean(UserTemp(), null, CancellationToken.None);

        Assert.Equal(0, clean.FilesDeleted);
        Assert.True(File.Exists(precious));
    }

    [Fact]
    public void Prefetch_RemovesOnlyTopLevelPfFiles()
    {
        var prefetch = Path.Combine(_root, "Prefetch");
        var trace = TestFileSystem.CreateFile(prefetch, "NOTEPAD.EXE-1A2B3C4D.pf", 10);
        var layout = TestFileSystem.CreateFile(prefetch, "Layout.ini", 10);
        var db = TestFileSystem.CreateFile(prefetch, "AgGlFaultHistory.db", 10);
        var readyBoot = TestFileSystem.CreateFile(prefetch, @"ReadyBoot\Trace.pf", 10);
        var location = new TempLocation(TempLocationKind.Prefetch, prefetch);

        var result = TempFileCleaner.Clean(location, null, CancellationToken.None);

        Assert.Equal(1, result.FilesDeleted);
        Assert.False(File.Exists(trace));
        Assert.True(File.Exists(layout));
        Assert.True(File.Exists(db));
        Assert.True(File.Exists(readyBoot));
    }

    [Fact]
    public void RecentFiles_AreKept_WhenMinAgeIsSet()
    {
        var old = TestFileSystem.CreateFile(_temp, "old.tmp", 10, age: TimeSpan.FromDays(2));
        var recent = TestFileSystem.CreateFile(_temp, "recent.tmp", 10);

        TempFileCleaner.Clean(UserTemp(TimeSpan.FromHours(24)), null, CancellationToken.None);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void Progress_ReportsFinalCounters()
    {
        for (var i = 0; i < 5; i++) TestFileSystem.CreateFile(_temp, $"f{i}.tmp", 100);
        var reports = new List<CleanProgress>();

        TempFileCleaner.Clean(UserTemp(), new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal(new CleanProgress(5, 5, 0, 500), reports[^1]);
    }

    [Fact]
    public void Cancellation_StopsCleaning()
    {
        TestFileSystem.CreateFile(_temp, "a.tmp", 10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => TempFileCleaner.Clean(UserTemp(), null, cts.Token));
        Assert.True(File.Exists(Path.Combine(_temp, "a.tmp")));
    }

    private TempLocation UserTemp(TimeSpan? minAge = null) =>
        new(TempLocationKind.UserTemp, _temp) { MinAge = minAge ?? TimeSpan.Zero };

    private void Deny(FileSystemInfo item, FileSystemRights rights)
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(user, rights, AccessControlType.Deny);
        switch (item)
        {
            case DirectoryInfo dir:
            {
                var acl = dir.GetAccessControl();
                acl.AddAccessRule(rule);
                dir.SetAccessControl(acl);
                _cleanup.Insert(0, () =>
                {
                    var current = dir.GetAccessControl();
                    current.RemoveAccessRule(rule);
                    dir.SetAccessControl(current);
                });
                break;
            }

            case FileInfo file:
            {
                var acl = file.GetAccessControl();
                acl.AddAccessRule(rule);
                file.SetAccessControl(acl);
                _cleanup.Insert(0, () =>
                {
                    var current = file.GetAccessControl();
                    current.RemoveAccessRule(rule);
                    file.SetAccessControl(current);
                });
                break;
            }
        }
    }

    private sealed class SyncProgress(Action<CleanProgress> report) : IProgress<CleanProgress>
    {
        public void Report(CleanProgress value) => report(value);
    }
}
