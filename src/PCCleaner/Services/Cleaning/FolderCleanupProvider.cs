using System.ServiceProcess;
using PCCleaner.Models;
using Wpf.Ui.Controls;

namespace PCCleaner.Services.Cleaning;

/// <summary>
/// A cleanup category made of one or more folders. Targets are produced lazily so settings
/// (like "skip recent temp files") apply at scan/clean time. Optionally stops Windows services
/// that lock the files while cleaning and restarts them afterwards.
/// </summary>
public sealed class FolderCleanupProvider(string id, string name, string description, CleanupGroup group,
    SymbolRegular icon, Func<IEnumerable<FolderTarget>> targets) : ICleanupProvider
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string Description { get; } = description;

    public CleanupGroup Group { get; } = group;

    public SymbolRegular Icon { get; } = icon;

    public bool IsRecommended { get; init; } = true;

    public bool RequiresAdmin { get; init; }

    public string? Warning { get; init; }

    public IReadOnlyList<string> BlockingProcesses { get; init; } = [];

    public IReadOnlyList<string> ServicesToStop { get; init; } = [];

    public Task<ScanResult> ScanAsync(CancellationToken cancellationToken) =>
        Task.Run(() => FileCleaner.Scan(targets(), cancellationToken), cancellationToken);

    public Task<CleanResult> CleanAsync(IProgress<long>? progress, CancellationToken cancellationToken) =>
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
