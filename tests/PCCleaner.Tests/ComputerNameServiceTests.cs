using PCCleaner.Services;

namespace PCCleaner.Tests;

public sealed class ComputerNameServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("DESKTOP-1")]
    [InlineData("Igor-PC")]
    [InlineData("A23456789012345")]
    public void Validate_AcceptsValidNames(string name) => Assert.Null(ComputerNameService.Validate(name));

    [Theory]
    [InlineData("A234567890123456", "System_NameTooLong")]
    [InlineData("My PC", "System_NameInvalidChars")]
    [InlineData("PC_1", "System_NameInvalidChars")]
    [InlineData("Računar", "System_NameInvalidChars")]
    [InlineData("12345", "System_NameAllDigits")]
    [InlineData("-PC", "System_NameHyphenEdge")]
    [InlineData("PC-", "System_NameHyphenEdge")]
    public void Validate_RejectsInvalidNames(string name, string expectedKey) =>
        Assert.Equal(expectedKey, ComputerNameService.Validate(name));
}
