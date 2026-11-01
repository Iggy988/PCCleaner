using System.IO;
using PCCleaner.Models;

namespace PCCleaner.Services.Cleaning.Temp;

public sealed record TempScanResult(TempLocationStatus Status, ScanResult Scan)
{
    public static TempScanResult For(TempLocationStatus status) => new(status, ScanResult.Empty);
}

/// <summary>
/// Scans and cleans the temporary-file locations. Uses the shared <see cref="FileCleaner"/> engine, so deletion follows
/// the same rules: files only, no links followed, root never deleted, every file re-validated right before deletion.
/// </summary>
public sealed class TempFileCleaner(TempLocationResolver resolver, SettingsService settings)
{
    private TimeSpan TempMinAge => settings.Current.SkipRecentTempFiles ? TimeSpan.FromHours(24) : TimeSpan.Zero;

    /// <summary>Resolves the three locations with the current settings (call again after settings change).</summary>
    public IReadOnlyList<TempLocation> GetLocations() => resolver.ResolveAll(TempMinAge);

    public Task<TempScanResult> ScanAsync(TempLocation location, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(location, cancellationToken), cancellationToken);

    public Task<CleanResult> CleanAsync(TempLocation location, IProgress<CleanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Clean(location, progress, cancellationToken), cancellationToken);

    public static TempScanResult Scan(TempLocation location, CancellationToken cancellationToken)
    {
        if (!location.IsResolved) return TempScanResult.For(TempLocationStatus.ResolveError);
        if (!Directory.Exists(location.Path)) return TempScanResult.For(TempLocationStatus.Missing);

        var result = FileCleaner.Scan([location.ToFolderTarget()], cancellationToken, probeLocks: true);
        var status = result.RootAccessDenied ? TempLocationStatus.AccessDenied : TempLocationStatus.Ok;
        AppLog.Write($"Temp scan {location.Kind}: {status}, {result.FileCount:N0} eligible files ({result.Bytes:N0} bytes), " +
                     $"{result.LockedFiles:N0} in use, {result.InaccessibleItems:N0} inaccessible.");
        return new TempScanResult(status, result);
    }

    public static CleanResult Clean(TempLocation location, IProgress<CleanProgress>? progress, CancellationToken cancellationToken)
    {
        if (!location.IsResolved || !Directory.Exists(location.Path)) return CleanResult.Empty;

        var result = FileCleaner.Clean([location.ToFolderTarget()], progress, cancellationToken);
        AppLog.Write($"Temp clean {location.Kind}: {result.FilesDeleted:N0} deleted ({result.BytesFreed:N0} bytes), " +
                     $"{result.SkippedInUse:N0} in use, {result.SkippedAccessDenied:N0} access denied, {result.SkippedUnsafe:N0} unsafe, " +
                     $"{result.Failed:N0} failed, {result.AlreadyGone:N0} already gone.");
        return result;
    }
}
