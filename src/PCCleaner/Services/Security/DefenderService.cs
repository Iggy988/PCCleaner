using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using PCCleaner.Models;
using PCCleaner.Services.Native;

namespace PCCleaner.Services.Security;

/// <summary>
/// Front-end for Microsoft Defender: status and detections through its WMI provider, scans and its quarantine
/// through MpCmdRun.exe, and threat actions through the Defender PowerShell cmdlets. Detection is always Defender's.
/// </summary>
public sealed class DefenderService
{
    private const string DefenderNamespace = @"\\.\root\Microsoft\Windows\Defender";
    private const string SecurityCenterNamespace = @"\\.\root\SecurityCenter2";

    public Task<DefenderStatus> GetStatusAsync() => Task.Run(() =>
    {
        var otherAv = ReadOtherAntivirus();
        try
        {
            using var searcher = new ManagementObjectSearcher(DefenderNamespace, "SELECT * FROM MSFT_MpComputerStatus");
            foreach (ManagementObject status in searcher.Get())
            {
                using (status)
                {
                    var mode = status["AMRunningMode"] as string;
                    return new DefenderStatus(
                        Available: true,
                        AntivirusEnabled: status["AntivirusEnabled"] is true,
                        RealTimeProtectionEnabled: status["RealTimeProtectionEnabled"] is true,
                        IsPassive: mode is not null && !mode.Equals("Normal", StringComparison.OrdinalIgnoreCase),
                        TamperProtected: status["IsTamperProtected"] is true,
                        SignatureVersion: status["AntivirusSignatureVersion"] as string,
                        SignatureUpdated: ToDate(status["AntivirusSignatureLastUpdated"]),
                        LastQuickScan: ToDate(status["QuickScanEndTime"]),
                        OtherAntivirus: otherAv);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            AppLog.Write($"Defender status unavailable: {ex.Message}");
        }

        return new DefenderStatus(false, false, false, false, false, null, null, null, otherAv);
    });

    /// <summary>Current Defender detections, newest first (one row per threat).</summary>
    public Task<IReadOnlyList<DefenderThreat>> GetThreatsAsync() => Task.Run<IReadOnlyList<DefenderThreat>>(() =>
    {
        var threats = new Dictionary<long, (string Name, int Severity, bool Active)>();
        var detections = new Dictionary<long, (DateTime? Time, List<string> Resources)>();
        try
        {
            using (var searcher = new ManagementObjectSearcher(DefenderNamespace, "SELECT ThreatID, ThreatName, SeverityID, IsActive FROM MSFT_MpThreat"))
            {
                foreach (ManagementObject threat in searcher.Get())
                {
                    using (threat)
                    {
                        var id = Convert.ToInt64(threat["ThreatID"]);
                        threats[id] = (threat["ThreatName"] as string ?? id.ToString(), Convert.ToInt32(threat["SeverityID"] ?? 0), threat["IsActive"] is true);
                    }
                }
            }

            using (var searcher = new ManagementObjectSearcher(DefenderNamespace, "SELECT ThreatID, Resources, InitialDetectionTime FROM MSFT_MpThreatDetection"))
            {
                foreach (ManagementObject detection in searcher.Get())
                {
                    using (detection)
                    {
                        var id = Convert.ToInt64(detection["ThreatID"]);
                        var time = ToDate(detection["InitialDetectionTime"]);
                        if (!detections.TryGetValue(id, out var entry)) entry = (time, []);
                        if (time > entry.Time) entry.Time = time;
                        if (detection["Resources"] is string[] resources)
                            entry.Resources.AddRange(resources.Select(CleanResource).Where(r => !entry.Resources.Contains(r)));
                        detections[id] = entry;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            AppLog.Write($"Reading Defender threats failed: {ex.Message}");
            return [];
        }

        return threats
            .Select(t =>
            {
                detections.TryGetValue(t.Key, out var d);
                return new DefenderThreat(t.Key, t.Value.Name, t.Value.Severity, d.Resources ?? [], d.Time, t.Value.Active);
            })
            .OrderByDescending(t => t.DetectedAt)
            .ToList();
    });

    public Task<int> ScanAsync(ScanKind kind, string? path, Action<string> onLine, CancellationToken cancellationToken)
    {
        var arguments = $"-Scan -ScanType {(int)kind}";
        if (kind == ScanKind.Custom)
        {
            // A trailing backslash would escape the closing quote; "C:" alone would mean the current folder on C.
            var target = path?.TrimEnd('\\') ?? "";
            if (target.EndsWith(':')) target += @"\.";
            arguments += $" -File \"{target}\"";
        }

        return RunMpCmdRunAsync(arguments, onLine, cancellationToken);
    }

    public Task<int> UpdateSignaturesAsync(Action<string> onLine, CancellationToken cancellationToken) =>
        RunMpCmdRunAsync("-SignatureUpdate", onLine, cancellationToken);

    /// <summary>Lets Defender clean or quarantine every active threat (Remove-MpThreat).</summary>
    public Task<(bool Success, string Output)> RemoveActiveThreatsAsync() => RunPowerShellAsync("Remove-MpThreat");

    /// <summary>Tells Defender to allow this threat from now on.</summary>
    public Task<(bool Success, string Output)> AllowThreatAsync(long threatId) =>
        RunPowerShellAsync($"Add-MpPreference -ThreatIDDefaultAction_Ids {threatId} -ThreatIDDefaultAction_Actions Allow");

    public Task<IReadOnlyList<DefenderQuarantineItem>> GetQuarantineAsync() => Task.Run<IReadOnlyList<DefenderQuarantineItem>>(() =>
    {
        var mpCmdRun = FindMpCmdRun();
        if (mpCmdRun is null) return [];
        var (_, output) = ProcessRunner.Run(mpCmdRun, "-Restore -ListAll", 60_000);
        return ParseQuarantineList(output);
    });

    /// <summary>Restores every quarantined item Defender stored under <paramref name="threatName"/>.</summary>
    public Task<(bool Success, string Output)> RestoreFromQuarantineAsync(string threatName) => Task.Run(() =>
    {
        var mpCmdRun = FindMpCmdRun();
        if (mpCmdRun is null) return (false, "MpCmdRun.exe not found");
        var (code, output) = ProcessRunner.Run(mpCmdRun, $"-Restore -Name \"{threatName.Replace("\"", "")}\"", 120_000);
        return (code == 0, output.Trim());
    });

    /// <summary>
    /// Parses <c>MpCmdRun -Restore -ListAll</c>:
    /// <code>
    /// ThreatName = Virus:DOS/EICAR_Test_File
    ///       file:C:\Users\me\Downloads\eicar.com quarantined at 3/14/2026 9:12:44 AM (UTC)
    /// </code>
    /// </summary>
    internal static IReadOnlyList<DefenderQuarantineItem> ParseQuarantineList(string output)
    {
        var items = new List<DefenderQuarantineItem>();
        string? threat = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("ThreatName", StringComparison.OrdinalIgnoreCase) && line.Contains('='))
            {
                threat = line[(line.IndexOf('=') + 1)..].Trim();
                continue;
            }

            if (threat is null) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0 || line.StartsWith("ThreatName", StringComparison.OrdinalIgnoreCase)) continue;

            // "file:", "regkey:", "process:"... Only resources with a path are listed.
            var resource = line[(colon + 1)..];
            var at = resource.LastIndexOf(" quarantined at ", StringComparison.OrdinalIgnoreCase);
            var path = (at >= 0 ? resource[..at] : resource).Trim();
            var when = at >= 0 ? resource[(at + " quarantined at ".Length)..].Trim() : null;
            if (path.Length > 0) items.Add(new DefenderQuarantineItem(threat, path, when));
        }

        return items;
    }

