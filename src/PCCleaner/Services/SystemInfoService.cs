using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PCCleaner.Services.Native;

namespace PCCleaner.Services;

public sealed class SystemInfoService
{
    private long _prevIdle, _prevKernel, _prevUser;

    public SystemInfoService()
    {
        NativeMethods.GetSystemTimes(out _prevIdle, out _prevKernel, out _prevUser);
        OsName = ReadOsName();
        CpuName = ReadCpuName();
    }

    public string MachineName => Environment.MachineName;

    public string UserName => Environment.UserName;

    public string OsName { get; }

    public string CpuName { get; }

    public TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>CPU usage (0-100) since the previous call.</summary>
    public double GetCpuUsage()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;

        var idleDelta = idle - _prevIdle;
        // Kernel time includes idle time.
        var totalDelta = (kernel - _prevKernel) + (user - _prevUser);
        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;

        return totalDelta <= 0 ? 0 : Math.Clamp((totalDelta - idleDelta) * 100.0 / totalDelta, 0, 100);
    }

    public (double Percent, ulong Used, ulong Total) GetMemory()
    {
        var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0) return (0, 0, 0);

        var used = status.ullTotalPhys - status.ullAvailPhys;
        return (used * 100.0 / status.ullTotalPhys, used, status.ullTotalPhys);
    }

    public static ulong GetAvailableMemory()
    {
        var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status) ? status.ullAvailPhys : 0;
    }

    public (double Percent, long Free, long Total, string Name) GetSystemDrive()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            var used = drive.TotalSize - drive.TotalFreeSpace;
            return (used * 100.0 / drive.TotalSize, drive.TotalFreeSpace, drive.TotalSize, drive.Name.TrimEnd('\\'));
        }
        catch (IOException)
        {
            return (0, 0, 0, "C:");
        }
    }

    private static string ReadOsName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string ?? "Windows";
        var display = key?.GetValue("DisplayVersion") as string;
        var build = key?.GetValue("CurrentBuildNumber") as string;

        // Windows 11 still reports "Windows 10" in ProductName.
        if (int.TryParse(build, out var buildNumber) && buildNumber >= 22000)
            product = product.Replace("Windows 10", "Windows 11");

        return string.Join(" · ", new[] { product, display, build is null ? null : $"build {build}" }.Where(s => !string.IsNullOrEmpty(s)));
    }

    private static string ReadCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        var name = (key?.GetValue("ProcessorNameString") as string)?.Trim();
        return string.IsNullOrEmpty(name) ? $"{Environment.ProcessorCount} logical processors" : $"{name} ({Environment.ProcessorCount} threads)";
    }
}
