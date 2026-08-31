using System.Net;
using System.Security.Cryptography;
using System.Text;
using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.Tests;

public sealed class SelfUpdateServiceTests
{
    [Theory]
    [InlineData(false, "10.1.0", true)]
    [InlineData(true, "10.1.0", false)]
    [InlineData(true, "10.1.0-alpha.14", true)]
    [InlineData(true, "10.1.0-rc.1", true)]
    public void ReleaseChannel_PreventsStableBuildsFromReceivingPrereleases(
        bool prerelease, string currentVersion, bool expected)
    {
        Assert.Equal(expected, SelfUpdateService.ShouldConsiderRelease(prerelease, currentVersion));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_AcceptsMatchingSha256()
    {
        var payload = Encoding.UTF8.GetBytes("auto-emulator-updater-package");
        var expected = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var version = $"99.9.0-test-{Guid.NewGuid():N}";
        var handler = new StubHandler(request =>
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes($"{expected}  package.exe\n")
                : payload;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        });
        using var http = new HttpClient(handler);
        var service = new SelfUpdateService(http);

        var result = await service.DownloadAndVerifyAsync(
            new AppUpdateInfo(version, "https://example.test/package.exe", "package.exe", "https://example.test/release", "https://example.test/package.exe.sha256", "package.exe.sha256"),
            null,
            CancellationToken.None);

        try
        {
            Assert.True(result.ChecksumVerified);
            Assert.Equal(payload, await File.ReadAllBytesAsync(result.FilePath));
        }
        finally
        {
            var directory = Path.GetDirectoryName(result.FilePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_RejectsMismatchedSha256()
    {
        var payload = Encoding.UTF8.GetBytes("tampered-package");
        var version = $"99.9.0-test-{Guid.NewGuid():N}";
        var handler = new StubHandler(request =>
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes($"{new string('0', 64)}  package.exe\n")
                : payload;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        });
        using var http = new HttpClient(handler);
        var service = new SelfUpdateService(http);
        var directory = Path.Combine(Path.GetTempPath(), "AutoEmulatorUpdate", "updates", version);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndVerifyAsync(
                new AppUpdateInfo(version, "https://example.test/package.exe", "package.exe", "https://example.test/release", "https://example.test/package.exe.sha256", "package.exe.sha256"),
                null,
                CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
