using System.Net;
using AutoEmulatorUpdate.Core.Models;
using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.Tests;

public sealed class EasyModeTests
{
    [Fact]
    public void Default_Settings_Are_Safe_And_Simple()
    {
        var s = new AppSettings();
        Assert.Equal(UserExperienceMode.Simple, s.ExperienceMode);
        Assert.Equal(MaintenanceMode.BackupUpdateVerify, s.MaintenanceMode);
        Assert.True(s.AutoRollbackOnValidationFailure);
        Assert.Equal(StartupBehavior.CheckOnLaunch, s.StartupBehavior);
    }

    [Fact]
    public void Friendly_Error_Hides_Http_Jargon_From_Main_Message()
    {
        var service = new FriendlyErrorService();
        var result = service.Present(
            new HttpRequestException("403 test", null, HttpStatusCode.Forbidden),
            "Dolphin");

        Assert.Contains("rejected", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpRequestException", result.Message);
        Assert.Contains("HttpRequestException", result.TechnicalDetails);
    }
}
