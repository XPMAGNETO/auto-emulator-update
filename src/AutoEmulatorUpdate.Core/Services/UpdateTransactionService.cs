using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed record UpdateSwapHandle(string InstallPath, string? PreviousPath, bool HadExistingInstall);

public sealed class UpdateTransactionService
{
    public async Task<string> PrepareCandidateAsync(
        InstalledEmulator emulator,
        string payloadRoot,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var installPath = Path.GetFullPath(emulator.InstallPath);
        var parent = Directory.GetParent(installPath)?.FullName
                     ?? throw new InvalidOperationException("The emulator install folder has no parent directory.");
        Directory.CreateDirectory(parent);

        var candidate = Path.Combine(parent, $".aeu-{emulator.Definition.Id}-{Guid.NewGuid():N}-candidate");
        Directory.CreateDirectory(candidate);

        try
        {
            if (Directory.Exists(installPath))
                await CopyTreeAsync(installPath, candidate, null, null, ct);

            var protectedPaths = emulator.Definition.ProtectedPaths
                .Concat(emulator.Definition.SavePaths)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeRelative)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await CopyTreeAsync(payloadRoot, candidate, protectedPaths, progress, ct);
            return candidate;
        }
        catch
        {
            TryDeleteDirectory(candidate);
            throw;
        }
    }

    public UpdateSwapHandle Commit(string installPath, string candidatePath)
    {
        installPath = Path.GetFullPath(installPath);
        candidatePath = Path.GetFullPath(candidatePath);
        if (!Directory.Exists(candidatePath)) throw new DirectoryNotFoundException(candidatePath);

        var parent = Directory.GetParent(installPath)?.FullName
                     ?? throw new InvalidOperationException("The emulator install folder has no parent directory.");
        var hadExisting = Directory.Exists(installPath);
        var previous = hadExisting
            ? Path.Combine(parent, $".aeu-{Path.GetFileName(installPath)}-{Guid.NewGuid():N}-previous")
            : null;

        if (hadExisting) Directory.Move(installPath, previous!);
        try
        {
            Directory.Move(candidatePath, installPath);
            return new UpdateSwapHandle(installPath, previous, hadExisting);
        }
        catch
        {
            if (hadExisting && previous is not null && Directory.Exists(previous) && !Directory.Exists(installPath))
                Directory.Move(previous, installPath);
            throw;
        }
    }

    public Task CompleteAsync(UpdateSwapHandle handle)
    {
        if (handle.PreviousPath is not null) TryDeleteDirectory(handle.PreviousPath);
        return Task.CompletedTask;
    }

    public Task RollbackAsync(UpdateSwapHandle handle)
    {
        if (Directory.Exists(handle.InstallPath))
            TryDeleteDirectory(handle.InstallPath);

        if (handle.HadExistingInstall)
        {
            if (handle.PreviousPath is null || !Directory.Exists(handle.PreviousPath))
                throw new DirectoryNotFoundException("The pre-update transaction snapshot is missing.");
            Directory.Move(handle.PreviousPath, handle.InstallPath);
        }

        return Task.CompletedTask;
    }

    public static bool ValidateCandidate(InstalledEmulator emulator, string candidatePath)
    {
        if (!string.IsNullOrWhiteSpace(emulator.ExecutablePath))
        {
            try
            {
                var relative = Path.GetRelativePath(emulator.InstallPath, emulator.ExecutablePath);
                if (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                    !Path.IsPathRooted(relative) &&
                    File.Exists(Path.Combine(candidatePath, relative)))
                    return true;
            }
            catch
            {
                // Fall back to manifest executable names.
            }
        }

        return emulator.Definition.Executables.Values.SelectMany(x => x)
            .Any(name => File.Exists(Path.Combine(candidatePath, name)) ||
                         SafeFind(candidatePath, Path.GetFileName(name)) is not null);
    }

    private static async Task CopyTreeAsync(
        string source,
        string destination,
        IReadOnlyCollection<string>? protectedPaths,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        Directory.CreateDirectory(destination);
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();

        for (var i = 0; i < files.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, files[i]);
            if (protectedPaths is not null && IsProtected(relative, protectedPaths))
            {
                progress?.Report((i + 1) * 100d / Math.Max(1, files.Length));
                continue;
            }

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = File.Open(files[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var output = File.Create(target);
            await input.CopyToAsync(output, ct);
            progress?.Report((i + 1) * 100d / Math.Max(1, files.Length));
        }
    }

    private static bool IsProtected(string relativePath, IEnumerable<string> protectedPaths)
    {
        var relative = NormalizeRelative(relativePath);
        foreach (var protectedPath in protectedPaths)
        {
            if (relative.Equals(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith(protectedPath + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string NormalizeRelative(string path) =>
        path.Replace('\\', '/').TrimStart('/').TrimEnd('/');

    private static string? SafeFind(string root, string fileName)
    {
        try { return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault(); }
        catch { return null; }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // Cleanup is best effort. Recovery code keeps the previous directory until success.
        }
    }
}
