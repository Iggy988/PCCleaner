using System.IO;
using PCCleaner.Models;
using PCCleaner.Services;
using PCCleaner.Services.Security;

namespace PCCleaner.Tests;

public sealed class QuarantineServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PCCleanerTests", "quarantine-" + Guid.NewGuid().ToString("N"));
    private readonly QuarantineService _service;

    public QuarantineServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "files"));
        _service = new QuarantineService(new StartupService(), Path.Combine(_root, "store"), restrictAccess: false);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string CreateFile(string name, byte[] content)
    {
        var path = Path.Combine(_root, "files", name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public async Task QuarantineAndRestore_RoundTripsTheExactBytes()
    {
        var content = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 7)).ToArray();
        var path = CreateFile("tool.exe", content);
        var lastWrite = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, lastWrite);

        var entry = await _service.QuarantineAsync(path, [SuspiciousRules.UnsignedUserFolder], null);

        Assert.False(File.Exists(path));
        Assert.False(entry.PendingReboot);
        var stored = File.ReadAllBytes(Path.Combine(_service.Root, entry.Id + ".qbin"));
        Assert.Equal(content.Length, stored.Length);
        Assert.NotEqual(content, stored);
        Assert.Single(_service.GetEntries());

        var result = await _service.RestoreAsync(entry.Id, overwrite: false);

        Assert.Equal(QuarantineRestoreResult.Restored, result);
        Assert.Equal(content, File.ReadAllBytes(path));
        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(path));
        Assert.Empty(_service.GetEntries());
    }

    [Fact]
    public async Task Restore_DoesNotOverwriteAnExistingFile_UnlessAsked()
    {
        var path = CreateFile("script.ps1", "Write-Host original"u8.ToArray());
        var entry = await _service.QuarantineAsync(path, [], null);
        File.WriteAllText(path, "new file");

        Assert.Equal(QuarantineRestoreResult.TargetExists, await _service.RestoreAsync(entry.Id, overwrite: false));
        Assert.Equal("new file", File.ReadAllText(path));

        Assert.Equal(QuarantineRestoreResult.Restored, await _service.RestoreAsync(entry.Id, overwrite: true));
        Assert.Equal("Write-Host original", File.ReadAllText(path));
    }

    [Fact]
    public async Task DeletePermanently_RemovesTheEntry()
    {
        var entry = await _service.QuarantineAsync(CreateFile("a.vbs", [1, 2, 3]), [], null);

        await _service.DeletePermanentlyAsync(entry.Id);

        Assert.Empty(_service.GetEntries());
        Assert.Empty(Directory.GetFiles(_service.Root));
    }

    [Fact]
    public async Task Quarantine_RefusesWindowsFolderFiles()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.QuarantineAsync(notepad, [], null));
    }
}
