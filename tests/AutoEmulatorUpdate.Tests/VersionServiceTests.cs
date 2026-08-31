using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.Tests;

public sealed class VersionServiceTests
{
    private readonly VersionService _sut = new();

    [Theory]
    [InlineData("0.8.136", "v0.8.136", 0)]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("2606", "2606a", -1)]
    [InlineData("2606a", "2606a", 0)]
    [InlineData("2606a", "2606-328", 1)]
    public void Compare_Works(string current, string latest, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(_sut.Compare(current, latest)));
    }

    [Theory]
    [InlineData("Dolphin 2606a", "2606a")]
    [InlineData("RPCS3 0.0.37-12345", "0.0.37-12345")]
    [InlineData("v1.2.3", "1.2.3")]
    public void Extract_Works(string input, string expected)
        => Assert.Equal(expected, _sut.Extract(input));
}

public sealed class DiscoveryVersionTests
{
    [Fact]
    public async Task DetectVersionAsync_UsesNearbyVersionFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "aeu-version-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "sample.exe" : "sample");
            await File.WriteAllTextAsync(executable, "not an executable");
            await File.WriteAllTextAsync(Path.Combine(root, "version.txt"), "Sample Emulator v2.7.4");
            var service = new DiscoveryService(new PlatformService(), new VersionService());

            var result = await service.DetectVersionAsync(executable);

            Assert.Equal("2.7.4", result.version);
            Assert.Contains("Version file", result.method);
            Assert.Equal("High", result.confidence);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
