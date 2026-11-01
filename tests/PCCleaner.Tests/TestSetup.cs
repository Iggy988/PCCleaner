using System.Runtime.CompilerServices;
using PCCleaner.Services;

namespace PCCleaner.Tests;

internal static class TestSetup
{
    /// <summary>Keeps test runs out of the user's real log in %LOCALAPPDATA%\UltimatePCCleaner\logs.</summary>
    [ModuleInitializer]
    internal static void Initialize() => AppLog.IsEnabled = false;
}
