using Wpf.Ui.Controls;

namespace PCCleaner.Services.Tweaks;

public sealed class TweakCatalog(SettingsService settings)
{
    // Fixed GUIDs for our own copies of hidden built-in plans, so re-applying never creates duplicates.
    private static readonly Guid OwnHighPerformance = new("5a1c3b0e-7d4f-4c36-9e0b-2f6a8d9c1b11");
    private static readonly Guid OwnUltimatePerformance = new("5a1c3b0e-7d4f-4c36-9e0b-2f6a8d9c1b22");

    public IReadOnlyList<ITweak> Tweaks { get; } =
    [
        new PowerPlanTweak("power-high", "High performance power plan",
            "Keeps the CPU at higher clock speeds instead of saving power. Best for desktops or laptops on AC power.",
            SymbolRegular.Power24, PowerPlanTweak.HighPerformance, OwnHighPerformance, settings),
        new PowerPlanTweak("power-ultimate", "Ultimate performance power plan",
            "Removes power-saving micro-latencies. Intended for workstations; uses more energy and battery.",
            SymbolRegular.Rocket24, PowerPlanTweak.UltimatePerformance, OwnUltimatePerformance, settings),
        new VisualEffectsTweak(settings),
        new GameDvrTweak(settings),
    ];
}
