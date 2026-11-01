using System.Text.Json;
using PCCleaner.Services.Security;

namespace PCCleaner.Tests;

public sealed class DefenderServiceTests
{
    [Fact]
    public void ParseQuarantineList_ReadsThreatNamesAndPaths()
    {
        const string output = """
            The following items are quarantined:

            ThreatName = Virus:DOS/EICAR_Test_File
                  file:C:\Users\me\Downloads\eicar.com quarantined at 3/14/2026 9:12:44 AM (UTC)
                  file:C:\Users\me\Desktop\eicar copy.txt quarantined at 3/14/2026 9:13:02 AM (UTC)

            ThreatName = Trojan:Win32/Wacatac.B!ml
                  file:D:\Games\crack.exe quarantined at 3/15/2026 6:00:00 PM (UTC)
            """;

        var items = DefenderService.ParseQuarantineList(output.Replace("\n", "\r\n"));

        Assert.Equal(3, items.Count);
        Assert.Equal("Virus:DOS/EICAR_Test_File", items[0].ThreatName);
        Assert.Equal(@"C:\Users\me\Downloads\eicar.com", items[0].FilePath);
        Assert.Equal("3/14/2026 9:12:44 AM (UTC)", items[0].QuarantinedAt);
        Assert.Equal(@"C:\Users\me\Desktop\eicar copy.txt", items[1].FilePath);
        Assert.Equal("Trojan:Win32/Wacatac.B!ml", items[2].ThreatName);
        Assert.Equal(@"D:\Games\crack.exe", items[2].FilePath);
    }

    [Fact]
    public void ParseQuarantineList_EmptyQuarantine()
    {
        Assert.Empty(DefenderService.ParseQuarantineList("No items found in the quarantine."));
        Assert.Empty(DefenderService.ParseQuarantineList(""));
    }

    [Fact]
    public void VirusTotal_CountsMaliciousAndSuspicious()
    {
        using var document = JsonDocument.Parse("""
            { "data": { "attributes": { "last_analysis_stats": { "malicious": 5, "suspicious": 2, "undetected": 60, "harmless": 0 } } } }
            """);

        Assert.Equal(7, VirusTotalClient.ParsePositives(document));
    }
}
