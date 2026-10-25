namespace PCCleaner.Models;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public sealed class AppSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>Leave temp files touched in the last 24 hours alone (they may belong to running installers/apps).</summary>
    public bool SkipRecentTempFiles { get; set; } = true;

    public bool CreateRestorePoint { get; set; } = true;

    public bool RememberSelections { get; set; } = true;

    /// <summary>Cleanup category id -> checked.</summary>
    public Dictionary<string, bool> CategorySelections { get; set; } = [];

    public DateTime? LastCleaned { get; set; }

    public long TotalBytesFreed { get; set; }

    /// <summary>Original values saved by tweaks so they can be reverted exactly.</summary>
    public Dictionary<string, string> TweakBackups { get; set; } = [];
}
