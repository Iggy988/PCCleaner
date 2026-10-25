using PCCleaner.Models;
using Wpf.Ui.Controls;

namespace PCCleaner.Services.Cleaning;

public interface ICleanupProvider
{
    string Id { get; }

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

    Task<ScanResult> ScanAsync(CancellationToken cancellationToken);

    Task<CleanResult> CleanAsync(IProgress<long>? progress, CancellationToken cancellationToken);
}
