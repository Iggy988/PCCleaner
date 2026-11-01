using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using PCCleaner.Services.Native;

namespace PCCleaner.Services;

/// <summary>Reads and changes the computer's name (applied by Windows on the next restart).</summary>
public sealed class ComputerNameService
{
    internal const int MaxLength = 15;

    private const string TcpipParametersKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";

    /// <summary>The name the computer is running under right now.</summary>
    public string CurrentName => Environment.MachineName;

    /// <summary>The name set for the next restart, or null when no rename is pending.</summary>
    public string? PendingName
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(TcpipParametersKey);
            var active = key?.GetValue("Hostname") as string;
            var next = key?.GetValue("NV Hostname") as string;
            return !string.IsNullOrEmpty(next) && !string.Equals(next, active, StringComparison.OrdinalIgnoreCase) ? next : null;
        }
    }

    /// <summary>Returns the resource key describing why <paramref name="name"/> can't be used, or null if it is valid.</summary>
    internal static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.Length > MaxLength) return "System_NameTooLong";
        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return "System_NameInvalidChars";
        if (name.All(char.IsAsciiDigit)) return "System_NameAllDigits";
        if (name.StartsWith('-') || name.EndsWith('-')) return "System_NameHyphenEdge";
        return null;
    }

    /// <summary>Sets the DNS host name (and with it the NetBIOS name). Requires administrator rights.</summary>
    public Task RenameAsync(string newName) => Task.Run(() =>
    {
        if (!NativeMethods.SetComputerNameEx(NativeMethods.ComputerNamePhysicalDnsHostname, newName))
            throw new Win32Exception();

        AppLog.Write($"Computer renamed from {CurrentName} to {newName} (applies after restart).");
    });

    /// <summary>Restarts Windows right away (apps get the usual chance to close).</summary>
    public static void RestartComputer()
    {
        AppLog.Write("Restarting the computer.");
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/r /t 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
