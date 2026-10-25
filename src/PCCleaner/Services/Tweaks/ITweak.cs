using Wpf.Ui.Controls;

namespace PCCleaner.Services.Tweaks;

/// <summary>A reversible system setting shown as an on/off switch.</summary>
public interface ITweak
{
    string Id { get; }

    string Name { get; }

    string Description { get; }

    SymbolRegular Icon { get; }

    bool RequiresAdmin { get; }

    bool IsApplied();

    void Apply();

    void Revert();
}
