using System.Net;

namespace AutoEmulatorUpdate.Core.Services;

public sealed record FriendlyError(string Title, string Message, string TechnicalDetails);

public sealed class FriendlyErrorService
{
    public FriendlyError Present(Exception ex, string? emulator = null)
    {
        var name = string.IsNullOrWhiteSpace(emulator) ? "The emulator" : emulator;

        return ex switch
        {
            HttpRequestException http when http.StatusCode == HttpStatusCode.Forbidden =>
                new("Update source blocked the request",
                    $"{name}'s update server rejected the automated request. Auto Emulator Update will use another source when one is available.",
                    ex.ToString()),

            HttpRequestException =>
                new("Update server could not be reached",
                    $"{name}'s update information could not be downloaded. Your existing installation was not changed.",
                    ex.ToString()),

            IOException io when io.Message.Contains("free space", StringComparison.OrdinalIgnoreCase) =>
                new("Not enough disk space",
                    "Free some disk space and try the update again. Nothing was installed.",
                    ex.ToString()),

            InvalidDataException data when data.Message.Contains("verification", StringComparison.OrdinalIgnoreCase) =>
                new("Downloaded update could not be verified",
                    "The downloaded package did not pass verification, so it was not installed.",
                    ex.ToString()),

            _ => new("Something went wrong",
                "Auto Emulator Update could not complete this operation. Your existing emulator files were left alone or restored when a backup was available.",
                ex.ToString())
        };
    }
}
