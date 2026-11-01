using System.Runtime.InteropServices;
using PCCleaner.Services.Localization;
using PCCleaner.Services.Native;
using Wpf.Ui.Controls;

namespace PCCleaner.Services.Tweaks;

/// <summary>
/// Activates a high-performance power plan. If the built-in plan is hidden (common on laptops),
/// it is duplicated under a fixed GUID owned by this app. Revert re-activates the previous plan.
/// Text comes from the string resources: <c>Tweak_{id}_Name</c> / <c>_Desc</c>.
/// </summary>
public sealed class PowerPlanTweak(string id, SymbolRegular icon,
    Guid builtInScheme, Guid ownCopyScheme, SettingsService settings) : ITweak
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public string Id { get; } = id;

    public string Name => Loc.T($"Tweak_{Id.Replace('-', '_')}_Name");

    public string Description => Loc.T($"Tweak_{Id.Replace('-', '_')}_Desc");

    public SymbolRegular Icon { get; } = icon;

    public bool RequiresAdmin => false;

    private string BackupKey => $"{Id}.previous";

    public bool IsApplied()
    {
        var active = GetActiveScheme();
        return active == builtInScheme || active == ownCopyScheme;
    }

    public void Apply()
    {
        var current = GetActiveScheme();
        if (current is { } previous && previous != builtInScheme && previous != ownCopyScheme)
        {
            settings.Current.TweakBackups[BackupKey] = previous.ToString();
            settings.Save();
        }

        if (TrySetActive(builtInScheme) || TrySetActive(ownCopyScheme)) return;

        // Plan not present: create our own copy of it, then activate.
        ProcessRunner.Run("powercfg.exe", $"-duplicatescheme {builtInScheme} {ownCopyScheme}");
        if (!TrySetActive(ownCopyScheme))
            throw new InvalidOperationException(Loc.T("Tweak_PowerPlanUnsupported"));
    }

    public void Revert()
    {
        var target = settings.Current.TweakBackups.TryGetValue(BackupKey, out var saved) && Guid.TryParse(saved, out var guid)
            ? guid
            : Balanced;

        if (!TrySetActive(target) && !TrySetActive(Balanced))
            throw new InvalidOperationException(Loc.T("Tweak_PowerPlanRevertFailed"));

        settings.Current.TweakBackups.Remove(BackupKey);
        settings.Save();
    }

    private static Guid? GetActiveScheme()
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) return null;
        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            NativeMethods.LocalFree(ptr);
        }
    }

    private static bool TrySetActive(Guid scheme) => NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
}
