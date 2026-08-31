using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AutoEmulatorUpdate.Core.Services;

public sealed record CompanionCertificate(X509Certificate2 Certificate, string Fingerprint);

public sealed class CompanionCertificateService
{
    public CompanionCertificate LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var certificate = File.Exists(path) ? Load(path) : Create(path);
        return new CompanionCertificate(certificate, Fingerprint(certificate));
    }

    private static X509Certificate2 Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var certificate = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.Exportable);
        if (!certificate.HasPrivateKey || certificate.NotAfter <= DateTime.UtcNow.AddDays(14))
            throw new CryptographicException("The saved companion certificate is invalid or near expiration.");
        return certificate;
    }

    private static X509Certificate2 Create(string path)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Auto Emulator Updater Companion",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        foreach (var address in LocalAddresses()) names.AddIpAddress(address);
        request.CertificateExtensions.Add(names.Build());

        var now = DateTimeOffset.UtcNow;
        using var generated = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(2));
        var bytes = generated.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, bytes);
        RestrictPermissions(path);
        return X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.Exportable);
    }

    private static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(entry => entry.Address)
            .Where(address => !IPAddress.IsLoopback(address))
            .Where(address => address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork
                or System.Net.Sockets.AddressFamily.InterNetworkV6)
            .Distinct();

    private static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));

    private static void RestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
