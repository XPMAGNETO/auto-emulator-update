using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.Tests;

public sealed class CatalogTests
{
    [Fact]
    public async Task Bundled_Manifests_Are_Readable()
    {
        var paths = new AppPaths();
        var catalog = new CatalogService(paths);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "manifests", "emulators"));
        var defs = await catalog.LoadAsync(root);
        Assert.True(defs.Count >= 20);
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Id)));
    }
}
