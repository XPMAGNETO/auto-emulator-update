using System.Net;
using System.Security.Cryptography;
using System.Text;
using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.Tests;

public sealed class SelfUpdateServiceTests
{
    [Fact]
    public async Task DownloadAndVerifyAsync_AcceptsMatchingSha256()
    {
        var payload = Encoding.UTF8.GetBytes("auto-emulator-updater-package");
        var expected = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var handler = new StubHandler(request =>
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes($"{expected}  package.exe\n")
                : payload;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        });
        using var http = new HttpClient(handler);
        var service = new SelfUpdateService(http);
        var temp = Path.Combine(Path.GetTempPath(), $"aeu-selfupdate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);

        try
        {
            var result = await service.DownloadAndVerifyAsync(
                new SelfUpdateInfo("10.1.0-alpha.6", "https://example.test/package.exe", "https://example.test/package.exe.sha256", "package.exe"),
                temp,
                null,
                CancellationToken.None);

            Assert.True(result.ChecksumVerified);
            Assert.Equal(payload, await File.ReadAllBytesAsync(result.PackagePath));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_RejectsMismatchedSha256()
    {
        var payload = Encoding.UTF8.GetBytes("tampered-package");
        var handler = new StubHandler(request =>
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes($"{new string('0', 64)}  package.exe\n")
                : payload;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        });
        using var http = new HttpClient(handler);
        var service = new SelfUpdateService(http);
        var temp = Path.Combine(Path.GetTempPath(), $"aeu-selfupdate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndVerifyAsync(
                new SelfUpdateInfo("10.1.0-alpha.6", "https://example.test/package.exe", "https://example.test/package.exe.sha256", "package.exe"),
                temp,
                null,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
