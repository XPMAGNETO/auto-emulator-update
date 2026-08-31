using System.Net;
using System.Security.Cryptography.X509Certificates;
using AutoEmulatorUpdate.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AutoEmulatorUpdate.App;

public sealed record CompanionPairRequest(string Code, string DeviceName);
public sealed record CompanionPairResponse(string AccessToken, CompanionSnapshot Snapshot);
public sealed record CompanionCommandRequest(string Name);
public sealed record CompanionEmulatorStatus(string Name, string CurrentVersion, string AvailableVersion, string Status);
public sealed record CompanionActivityStatus(DateTimeOffset Timestamp, string Message);
public sealed record CompanionSnapshot(
    int InstalledCount,
    int UpdateCount,
    string StatusMessage,
    IReadOnlyList<CompanionEmulatorStatus> Emulators,
    IReadOnlyList<CompanionActivityStatus> Activity);

public sealed class CompanionHost : IAsyncDisposable
{
    public const int DefaultPort = 45831;
    private WebApplication? _application;

    public async Task StartAsync(
        X509Certificate2 certificate,
        CompanionPairingService pairing,
        Func<CompanionSnapshot> snapshot,
        Func<string, Task<CompanionSnapshot>> command,
        Action<CompanionDevice> devicePaired,
        int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        if (_application is not null) return;
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(pairing);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Any, port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1AndHttp2;
                listen.UseHttps(certificate);
            }));

        var app = builder.Build();
        app.MapPost("/api/companion/pair", (CompanionPairRequest request) =>
        {
            try
            {
                var paired = pairing.Pair(request.Code, request.DeviceName);
                devicePaired(paired.Device);
                return Results.Ok(new CompanionPairResponse(paired.AccessToken, snapshot()));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/companion/status", (HttpContext context) =>
            Authorize(context, pairing) is null ? Results.Unauthorized() : Results.Ok(snapshot()));

        app.MapPost("/api/companion/commands", async (HttpContext context, CompanionCommandRequest request) =>
        {
            if (Authorize(context, pairing) is null) return Results.Unauthorized();
            if (request.Name is not ("check-all" or "update-all"))
                return Results.BadRequest(new { error = "Unsupported companion command." });
            return Results.Ok(await command(request.Name));
        });

        await app.StartAsync(cancellationToken);
        _application = app;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_application is null) return;
        var app = _application;
        _application = null;
        await app.StopAsync(cancellationToken);
        await app.DisposeAsync();
    }

    public ValueTask DisposeAsync() => _application is null
        ? ValueTask.CompletedTask
        : new ValueTask(StopAsync());

    private static CompanionDevice? Authorize(HttpContext context, CompanionPairingService pairing)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? pairing.Authorize(header[prefix.Length..])
            : null;
    }
}
