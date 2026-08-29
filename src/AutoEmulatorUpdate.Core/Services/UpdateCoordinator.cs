using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class UpdateCoordinator(
    ReleaseResolverService resolver,
    DownloadService downloader,
    ArchiveService archives,
    BackupService backups,
    JsonStore store,
    AppPaths paths,
    VersionService versions)
{
    public async Task<ReleaseInfo> CheckAsync(InstalledEmulator emulator, CancellationToken ct = default)
    {
        var release = await resolver.ResolveAsync(emulator.Definition, emulator.Channel, ct);
        emulator.LatestVersion = release.Version;
        emulator.SourceName = release.Source;
        emulator.SourceHealth = SourceHealthState.Healthy;
        emulator.Status = versions.Compare(emulator.CurrentVersion, release.Version) < 0 ? "Update available" : "Up to date";
        if (!string.IsNullOrWhiteSpace(emulator.PinnedVersion)) emulator.Status = "Pinned";
        if (string.Equals(emulator.IgnoredVersion, release.Version, StringComparison.OrdinalIgnoreCase)) emulator.Status = "Ignored";
        return release;
    }

    public async Task ProcessAsync(
        UpdateQueueItem item,
        AppSettings settings,
        IProgress<(double percent, string message)>? progress = null,
        CancellationToken ct = default)
    {
        item.State = QueueState.Running;
        var emu = item.Emulator;
        BackupRecord? backup = null;
        ReleaseInfo? release = null;

        try
        {
            release = await resolver.ResolveAsync(emu.Definition, emu.Channel, ct);
            var old = emu.CurrentVersion;

            if (item.Action == QueueAction.DownloadOnly)
            {
                await downloader.DownloadAsync(emu.Definition.Id, release, settings.BandwidthLimitMBps,
                    new Progress<double>(p => progress?.Report((p, $"Downloading {emu.Definition.Name}"))), ct);
                item.State = QueueState.Complete;
                item.Message = "Downloaded";
                return;
            }

            if (item.Action != QueueAction.Install)
            {
                backup = await backups.BackupAsync(emu,
                    new Progress<string>(m => progress?.Report((0, $"Backing up {m}"))), ct);
            }

            EnsureDiskSpace(emu.InstallPath, release.SizeBytes);
            var package = await downloader.DownloadAsync(
                emu.Definition.Id, release, settings.BandwidthLimitMBps,
                new Progress<double>(p => progress?.Report((p * .6, $"Downloading {emu.Definition.Name}"))), ct);

            var staging = Path.Combine(Path.GetTempPath(), "AEU", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                await archives.ExtractAsync(package, staging,
                    new Progress<string>(m => progress?.Report((65, m))), ct);
                var payloadRoot = CollapseSingleDirectory(staging);
                await CopyTreeAsync(payloadRoot, emu.InstallPath,
                    new Progress<double>(p => progress?.Report((65 + p * .35, "Installing"))), ct);

                if (!Validate(emu))
                {
                    if (settings.AutoRollbackOnValidationFailure && backup is not null)
                        await backups.RollbackAsync(emu, backup, null, ct);
                    throw new InvalidDataException("Post-update validation failed: executable was not found.");
                }

                emu.CurrentVersion = release.Version;
                emu.LatestVersion = release.Version;
                emu.Status = "Up to date";
                item.State = QueueState.Complete;
                item.Progress = 100;
                item.Message = "Complete";

                await store.AppendJsonLineAsync(paths.HistoryFile,
                    new HistoryEntry(DateTimeOffset.Now, emu.Definition.Name, item.Action.ToString(), old, release.Version, "Success", release.Source, backup?.Path), ct);
            }
            finally { try { Directory.Delete(staging, true); } catch { } }
        }
        catch (OperationCanceledException)
        {
            item.State = QueueState.Cancelled; item.Message = "Cancelled"; throw;
        }
        catch (Exception ex)
        {
            item.State = QueueState.Failed; item.Message = ex.Message;
            await store.AppendJsonLineAsync(paths.FailureFile,
                new FailureEntry(DateTimeOffset.Now, emu.Definition.Name, item.Action.ToString(), ex.Message, release?.Source), ct);
            throw;
        }
    }

    private static bool Validate(InstalledEmulator emu)
    {
        if (emu.ExecutablePath is not null && File.Exists(emu.ExecutablePath)) return true;
        return emu.Definition.Executables.Values.SelectMany(x => x)
            .Any(x => File.Exists(Path.Combine(emu.InstallPath, x)));
    }

    private static void EnsureDiskSpace(string target, long? packageBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(target))!;
        var drive = new DriveInfo(root);
        var need = Math.Max(packageBytes.GetValueOrDefault(512L * 1024 * 1024) * 3, 512L * 1024 * 1024);
        if (drive.AvailableFreeSpace < need)
            throw new IOException($"Not enough free space. Need about {need / 1024d / 1024d / 1024d:F2} GB.");
    }

    private static string CollapseSingleDirectory(string dir)
    {
        var files = Directory.EnumerateFiles(dir).Any();
        var dirs = Directory.EnumerateDirectories(dir).ToArray();
        return !files && dirs.Length == 1 ? dirs[0] : dir;
    }

    private static async Task CopyTreeAsync(string source, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
        for (var i = 0; i < files.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(dest, Path.GetRelativePath(source, files[i]));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = File.OpenRead(files[i]);
            await using var output = File.Create(target);
            await input.CopyToAsync(output, ct);
            progress?.Report((i + 1) * 100d / Math.Max(1, files.Length));
        }
    }
}
