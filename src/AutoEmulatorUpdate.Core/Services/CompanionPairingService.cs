using System.Security.Cryptography;
using System.Text;

namespace AutoEmulatorUpdate.Core.Services;

public sealed record CompanionPairingCode(string Code, DateTimeOffset ExpiresAt);
public sealed record CompanionDevice(string Id, string Name, DateTimeOffset PairedAt, DateTimeOffset LastSeenAt);
public sealed record CompanionPairingResult(string AccessToken, CompanionDevice Device);

public sealed class CompanionPairingService
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly Dictionary<string, AuthorizedDevice> _devices = new(StringComparer.Ordinal);
    private string? _codeHash;
    private DateTimeOffset _codeExpiresAt;

    public CompanionPairingService(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    public CompanionPairingCode CreateCode(TimeSpan? lifetime = null)
    {
        var duration = lifetime ?? TimeSpan.FromMinutes(5);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Pairing codes must last between 1 second and 15 minutes.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        lock (_gate)
        {
            _codeHash = Hash(code);
            _codeExpiresAt = _time.GetUtcNow().Add(duration);
        }
        return new CompanionPairingCode(code, _codeExpiresAt);
    }

    public CompanionPairingResult Pair(string code, string deviceName)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Enter the pairing code.", nameof(code));
        if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("A device name is required.", nameof(deviceName));

        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_codeHash is null || now > _codeExpiresAt || !FixedTimeEquals(_codeHash, Hash(code.Trim())))
                throw new UnauthorizedAccessException("The pairing code is invalid or expired.");

            // A one-time code is consumed even if the client loses the response.
            _codeHash = null;
            _codeExpiresAt = default;

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var device = new CompanionDevice(Guid.NewGuid().ToString("N"), deviceName.Trim(), now, now);
            _devices[device.Id] = new AuthorizedDevice(device, Hash(token));
            return new CompanionPairingResult(token, device);
        }
    }

    public CompanionDevice? Authorize(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return null;
        var tokenHash = Hash(accessToken.Trim());
        lock (_gate)
        {
            foreach (var (id, authorized) in _devices)
            {
                if (!FixedTimeEquals(authorized.TokenHash, tokenHash)) continue;
                var updated = authorized.Device with { LastSeenAt = _time.GetUtcNow() };
                _devices[id] = authorized with { Device = updated };
                return updated;
            }
        }
        return null;
    }

    public IReadOnlyList<CompanionDevice> GetDevices()
    {
        lock (_gate) return _devices.Values.Select(x => x.Device).OrderBy(x => x.Name).ToArray();
    }

    public bool Revoke(string deviceId)
    {
        lock (_gate) return _devices.Remove(deviceId);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));

    private sealed record AuthorizedDevice(CompanionDevice Device, string TokenHash);
}
