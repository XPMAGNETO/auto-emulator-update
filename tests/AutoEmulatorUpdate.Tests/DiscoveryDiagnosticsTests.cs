using AutoEmulatorUpdate.Core.Models;
using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.Tests;

public sealed class DiscoveryDiagnosticsTests
{
    [Fact]
    public async Task Scan_records_roots_expected_names_and_detection()
    {
        var root = Path.Combine(Path.GetTempPath(), "aeu-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "sampleemu");
            await File.WriteAllTextAsync(executable, "not-an-executable");
            var definition = new EmulatorDefinition
            {
                Id = "sample",
                Name = "Sample Emulator",
                Executables = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["windows"] = ["sampleemu"],
                    ["linux"] = ["sampleemu"],
                    ["macos"] = ["sampleemu"]
                }
            };

            var platform = new PlatformService();
            var service = new DiscoveryService(platform, new VersionService());
            var found = await service.ScanAsync([definition], [root]);

            Assert.Single(found);
            Assert.Contains(service.LastDiagnostics.RequestedRoots, x => Path.GetFullPath(x).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(service.LastDiagnostics.ScannedDirectories, x => Path.GetFullPath(x).Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
            Assert.Equal(["sampleemu"], service.LastDiagnostics.ExpectedExecutables["sample"]);
            Assert.Contains("sample", service.LastDiagnostics.DetectedDefinitions, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Formatter_sanitizes_home_and_explains_missing_emulator()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = Path.Combine(home, "Games", "Emulators");
        var definition = new EmulatorDefinition
        {
            Id = "missing",
            Name = "Missing Emulator",
            Executables = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["windows"] = ["missing.exe"],
                ["linux"] = ["missing"],
                ["macos"] = ["Missing.app"]
            }
        };
        var platform = new PlatformService();
        var diagnostics = new DiscoveryScanDiagnostics(
            DateTimeOffset.UtcNow,
            platform.PlatformName,
            platform.RuntimeId,
            [root], [], [root],
            [new DiscoveryDiagnosticEntry(Path.Combine(root, ".cache"), "Excluded system/cache/problem directory")],
            [], [],
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["missing"] = definition.Executables.TryGetValue(platform.Os, out var exes) ? exes : []
            },
            []);

        var report = DiscoveryDiagnosticsFormatter.Build(diagnostics, [definition], [], []);

        Assert.Contains("WHY WASN'T THIS FOUND?", report);
        Assert.Contains("Not detected", report);
        Assert.Contains("Expected:", report);
        Assert.Contains("Frontend names considered", report);
        if (!string.IsNullOrWhiteSpace(home)) Assert.DoesNotContain(home, report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~", report);
    }

    [Theory]
    [InlineData("ID=steamos", "steamos")]
    [InlineData("ID=pop", "pop")]
    [InlineData("ID=cachyos", "cachyos")]
    [InlineData("ID=batocera", "batocera")]
    public void Linux_distribution_variants_are_identified_for_platform_diagnostics(string line, string expected)
    {
        Assert.Equal(expected, PlatformService.IdentifyLinuxDistribution([line]));
    }
}
