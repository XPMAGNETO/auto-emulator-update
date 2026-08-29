using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class UpdateCoordinator(
    ReleaseResolverService resolver,
    DownloadService downloader,
    ArchiveService archives,
    BackupService backups,
    JsonStore store,
    AppPaths paths,
    VersionService versions,
    UpdateTransactionService? transactions = null,
    LaunchValidationService? launchValidator = null)
{
    private readonly UpdateTransactionService _transactions = transactions ?? new UpdateTransactionService();
    private readonly LaunchValidationService _launchValidator = launchValidator ?? new LaunchValidationService();

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
        UpdateSwapHandle? swap = null;
        string? candidate = null;
        string stage = "Resolving release";
        var oldVersion = emu.CurrentVersion;

        try
        {
            release = await resolver.ResolveAsync(emu.Definition, emu.Channel, ct);

            if (item.Action == QueueAction.DownloadOnly)
            {
                stage = "Downloading";
                await downloader.DownloadAsync(emu.Definition.Id, release, settings.BandwidthLimitMBps,
                    new Progress<double>(p => progress?.Report((p, $"Downloading {emu.Definition.Name}"))), ct);
                item.State = QueueState.Complete;
                item.Message = "Downloaded";
                return;
            }

            if (item.Action != QueueAction.Install && Directory.Exists(emu.InstallPath))
            {
                stage = "Backing up";
                backup = await backups.BackupAsync(emu,
                    new Progress<string>(m => progress?.Report((Math.Min(item.Progress, 10), $"Backing up {m}"))), ct);
            }

            stage = "Checking disk space";
            EnsureDiskSpace(emu.InstallPath, release.SizeBytes);

            stage = "Downloading";
            var package = await downloader.DownloadAsync(
                emu.Definition.Id, release, settings.BandwidthLimitMBps,
                new Progress<double>(p => progress?.Report((p * .5, $"Downloading {emu.Definition.Name}"))), ct);

            var extraction = Path.Combine(Path.GetTempPath(), "AEU", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extraction);
            try
            {
                stage = "Extracting";
                await archives.ExtractAsync(package, extraction,
                    new Progress<string>(m => progress?.Report((55, m))), ct);
                var payloadRoot = CollapseSingleDirectory(extraction);

                stage = "Preparing transaction";
                candidate = await _transactions.PrepareCandidateAsync(
                    emu,
                    payloadRoot,
                    new Progress<double>(p => progress?.Report((55 + p * .25, "Preparing safe update"))),
                    ct);

                stage = "Validating staged update";
                if (!UpdateTransactionService.ValidateCandidate(emu, candidate))
                    throw new InvalidDataException("Staged update validation failed: the emulator executable was not found in the candidate installation.");

                stage = "Committing transaction";
                progress?.Report((82, "Switching to the new version"));
                swap = _transactions.Commit(emu.InstallPath, candidate);
                candidate = null;

                emu.CurrentVersion = release.Version;
                emu.LatestVersion = release.Version;
                emu.Status = "Validating";

                if (settings.PostUpdateLaunchTest)
                {
                    stage = "Launch validation";
                    progress?.Report((90, "Launching emulator for post-update validation"));
                    var seconds = Math.Clamp(settings.PostUpdateLaunchTestSeconds, 2, 30);
                    var launch = await _launchValidator.ValidateAsync(emu, TimeSpan.FromSeconds(seconds), ct);
                    if (!launch.Success)
                        throw new InvalidDataException($"Post-update launch test failed: {launch.Message}");
                }

                stage = "Finalizing transaction";
                await _transactions.CompleteAsync(swap);
                swap = null;

                emu.Status = "Up to date";
                item.State = QueueState.Complete;
                item.Progress = 100;
                item.Message = settings.PostUpdateLaunchTest ? "Complete - launch verified" : "Complete - files verified";
                progress?.Report((100, item.Message));

                await store.AppendJsonLineAsync(paths.HistoryFile,
                    new HistoryEntry(DateTimeOffset.Now, emu.Definition.Name, item.Action.ToString(), oldVersion, release.Version,
                        settings.PostUpdateLaunchTest ? "Success - launch verified" : "Success - files verified", release.Source, backup?.Path), ct);
            }
            finally
            {
                try { Directory.Delete(extraction, true); } catch { }
                if (candidate is not null)
                {
                    try { Directory.Delete(candidate, true); } catch { }
                }
            }
        }
        catch (OperationCanceledException)
        {
            var rollbackMessage = await TryRollbackAsync(emu, swap, backup);
            emu.CurrentVersion = oldVersion;
            emu.Status = rollbackMessage is null ? "Cancelled" : "Rolled back";
            item.State = QueueState.Cancelled;
            item.Message = rollbackMessage is null ? "Cancelled" : $"Cancelled - {rollbackMessage}";
            await store.AppendJsonLineAsync(paths.FailureFile,
                new FailureEntry(DateTimeOffset.Now, emu.Definition.Name, stage, item.Message, release?.Source), CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            var rollbackMessage = await TryRollbackAsync(emu, swap, backup);
            emu.CurrentVersion = oldVersion;
            emu.Status = rollbackMessage is null ? "Needs attention" : "Rolled back";
            item.State = QueueState.Failed;
            item.Message = rollbackMessage is null ? ex.Message : $"{ex.Message} {rollbackMessage}";
            await store.AppendJsonLineAsync(paths.FailureFile,
                new FailureEntry(DateTimeOffset.Now, emu.Definition.Name, stage, item.Message, release?.Source), CancellationToken.None);
            throw new InvalidOperationException(item.Message, ex);
        }
    }

    private async Task<string?> TryRollbackAsync(InstalledEmulator emulator, UpdateSwapHandle? swap, BackupRecord? backup)
    {
        if (swap is null) return null;

        try
        {
            await _transactions.RollbackAsync(swap);
            return "The previous installation was restored automatically.";
        }
        catch (Exception transactionError)
        {
            if (backup is not null)
            {
                try
                {
                    await backups.RollbackAsync(emulator, backup, null, CancellationToken.None);
                    return $"The transaction rollback failed ({transactionError.Message}), but the backup was restored successfully.";
                }
                catch (Exception backupError)
                {
                    return $"Automatic rollback failed. Transaction error: {transactionError.Message}. Backup restore error: {backupError.Message}.";
                }
            }

            return $"Automatic rollback failed: {transactionError.Message}.";
        }
    }

    private static void EnsureDiskSpace(string target, long? packageBytes)
    {
        var fullTarget = Path.GetFullPath(target);
        var root = Path.GetPathRoot(fullTarget)!;
        var drive = new DriveInfo(root);
        var currentInstallBytes = DirectorySize(fullTarget);
        var packageEstimate = packageBytes.GetValueOrDefault(512L * 1024 * 1024);
        var need = Math.Max(currentInstallBytes * 2 + packageEstimate * 2, 768L * 1024 * 1024);
        if (drive.AvailableFreeSpace < need)
            throw new IOException($"Not enough free space for a safe transactional update. Need about {need / 1024d / 1024d / 1024d:F2} GB.");
    }

    private static long DirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                total += new FileInfo(file).Length;
        }
        catch
        {
            // A conservative package-size estimate still applies if one file cannot be measured.
        }
        return total;
    }

    private static string CollapseSingleDirectory(string dir)
    {
        var files = Directory.EnumerateFiles(dir).Any();
        var dirs = Directory.EnumerateDirectories(dir).ToArray();
        return !files && dirs.Length == 1 ? dirs[0] : dir;
    }
}
