namespace PCCleaner.Models;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public sealed class AppSettings
{
    /// <summary>UI language code (see LocalizationService.SupportedLanguages). Serbian (Latin) by default.</summary>
    public string Language { get; set; } = "sr-Latn";

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>Turns off decorative animations even if Windows animations are enabled.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Leave temp files touched in the last 24 hours alone (they may belong to running installers/apps).</summary>
    public bool SkipRecentTempFiles { get; set; } = true;

    public bool CreateRestorePoint { get; set; } = true;

    public bool RememberSelections { get; set; } = true;

    /// <summary>Cleanup category id -> checked.</summary>
    public Dictionary<string, bool> CategorySelections { get; set; } = [];

    public DateTime? LastCleaned { get; set; }

    public long TotalBytesFreed { get; set; }

    /// <summary>Opt-in: look up hashes of suspicious files on VirusTotal (only SHA-256 hashes are sent).</summary>
    public bool VirusTotalEnabled { get; set; }

    /// <summary>VirusTotal API key encrypted with DPAPI for the current Windows user (base64).</summary>
    public string? VirusTotalApiKeyProtected { get; set; }

    /// <summary>Original values saved by tweaks so they can be reverted exactly.</summary>
    public Dictionary<string, string> TweakBackups { get; set; } = [];
}
