using System.IO;
using Microsoft.Win32;

namespace PCCleaner.Models;

/// <summary>MpCmdRun scan types: 1 = quick, 2 = full, 3 = custom (a file or folder).</summary>
public enum ScanKind
{
    Quick = 1,
    Full = 2,
    Custom = 3,
}

public enum AmsiVerdict
{
    Clean,
    NotDetected,
    BlockedByAdmin,
    Malware,
    Unavailable,
}

public enum SignatureState
{
    Unknown,
    Unsigned,
    Signed,
    SignedByMicrosoft,
}

public enum SuspicionSeverity
{
    Low,
    Medium,
    High,
}

/// <summary>Where a suspicious item was found. The string-resource key is <c>Sec_Source_{value}</c>.</summary>
public enum SuspiciousSource
{
    StartupEntry,
    ScheduledTask,
    ScriptFile,
}

public sealed record DefenderStatus(
    bool Available,
    bool AntivirusEnabled,
    bool RealTimeProtectionEnabled,
    bool IsPassive,
    bool TamperProtected,
    string? SignatureVersion,
    DateTime? SignatureUpdated,
    DateTime? LastQuickScan,
    string? OtherAntivirus);

public sealed record DefenderThreat(
    long ThreatId,
    string Name,
    int SeverityId,
    IReadOnlyList<string> Resources,
    DateTime? DetectedAt,
    bool IsActive);

public sealed record DefenderQuarantineItem(string ThreatName, string FilePath, string? QuarantinedAt);

public sealed class SuspiciousItem
{
    public required string DisplayName { get; init; }

    public required SuspiciousSource Source { get; init; }

    /// <summary>The file the entry points at (or the script itself). May not exist.</summary>
    public string? FilePath { get; init; }

    /// <summary>Full command line (startup entries, scheduled tasks) or null for script files.</summary>
    public string? Command { get; init; }

    /// <summary>String-resource keys (<c>Sec_Reason_*</c>) explaining why the item was flagged.</summary>
    public List<string> Reasons { get; init; } = [];

    public SuspicionSeverity Severity { get; set; }

    /// <summary>Engines that flagged the file on VirusTotal (null until checked; -1 when the file is unknown there).</summary>
    public int? VirusTotalPositives { get; set; }

    /// <summary>Set for startup entries, so quarantining can also disable the entry.</summary>
    public StartupItem? StartupItem { get; init; }

    public bool FileExists => FilePath is not null && File.Exists(FilePath);
}

/// <summary>Metadata stored next to each quarantined file (<c>{Id}.json</c> beside <c>{Id}.qbin</c>).</summary>
public sealed class QuarantineEntry
{
    public string Id { get; set; } = "";

    public string OriginalPath { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public long Size { get; set; }

    public DateTime QuarantinedAt { get; set; }

    public List<string> Reasons { get; set; } = [];

    /// <summary>XOR key for the stored content (keeps it non-executable and stops AV from re-flagging it).</summary>
    public string Key { get; set; } = "";

    public FileAttributes Attributes { get; set; }

    public DateTime CreationTimeUtc { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    /// <summary>The original file was locked and will be deleted on the next restart.</summary>
    public bool PendingReboot { get; set; }

    public RegistryHive? StartupHive { get; set; }

    public string? StartupKeyPath { get; set; }

    public string? StartupEntryName { get; set; }

    /// <summary>The startup entry was enabled before quarantine and is re-enabled on restore.</summary>
    public bool StartupWasEnabled { get; set; }
}

public enum QuarantineRestoreResult
{
    Restored,
    TargetExists,
    PendingReboot,
}
