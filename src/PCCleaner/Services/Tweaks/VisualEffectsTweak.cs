using System.Text.Json;
using Microsoft.Win32;
using PCCleaner.Services.Native;
using Wpf.Ui.Controls;

namespace PCCleaner.Services.Tweaks;

/// <summary>Turns off window/menu animations and transparency, like "Adjust for best performance" (but reversible).</summary>
public sealed class VisualEffectsTweak(SettingsService settings) : ITweak
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string VisualEffectsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";

    private static readonly (uint Get, uint Set)[] BoolSettings =
    [
        (NativeMethods.SPI_GETMENUANIMATION, NativeMethods.SPI_SETMENUANIMATION),
        (NativeMethods.SPI_GETCOMBOBOXANIMATION, NativeMethods.SPI_SETCOMBOBOXANIMATION),
        (NativeMethods.SPI_GETLISTBOXSMOOTHSCROLLING, NativeMethods.SPI_SETLISTBOXSMOOTHSCROLLING),
        (NativeMethods.SPI_GETTOOLTIPANIMATION, NativeMethods.SPI_SETTOOLTIPANIMATION),
        (NativeMethods.SPI_GETCLIENTAREAANIMATION, NativeMethods.SPI_SETCLIENTAREAANIMATION),
    ];

    public string Id => "visual-effects";

    public string Name => "Performance visual effects";

    public string Description => "Turns off window animations, smooth scrolling and transparency so the desktop feels snappier on slower PCs.";

    public SymbolRegular Icon => SymbolRegular.Eye24;

    public bool RequiresAdmin => false;

    public bool IsApplied() => !GetMinimizeAnimation() && ReadTransparency() == 0 && BoolSettings.All(s => !GetBool(s.Get));

    public void Apply()
    {
        var backup = new Dictionary<string, int>
        {
            ["minAnimate"] = GetMinimizeAnimation() ? 1 : 0,
            ["transparency"] = ReadTransparency(),
            ["visualFx"] = ReadVisualFxSetting(),
        };
        foreach (var (get, _) in BoolSettings) backup[get.ToString()] = GetBool(get) ? 1 : 0;

        if (!settings.Current.TweakBackups.ContainsKey(Id))
        {
            settings.Current.TweakBackups[Id] = JsonSerializer.Serialize(backup);
            settings.Save();
        }

        SetMinimizeAnimation(false);
        foreach (var (_, set) in BoolSettings) SetBool(set, false);
        WriteTransparency(0);
        WriteVisualFxSetting(3); // "Custom" in System Properties → Performance Options
        BroadcastThemeChange();
    }

    public void Revert()
    {
        var backup = settings.Current.TweakBackups.TryGetValue(Id, out var json)
            ? JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? []
            : [];

        int Saved(string key) => backup.TryGetValue(key, out var v) ? v : 1;

        SetMinimizeAnimation(Saved("minAnimate") != 0);
        foreach (var (get, set) in BoolSettings) SetBool(set, Saved(get.ToString()) != 0);
        WriteTransparency(Saved("transparency"));
        WriteVisualFxSetting(backup.TryGetValue("visualFx", out var fx) ? fx : 0);
        BroadcastThemeChange();

        settings.Current.TweakBackups.Remove(Id);
        settings.Save();
    }

    private static bool GetBool(uint action) => NativeMethods.SystemParametersInfo(action, 0, out int value, 0) && value != 0;

    private static void SetBool(uint action, bool value) =>
        NativeMethods.SystemParametersInfo(action, 0, value ? new IntPtr(1) : IntPtr.Zero,
            NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);

    private static bool GetMinimizeAnimation()
    {
        var info = new NativeMethods.ANIMATIONINFO { cbSize = 8 };
        return NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETANIMATION, 8, ref info, 0) && info.iMinAnimate != 0;
    }

    private static void SetMinimizeAnimation(bool enabled)
    {
        var info = new NativeMethods.ANIMATIONINFO { cbSize = 8, iMinAnimate = enabled ? 1 : 0 };
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETANIMATION, 8, ref info,
            NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);
    }

    private static int ReadTransparency()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("EnableTransparency") is int v ? v : 1;
    }

    private static void WriteTransparency(int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
        key.SetValue("EnableTransparency", value, RegistryValueKind.DWord);
    }

    private static int ReadVisualFxSetting()
    {
        using var key = Registry.CurrentUser.OpenSubKey(VisualEffectsKey);
        return key?.GetValue("VisualFXSetting") is int v ? v : 0;
    }

    private static void WriteVisualFxSetting(int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(VisualEffectsKey);
        key.SetValue("VisualFXSetting", value, RegistryValueKind.DWord);
    }

    private static void BroadcastThemeChange() =>
        NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero,
            "ImmersiveColorSet", NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
}
