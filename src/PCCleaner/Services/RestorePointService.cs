using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PCCleaner.Services.Localization;

namespace PCCleaner.Services;

public sealed class RestorePointService
{
    private const string SystemRestoreKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string FrequencyValue = "SystemRestorePointCreationFrequency";
    private const int ModifySettings = 12;
    private const int BeginSystemChange = 100;

    /// <summary>True once a restore point was created by this app session.</summary>
    public bool CreatedThisSession { get; private set; }

    public Task<(bool Success, string Message)> CreateAsync(string description) => Task.Run(() =>
    {
        if (!AdminHelper.IsAdministrator)
            return (false, Loc.T("Restore_NeedsAdmin"));

        using var key = Registry.LocalMachine.CreateSubKey(SystemRestoreKey, writable: true);
        var previous = key.GetValue(FrequencyValue);

        try
        {
            // Windows silently skips restore points made within 24 hours of the last one unless this is 0.
            key.SetValue(FrequencyValue, 0, RegistryValueKind.DWord);

            using var restore = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
            using var input = restore.GetMethodParameters("CreateRestorePoint");
            input["Description"] = description;
            input["RestorePointType"] = ModifySettings;
            input["EventType"] = BeginSystemChange;

            using var output = restore.InvokeMethod("CreateRestorePoint", input, null);
            var code = Convert.ToUInt32(output["ReturnValue"]);
            if (code == 0)
            {
                CreatedThisSession = true;
                AppLog.Write($"Restore point created: {description}");
                return (true, Loc.T("Restore_Created"));
            }

            AppLog.Write($"Restore point failed with code {code}");
            return (false, code is 1058 or 0x80070422
                ? Loc.T("Restore_ProtectionOff")
                : Loc.F("Restore_FailedCode", code));
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            AppLog.Write($"Restore point failed: {ex.Message}");
            return (false, Loc.F("Restore_Failed", ex.Message));
        }
        finally
        {
            if (previous is null) key.DeleteValue(FrequencyValue, throwOnMissingValue: false);
            else key.SetValue(FrequencyValue, previous, RegistryValueKind.DWord);
        }
    });
}
