using System.Diagnostics;
using System.Runtime.InteropServices;
using PCCleaner.Services.Native;

namespace PCCleaner.Services;

/// <summary>One-shot optimizations: flush DNS and free RAM.</summary>
public sealed class SystemActions
{
    public Task<bool> FlushDnsAsync() => Task.Run(() => NativeMethods.DnsFlushResolverCache() != 0);

    /// <summary>Trims process working sets and (when elevated) purges the standby list. Returns bytes of RAM made available.</summary>
    public Task<long> FreeMemoryAsync() => Task.Run(() =>
    {
        var before = SystemInfoService.GetAvailableMemory();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                using var handle = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION | NativeMethods.PROCESS_SET_QUOTA, false, process.Id);
                if (!handle.IsInvalid)
                    NativeMethods.EmptyWorkingSet(handle);
            }
        }

        if (EnablePrivilege("SeProfileSingleProcessPrivilege"))
        {
            var command = NativeMethods.MemoryPurgeStandbyList;
            NativeMethods.NtSetSystemInformation(NativeMethods.SystemMemoryListInformation, ref command, sizeof(int));
        }

        var after = SystemInfoService.GetAvailableMemory();
        return after > before ? (long)(after - before) : 0L;
    });

    private static bool EnablePrivilege(string privilege)
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out var token))
            return false;

        using (token)
        {
            if (!NativeMethods.LookupPrivilegeValue(null, privilege, out var luid)) return false;

            var state = new NativeMethods.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeMethods.SE_PRIVILEGE_ENABLED,
            };

            return NativeMethods.AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero)
                   && Marshal.GetLastWin32Error() != NativeMethods.ERROR_NOT_ALL_ASSIGNED;
        }
    }
}
