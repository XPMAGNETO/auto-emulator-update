namespace AutoEmulatorUpdate.Core.Services;

public sealed class AppLogService(AppPaths paths)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string CurrentLogFile { get; } =
        Path.Combine(paths.LogsRoot, $"AutoEmulatorUpdate-{DateTime.Now:yyyyMMdd}.log");

    public async Task WriteAsync(string message, CancellationToken ct = default)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
        await _gate.WaitAsync(ct);
        try { await File.AppendAllTextAsync(CurrentLogFile, line, ct); }
        finally { _gate.Release(); }
    }
}
