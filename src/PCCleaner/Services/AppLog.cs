using System.IO;

namespace PCCleaner.Services;

/// <summary>Minimal daily log file in %LOCALAPPDATA%\UltimatePCCleaner\logs.</summary>
public static class AppLog
{
    private static readonly Lock Gate = new();

    /// <summary>Turned off by the test suite so tests never write to the user's real log.</summary>
    internal static bool IsEnabled { get; set; } = true;

    public static void Write(string message)
    {
        if (!IsEnabled) return;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(SettingsService.LogsDirectory);
                var file = Path.Combine(SettingsService.LogsDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never crash the app.
            }
        }
    }
}
