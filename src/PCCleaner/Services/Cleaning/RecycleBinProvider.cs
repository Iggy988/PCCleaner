using System.Runtime.InteropServices;
using PCCleaner.Models;
using PCCleaner.Services.Localization;
using PCCleaner.Services.Native;
using Wpf.Ui.Controls;

namespace PCCleaner.Services.Cleaning;

public sealed class RecycleBinProvider : ICleanupProvider
{
    public string Id => "recycle-bin";

    public string Name => Loc.T("Cleanup_recycle_bin_Name");

    public string Description => Loc.T("Cleanup_recycle_bin_Desc");

    public CleanupGroup Group => CleanupGroup.System;

    public SymbolRegular Icon => SymbolRegular.Delete24;

    public bool IsRecommended => true;

    public bool RequiresAdmin => false;

    public string? Warning => null;

    public IReadOnlyList<string> BlockingProcesses => [];

    public string? BlockingAppName => null;

    public Task<ScanResult> ScanAsync(CancellationToken cancellationToken) => Task.Run(Query, cancellationToken);

    public Task<CleanResult> CleanAsync(IProgress<CleanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var before = Query();
            if (before.FileCount == 0) return CleanResult.Empty;

            NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
                NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);

            var after = Query();
            var freed = Math.Max(0, before.Bytes - after.Bytes);
            var deleted = Math.Max(0, before.FileCount - after.FileCount);
            progress?.Report(new CleanProgress(before.FileCount, deleted, after.FileCount, freed));
            return new CleanResult(freed, deleted, after.FileCount) { Failed = after.FileCount };
        }, cancellationToken);

    private static ScanResult Query()
    {
        var info = new NativeMethods.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        return NativeMethods.SHQueryRecycleBin(null, ref info) == 0
            ? new ScanResult(info.i64Size, (int)Math.Min(info.i64NumItems, int.MaxValue))
            : ScanResult.Empty;
    }
}
