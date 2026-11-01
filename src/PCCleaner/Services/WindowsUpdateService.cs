using System.Runtime.InteropServices;

namespace PCCleaner.Services;

/// <summary>An update Windows Update offers but hasn't installed yet.</summary>
public sealed record PendingUpdate(string Id, string Title, string? KbArticle, long SizeBytes, bool IsDownloaded, bool IsOptional);

public enum UpdatePhase { Downloading, Installing }

public sealed record UpdateProgress(UpdatePhase Phase, int Index, int Total, string Title);

public sealed record UpdateInstallResult(int Installed, int Failed, bool RebootRequired);

/// <summary>
/// Searches, downloads and installs updates through the Windows Update Agent COM API (the same engine as Settings).
/// The calls are synchronous and slow, so everything runs on a worker thread; cancellation takes effect between updates.
/// </summary>
public sealed class WindowsUpdateService
{
    // OperationResultCode: orcSucceeded / orcSucceededWithErrors.
    private const int Succeeded = 2;
    private const int SucceededWithErrors = 3;

    private readonly Lock _gate = new();
    private dynamic? _session;
    private Dictionary<string, dynamic> _found = [];

    public static bool IsRebootRequired()
    {
        try
        {
            return (bool)CreateCom("Microsoft.Update.SystemInfo").RebootRequired;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public Task<IReadOnlyList<PendingUpdate>> SearchAsync() => Task.Run<IReadOnlyList<PendingUpdate>>(() =>
    {
        var session = Session();
        var searcher = session.CreateUpdateSearcher();
        var result = searcher.Search("IsInstalled=0 and IsHidden=0");

        var updates = new List<PendingUpdate>();
        var found = new Dictionary<string, dynamic>();
        var collection = result.Updates;
        for (var i = 0; i < (int)collection.Count; i++)
        {
            var update = collection[i];
            string id = update.Identity.UpdateID;
            var kbs = update.KBArticleIDs;
            updates.Add(new PendingUpdate(
                id,
                (string)update.Title,
                (int)kbs.Count > 0 ? "KB" + (string)kbs[0] : null,
                Convert.ToInt64(update.MaxDownloadSize),
                (bool)update.IsDownloaded,
                (bool)update.BrowseOnly));
            found[id] = update;
        }

        lock (_gate) _found = found;
        AppLog.Write($"Windows Update search found {updates.Count} update(s).");
        return updates;
    });

    /// <summary>Downloads and installs the given updates (from the last search) one by one. Requires administrator rights.</summary>
    public Task<UpdateInstallResult> InstallAsync(IReadOnlyList<string> updateIds, IProgress<UpdateProgress> progress,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        var session = Session();
        int installed = 0, failed = 0;
        var rebootRequired = false;

        for (var i = 0; i < updateIds.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            dynamic? update;
            lock (_gate) update = _found.GetValueOrDefault(updateIds[i]);
            if (update is null) { failed++; continue; }

            string title = update.Title;
            try
            {
                if (!(bool)update.EulaAccepted) update.AcceptEula();

                var single = CreateCom("Microsoft.Update.UpdateColl");
                single.Add(update);

                if (!(bool)update.IsDownloaded)
                {
                    progress.Report(new UpdateProgress(UpdatePhase.Downloading, i + 1, updateIds.Count, title));
                    var downloader = session.CreateUpdateDownloader();
                    downloader.Updates = single;
                    var download = downloader.Download();
                    if (!IsSuccess((int)download.ResultCode))
                    {
                        AppLog.Write($"Update download failed ({download.ResultCode}): {title}");
                        failed++;
                        continue;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(new UpdateProgress(UpdatePhase.Installing, i + 1, updateIds.Count, title));
                var installer = session.CreateUpdateInstaller();
                installer.Updates = single;
                var result = installer.Install();
                if (IsSuccess((int)result.ResultCode))
                {
                    installed++;
                    rebootRequired |= (bool)result.RebootRequired;
                    AppLog.Write($"Update installed: {title}");
                }
                else
                {
                    failed++;
                    AppLog.Write($"Update install failed ({result.ResultCode}): {title}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                AppLog.Write($"Update {title} failed: 0x{ex.HResult:X8} {ex.Message}");
            }
        }

        return new UpdateInstallResult(installed, failed, rebootRequired || IsRebootRequired());
    }, cancellationToken);

    private static bool IsSuccess(int resultCode) => resultCode is Succeeded or SucceededWithErrors;

    private dynamic Session()
    {
        lock (_gate)
        {
            if (_session is null)
            {
                _session = CreateCom("Microsoft.Update.Session");
                _session.ClientApplicationID = "Ultimate PC Cleaner";
            }

            return _session;
        }
    }

    private static dynamic CreateCom(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;
}
