using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AutoEmulatorUpdate.Mobile;

public sealed class CompanionClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private Uri? _baseAddress;
    private string? _accessToken;
    private byte[]? _certificatePin;

    public CompanionClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = ValidateCertificate
        };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<CompanionSnapshot> PairAsync(string address, string code, CancellationToken cancellationToken = default)
    {
        (_baseAddress, _certificatePin) = ValidatePairingAddress(address);
        var response = await _httpClient.PostAsJsonAsync(
            new Uri(_baseAddress, "api/companion/pair"),
            new PairRequest(code.Trim(), Environment.MachineName),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PairResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The desktop returned an empty pairing response.");
        _accessToken = result.AccessToken;
        return result.Snapshot;
    }

    public Task<CompanionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "api/companion/status", null, cancellationToken);

    public Task<CompanionSnapshot> RunCommandAsync(string command, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/companion/commands", new CompanionCommand(command), cancellationToken);

    private async Task<CompanionSnapshot> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        if (_baseAddress is null || string.IsNullOrWhiteSpace(_accessToken))
            throw new InvalidOperationException("Pair this device before sending commands.");

        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CompanionSnapshot>(cancellationToken)
            ?? throw new InvalidOperationException("The desktop returned an empty status response.");
    }

    public void Dispose() => _httpClient.Dispose();

    private bool ValidateCertificate(HttpRequestMessage request, X509Certificate2? certificate,
        X509Chain? chain, System.Net.Security.SslPolicyErrors errors)
    {
        if (certificate is null || _certificatePin is null) return false;
        var actual = SHA256.HashData(certificate.RawData);
        return actual.Length == _certificatePin.Length &&
               CryptographicOperations.FixedTimeEquals(actual, _certificatePin);
    }

    private static (Uri Address, byte[] Pin) ValidatePairingAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("Enter the complete pairing address shown by the desktop app.");

        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("A secure HTTPS computer address is required.");

        var pinText = uri.Fragment.TrimStart('#');
        if (pinText.Length != 64 || !pinText.All(Uri.IsHexDigit))
            throw new ArgumentException("The pairing address is missing its certificate pin.");

        var builder = new UriBuilder(uri) { Fragment = "", Path = "/" };
        return (builder.Uri, Convert.FromHexString(pinText));
    }
}
