using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.Tests;

public sealed class CompanionPairingTests
{
    [Fact]
    public void PairingCode_IsOneTime_AndCreatesAuthorizedDevice()
    {
        var service = new CompanionPairingService();
        var code = service.CreateCode();

        var paired = service.Pair(code.Code, "Marcos's phone");

        Assert.Equal(64, paired.AccessToken.Length);
        Assert.Equal("Marcos's phone", service.Authorize(paired.AccessToken)?.Name);
        Assert.Throws<UnauthorizedAccessException>(() => service.Pair(code.Code, "Second phone"));
    }

    [Fact]
    public void InvalidToken_IsRejected_AndDeviceCanBeRevoked()
    {
        var service = new CompanionPairingService();
        var code = service.CreateCode();
        var paired = service.Pair(code.Code, "Phone");

        Assert.Null(service.Authorize("not-a-token"));
        Assert.True(service.Revoke(paired.Device.Id));
        Assert.Null(service.Authorize(paired.AccessToken));
    }

    [Fact]
    public void PairingCode_RejectsUnsafeLifetime()
    {
        var service = new CompanionPairingService();
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreateCode(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreateCode(TimeSpan.FromHours(1)));
    }
}
