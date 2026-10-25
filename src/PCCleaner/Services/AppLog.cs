using System.IO;

namespace PCCleaner.Services;

/// <summary>Minimal daily log file in %LOCALAPPDATA%\PCCleaner\logs.</summary>
public static class AppLog
{
    private static readonly Lock Gate = new();

    public static void Write(string message)
    {
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
