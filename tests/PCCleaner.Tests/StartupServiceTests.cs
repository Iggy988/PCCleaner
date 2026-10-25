using PCCleaner.Services;

namespace PCCleaner.Tests;

public sealed class StartupServiceTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Program Files\\App\\app.exe /background", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Tools\\tray.EXE", "C:\\Tools\\tray.EXE")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL", "rundll32.exe")]
    public void ExtractExecutable_HandlesQuotedAndUnquotedCommands(string command, string expected)
    {
        Assert.Equal(expected, StartupService.ExtractExecutable(command));
    }

    [Fact]
    public void ExtractExecutable_ExpandsEnvironmentVariables()
    {
        var result = StartupService.ExtractExecutable("\"%WINDIR%\\explorer.exe\"");

        Assert.Equal(Environment.ExpandEnvironmentVariables("%WINDIR%\\explorer.exe"), result);
    }
}
