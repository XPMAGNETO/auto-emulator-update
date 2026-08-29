using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class BackupService(AppPaths paths)
{
    public async Task<BackupRecord> BackupAsync(InstalledEmulator emulator, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var stamp = DateTimeOffset.Now;
        var safeVersion = string.IsNullOrWhiteSpace(emulator.CurrentVersion) ? "unknown" : emulator.CurrentVersion;
        var dest = Path.Combine(paths.BackupsRoot, emulator.Definition.Id, $"{stamp:yyyyMMdd_HHmmss}_{safeVersion}");
        await CopyTreeAsync(emulator.InstallPath, dest, progress, ct);
        return new BackupRecord(emulator.Definition.Name, safeVersion, stamp, dest);
    }

    public async Task RollbackAsync(InstalledEmulator emulator, BackupRecord backup, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(backup.Path)) throw new DirectoryNotFoundException(backup.Path);
        await CopyTreeAsync(backup.Path, emulator.InstallPath, progress, ct);
    }

    public IEnumerable<BackupRecord> List(string emulatorId, string emulatorName)
    {
        var root = Path.Combine(paths.BackupsRoot, emulatorId);
        if (!Directory.Exists(root)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(root).OrderByDescending(x => x))
        {
            var name = Path.GetFileName(dir);
            var parts = name.Split('_', 3);
            var stamp = Directory.GetCreationTimeUtc(dir);
            var version = parts.Length >= 3 ? parts[2] : "unknown";
            yield return new BackupRecord(emulatorName, version, stamp, dir);
        }
    }

    public void Cleanup(int retentionDays, double maxGb)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
        foreach (var dir in Directory.Exists(paths.BackupsRoot) ? Directory.EnumerateDirectories(paths.BackupsRoot, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length) : [])
        {
            try { if (Directory.GetLastWriteTimeUtc(dir) < cutoff) Directory.Delete(dir, true); } catch { }
        }
        var max = (long)(Math.Max(1, maxGb) * 1024 * 1024 * 1024);
        while (DirectorySize(paths.BackupsRoot) > max)
        {
            var oldest = Directory.EnumerateDirectories(paths.BackupsRoot, "*", SearchOption.AllDirectories)
                .OrderBy(Directory.GetLastWriteTimeUtc).FirstOrDefault();
            if (oldest is null) break;
            try { Directory.Delete(oldest, true); } catch { break; }
        }
    }

    private static async Task CopyTreeAsync(string source, string destination, IProgress<string>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            progress?.Report(Path.GetFileName(file));
            await using var input = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var output = File.Create(dest);
            await input.CopyToAsync(output, ct);
        }
    }

    private static long DirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try { foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) total += new FileInfo(f).Length; } catch { }
        return total;
    }
}
