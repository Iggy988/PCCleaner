using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace PCCleaner.Services;

public static class AdminHelper
{
    public static bool IsAdministrator { get; } = CheckIsAdministrator();

    private static bool CheckIsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Starts a new elevated instance of the app. Returns false if the user declined the UAC prompt.</summary>
    public static bool StartElevatedInstance()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
