using System.Diagnostics;
using System.IO;

namespace PCCleaner.Tests;

/// <summary>Shared helpers for tests that work on a throw-away folder under %TEMP%.</summary>
internal static class TestFileSystem
{
    public static string NewRoot() => Path.Combine(Path.GetTempPath(), "PCCleanerTests", Guid.NewGuid().ToString("N"));

    public static string CreateFile(string directory, string relativePath, int size, TimeSpan? age = null)
    {
        var path = Path.Combine(directory, relativePath);
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

    public static void CreateJunction(string link, string target) => RunCmd($"mklink /J \"{link}\" \"{target}\"");

    // rmdir removes only the link itself, never the target's contents.
    public static void RemoveJunction(string link) => RunCmd($"rmdir \"{link}\"");

    public static void TryDeleteTree(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

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
