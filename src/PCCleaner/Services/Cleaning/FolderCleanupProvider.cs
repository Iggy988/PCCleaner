using System.ServiceProcess;
using PCCleaner.Models;
using PCCleaner.Services.Localization;
using Wpf.Ui.Controls;

namespace PCCleaner.Services.Cleaning;

/// <summary>
/// A cleanup category made of one or more folders. Targets are produced lazily so settings
/// (like "skip recent temp files") apply at scan/clean time. Optionally stops Windows services
/// that lock the files while cleaning and restarts them afterwards.
/// Display text comes from the string resources: <c>Cleanup_{id}_Name</c>, <c>_Desc</c> and (if <see cref="HasWarning"/>) <c>_Warn</c>.
/// </summary>
public sealed class FolderCleanupProvider(string id, CleanupGroup group, SymbolRegular icon,
    Func<IEnumerable<FolderTarget>> targets, string? descriptionKey = null) : ICleanupProvider
{
    private readonly string _key = "Cleanup_" + id.Replace('-', '_');

    public string Id { get; } = id;

    public string Name => Loc.T(_key + "_Name");

    public string Description => Loc.T(descriptionKey ?? _key + "_Desc");

    public CleanupGroup Group { get; } = group;

    public SymbolRegular Icon { get; } = icon;

    public bool IsRecommended { get; init; } = true;

    public bool RequiresAdmin { get; init; }

    public bool HasWarning { get; init; }

    public string? Warning => HasWarning ? Loc.T(_key + "_Warn") : null;

    public IReadOnlyList<string> BlockingProcesses { get; init; } = [];

    public string? BlockingAppName { get; init; }

    public IReadOnlyList<string> ServicesToStop { get; init; } = [];

    public Task<ScanResult> ScanAsync(CancellationToken cancellationToken) =>
        Task.Run(() => FileCleaner.Scan(targets(), cancellationToken), cancellationToken);

    public Task<CleanResult> CleanAsync(IProgress<CleanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var stopped = StopServices();
            try
            {
                return FileCleaner.Clean(targets(), progress, cancellationToken);
            }
            finally
            {
                StartServices(stopped);
            }
        }, cancellationToken);

    private List<string> StopServices()
    {
        var stopped = new List<string>();
        foreach (var name in ServicesToStop)
        {
            try
            {
                using var service = new ServiceController(name);
                if (service.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) continue;
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                stopped.Add(name);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
            {
                AppLog.Write($"Could not stop service {name}: {ex.Message}");
            }
        }

        return stopped;
    }

    private static void StartServices(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            try
            {
                using var service = new ServiceController(name);
                service.Start();
            }
            catch (InvalidOperationException ex)
            {
                AppLog.Write($"Could not restart service {name}: {ex.Message}");
            }
        }
    }
}
