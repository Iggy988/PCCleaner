using System.IO;
using PCCleaner.Services.Cleaning.Temp;

namespace PCCleaner.Tests;

public sealed class TempLocationResolverTests
{
    private static TempLocationResolver Resolver(Dictionary<string, string?> env, string fallback = "") =>
        new(name => env.GetValueOrDefault(name), _ => fallback);

    [Fact]
    public void WindowsLocations_ComeFromWindir_OnAnyDrive()
    {
        var resolver = Resolver(new() { ["WINDIR"] = @"D:\Win" });

        Assert.Equal(@"D:\Win\Prefetch", resolver.Resolve(TempLocationKind.Prefetch).Path);
        Assert.Equal(@"D:\Win\Temp", resolver.Resolve(TempLocationKind.WindowsTemp).Path);
    }

    [Fact]
    public void UserTemp_ComesFromLocalAppData_ForAnyUser()
    {
        var resolver = Resolver(new() { ["LOCALAPPDATA"] = @"E:\Profiles\someone\AppData\Local" });

        var location = resolver.Resolve(TempLocationKind.UserTemp, TimeSpan.FromHours(24));

        Assert.Equal(@"E:\Profiles\someone\AppData\Local\Temp", location.Path);
        Assert.Equal(TimeSpan.FromHours(24), location.MinAge);
        Assert.Contains(@"E:\Profiles\someone\AppData\Local\Temp\.net", location.ExcludedPaths);
        Assert.False(location.RequiresAdmin);
    }

    [Fact]
    public void MissingWindir_FallsBackToSystemRoot_ThenKnownFolder()
    {
        Assert.Equal(@"F:\Windows\Temp", Resolver(new() { ["SystemRoot"] = @"F:\Windows" }).Resolve(TempLocationKind.WindowsTemp).Path);
        Assert.Equal(@"G:\Windows\Prefetch", Resolver([], fallback: @"G:\Windows").Resolve(TempLocationKind.Prefetch).Path);
    }

    [Theory]
    [InlineData("Windows")]                    // relative
    [InlineData(@"C:\")]                        // drive root
    [InlineData(@"C:\Windows\..\Users")]        // traversal
    [InlineData(@"\\server\share\Windows")]     // UNC
    [InlineData(@"\\?\C:\Windows")]             // device path
    [InlineData("   ")]
    public void UnsafeBaseDirectories_AreRejected_AndNeverGuessed(string windir)
    {
        var location = Resolver(new() { ["WINDIR"] = windir }).Resolve(TempLocationKind.WindowsTemp);

        Assert.False(location.IsResolved);
        Assert.Null(location.Path);
        Assert.NotNull(location.ResolveError);
        Assert.Throws<InvalidOperationException>(() => location.ToFolderTarget());
    }

    [Fact]
    public void UnsafeVariable_FallsThroughToNextValidSource()
    {
        var location = Resolver(new() { ["WINDIR"] = @"..\Windows", ["SystemRoot"] = @"H:\Windows" }).Resolve(TempLocationKind.Prefetch);

        Assert.Equal(@"H:\Windows\Prefetch", location.Path);
    }

    [Fact]
    public void Prefetch_OnlyTargetsTopLevelPfFiles_AndNeedsAdmin()
    {
        var location = Resolver(new() { ["WINDIR"] = @"D:\Win" }).Resolve(TempLocationKind.Prefetch, TimeSpan.FromHours(24));
        var target = location.ToFolderTarget();

        Assert.Equal("*.pf", target.SearchPattern);
        Assert.False(target.Recursive);
        Assert.Equal(TimeSpan.Zero, target.MinAge);
        Assert.True(location.RequiresAdmin);
    }

    [Fact]
    public void LocationThatIsAJunction_IsRejected()
    {
        var root = TestFileSystem.NewRoot();
        var windows = Path.Combine(root, "Windows");
        var elsewhere = Path.Combine(root, "Elsewhere");
        Directory.CreateDirectory(windows);
        Directory.CreateDirectory(elsewhere);
        var temp = Path.Combine(windows, "Temp");
        TestFileSystem.CreateJunction(temp, elsewhere);
        try
        {
            var location = Resolver(new() { ["WINDIR"] = windows }).Resolve(TempLocationKind.WindowsTemp);

            Assert.False(location.IsResolved);
        }
        finally
        {
            TestFileSystem.RemoveJunction(temp);
            TestFileSystem.TryDeleteTree(root);
        }
    }

    [Fact]
    public void RealEnvironment_ResolvesAllThreeLocations()
    {
        var locations = new TempLocationResolver().ResolveAll(TimeSpan.Zero);

        Assert.Equal(3, locations.Count);
        Assert.All(locations, l => Assert.True(l.IsResolved, l.ResolveError));
        Assert.Equal(
            Path.Combine(Environment.GetEnvironmentVariable("WINDIR")!, "Prefetch"),
            locations.Single(l => l.Kind == TempLocationKind.Prefetch).Path,
            ignoreCase: true);
    }
}
