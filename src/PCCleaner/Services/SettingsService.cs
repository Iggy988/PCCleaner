using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCleaner.Models;

namespace PCCleaner.Services;

public sealed class SettingsService
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UltimatePCCleaner");

    /// <summary>Where versions before the Ultimate PC Cleaner rebrand kept their settings.</summary>
    private static readonly string LegacyDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCCleaner");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");

    private static readonly string SettingsPath = Path.Combine(DataDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _gate = new();

    public SettingsService()
    {
        Current = Load();
    }

    public AppSettings Current { get; }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                var tmp = SettingsPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
                File.Move(tmp, SettingsPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write($"Could not save settings: {ex.Message}");
            }
        }
    }

    private static AppSettings Load()
    {
        MigrateLegacySettings();
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Write($"Settings file unreadable, using defaults: {ex.Message}");
        }

        return new AppSettings();
    }

    /// <summary>Copies settings.json from the pre-rebrand data folder once, so selections and stats carry over.</summary>
    private static void MigrateLegacySettings()
    {
        var legacy = Path.Combine(LegacyDataDirectory, "settings.json");
        if (File.Exists(SettingsPath) || !File.Exists(legacy)) return;

        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.Copy(legacy, SettingsPath);
            AppLog.Write("Migrated settings from the previous PC Cleaner data folder.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"Could not migrate old settings: {ex.Message}");
        }
    }
}
