using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.Tests;

public sealed class CompanionCertificateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aeu-cert-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Identity_IsCreatedAndPersistsAcrossLoads()
    {
        var path = Path.Combine(_root, "identity.pfx");
        var service = new CompanionCertificateService();

        using var first = service.LoadOrCreate(path).Certificate;
        var firstFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(first.RawData));
        using var second = service.LoadOrCreate(path).Certificate;
        var secondIdentity = service.LoadOrCreate(path);
        using var third = secondIdentity.Certificate;

        Assert.True(first.HasPrivateKey);
        Assert.Equal(first.Thumbprint, second.Thumbprint);
        Assert.Equal(firstFingerprint, secondIdentity.Fingerprint);
        Assert.Equal(64, secondIdentity.Fingerprint.Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
