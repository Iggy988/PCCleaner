using System.Windows.Media;
using Microsoft.Win32;

namespace PCCleaner.Models;

public sealed class StartupItem
{
    public required string Name { get; init; }

    /// <summary>Registry value name or shortcut file name; the key used in StartupApproved.</summary>
    public required string EntryName { get; init; }

    public required string Command { get; init; }

    /// <summary>String-resource key describing where the entry is registered (e.g. Startup_Loc_RegistryUser).</summary>
    public required string Location { get; init; }

    public string? FilePath { get; init; }

    public string? Publisher { get; init; }

    public ImageSource? Icon { get; init; }

    public bool IsEnabled { get; set; }

    /// <summary>True for machine-wide entries (HKLM / All Users Startup) which need admin rights to change.</summary>
    public bool IsMachineWide { get; init; }

    internal RegistryHive ApprovedHive { get; init; }

    internal string ApprovedKeyPath { get; init; } = "";
}
