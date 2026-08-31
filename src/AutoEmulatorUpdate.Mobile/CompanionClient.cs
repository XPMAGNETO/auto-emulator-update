using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace AutoEmulatorUpdate.Mobile;

public sealed class CompanionClient(HttpClient httpClient)
{
    private Uri? _baseAddress;
    private string? _accessToken;

    public async Task<CompanionSnapshot> PairAsync(string address, string code, CancellationToken cancellationToken = default)
    {
        _baseAddress = ValidateAddress(address);
        var response = await httpClient.PostAsJsonAsync(
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

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CompanionSnapshot>(cancellationToken)
            ?? throw new InvalidOperationException("The desktop returned an empty status response.");
    }

    private static Uri ValidateAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri))
            throw new ArgumentException("Enter a valid computer address.");

        var isLocalDevelopment = uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp;
        if (uri.Scheme != Uri.UriSchemeHttps && !isLocalDevelopment)
            throw new ArgumentException("A secure HTTPS computer address is required.");
        return uri;
    }
}
