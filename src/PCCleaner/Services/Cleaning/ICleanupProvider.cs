using PCCleaner.Models;
using Wpf.Ui.Controls;

namespace PCCleaner.Services.Cleaning;

public interface ICleanupProvider
{
    string Id { get; }

    /// <summary>Localized display name (looked up on every access, so it follows language changes).</summary>
    string Name { get; }

    string Description { get; }

    CleanupGroup Group { get; }

    SymbolRegular Icon { get; }

    /// <summary>Checked by default (safe for everyone).</summary>
    bool IsRecommended { get; }

    bool RequiresAdmin { get; }

    /// <summary>Optional caution shown under the description.</summary>
    string? Warning { get; }

    /// <summary>Process names that hold files in this category open (e.g. a browser).</summary>
    IReadOnlyList<string> BlockingProcesses { get; }

    /// <summary>Product name of the app behind <see cref="BlockingProcesses"/> (e.g. "Google Chrome").</summary>
    string? BlockingAppName { get; }

    Task<ScanResult> ScanAsync(CancellationToken cancellationToken);

    Task<CleanResult> CleanAsync(IProgress<CleanProgress>? progress, CancellationToken cancellationToken);
}