    private static async Task<int> RunMpCmdRunAsync(string arguments, Action<string> onLine, CancellationToken cancellationToken)
    {
        var mpCmdRun = FindMpCmdRun() ?? throw new FileNotFoundException("MpCmdRun.exe");
        onLine($"> MpCmdRun.exe {arguments}");
        onLine("");
        var encoding = Encoding.GetEncoding((int)NativeMethods.GetOEMCP());
        return await ProcessRunner.RunAsync(mpCmdRun, arguments, onLine, encoding, cancellationToken);
    }

    private static Task<(bool Success, string Output)> RunPowerShellAsync(string command) => Task.Run(() =>
    {
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var (code, output) = ProcessRunner.Run(powershell, $"-NoProfile -NonInteractive -Command \"{command}\"", 300_000);
        return (code == 0, output.Trim());
    });

    internal static string? FindMpCmdRun()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender");
        var candidates = new[]
        {
            key?.GetValue("InstallLocation") as string,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender"),
        };

        return candidates
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => Path.Combine(c!, "MpCmdRun.exe"))
            .FirstOrDefault(File.Exists);
    }

    private static string? ReadOtherAntivirus()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(SecurityCenterNamespace, "SELECT displayName FROM AntiVirusProduct");
            foreach (ManagementObject product in searcher.Get())
            {
                using (product)
                {
                    if (product["displayName"] is string name && !name.Contains("Defender", StringComparison.OrdinalIgnoreCase))
                        return name;
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            // Security Center isn't available on Windows Server.
        }

        return null;
    }

    /// <summary>Detections list resources as "file:_C:\path"; keep just the path.</summary>
    private static string CleanResource(string resource)
    {
        var marker = resource.IndexOf(":_", StringComparison.Ordinal);
        return marker > 0 ? resource[(marker + 2)..] : resource;
    }

    private static DateTime? ToDate(object? value)
    {
        if (value is not string { Length: > 0 } dmtf) return null;
        try
        {
            var date = ManagementDateTimeConverter.ToDateTime(dmtf);
            return date.Year < 2000 ? null : date;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
