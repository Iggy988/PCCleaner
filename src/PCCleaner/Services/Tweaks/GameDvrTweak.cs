using Microsoft.Win32;
using PCCleaner.Services.Localization;
using Wpf.Ui.Controls;

namespace PCCleaner.Services.Tweaks;

/// <summary>Disables Xbox Game Bar background recording (Game DVR), which costs CPU/GPU while gaming.</summary>
public sealed class GameDvrTweak(SettingsService settings) : ITweak
{
    private static readonly (string Key, string Value)[] Values =
    [
        (@"System\GameConfigStore", "GameDVR_Enabled"),
        (@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"),
    ];

    public string Id => "game-dvr";

    public string Name => Loc.T("Tweak_game_dvr_Name");

    public string Description => Loc.T("Tweak_game_dvr_Desc");

    public SymbolRegular Icon => SymbolRegular.Games24;

    public bool RequiresAdmin => false;

    public bool IsApplied() => Values.All(v => Read(v.Key, v.Value) == 0);

    public void Apply()
    {
        foreach (var (key, value) in Values)
        {
            var backupKey = $"{Id}.{value}";
            if (!settings.Current.TweakBackups.ContainsKey(backupKey))
                settings.Current.TweakBackups[backupKey] = (Read(key, value) ?? 1).ToString();
            Write(key, value, 0);
        }

        settings.Save();
    }

    public void Revert()
    {
        foreach (var (key, value) in Values)
        {
            var backupKey = $"{Id}.{value}";
            var original = settings.Current.TweakBackups.TryGetValue(backupKey, out var s) && int.TryParse(s, out var v) ? v : 1;
            Write(key, value, original);
            settings.Current.TweakBackups.Remove(backupKey);
        }

        settings.Save();
    }

    private static int? Read(string key, string value)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(value) as int?;
    }

    private static void Write(string key, string value, int data)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        k.SetValue(value, data, RegistryValueKind.DWord);
    }
}
