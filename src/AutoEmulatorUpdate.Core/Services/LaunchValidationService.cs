using System.Diagnostics;
using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class LaunchValidationService
{
    public async Task<LaunchValidationResult> ValidateAsync(
        InstalledEmulator emulator,
        TimeSpan observationWindow,
        CancellationToken ct = default)
    {
        var executable = ResolveExecutablePath(emulator);
        if (executable is null)
            return new LaunchValidationResult(false, "No executable could be resolved for the updated emulator.");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = emulator.InstallPath,
                UseShellExecute = false,
                CreateNoWindow = false
            });

            if (process is null)
                return new LaunchValidationResult(false, "The emulator process could not be started.");

            var exitTask = process.WaitForExitAsync(ct);
            var delayTask = Task.Delay(observationWindow, ct);
            var completed = await Task.WhenAny(exitTask, delayTask);

            if (completed == exitTask)
            {
                await exitTask;
                return process.ExitCode == 0
                    ? new LaunchValidationResult(true, "The emulator started and exited normally.", 0)
                    : new LaunchValidationResult(false, $"The emulator exited during startup with code {process.ExitCode}.", process.ExitCode);
            }

            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
                // A successful launch test must not be turned into a failure only because
                // the temporary validation process already closed or could not be terminated.
            }

            return new LaunchValidationResult(true, $"The emulator remained running for {observationWindow.TotalSeconds:0.#} seconds.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LaunchValidationResult(false, $"The emulator could not be launched: {ex.Message}");
        }
    }

    public string? ResolveExecutablePath(InstalledEmulator emulator)
    {
        if (!string.IsNullOrWhiteSpace(emulator.ExecutablePath) && File.Exists(emulator.ExecutablePath))
            return emulator.ExecutablePath;

        var key = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        if (!emulator.Definition.Executables.TryGetValue(key, out var names)) return null;

        foreach (var name in names)
        {
            var candidate = Path.Combine(emulator.InstallPath, name);
            if (File.Exists(candidate))
            {
                emulator.ExecutablePath = candidate;
                return candidate;
            }

            try
            {
                var recursive = Directory.EnumerateFiles(emulator.InstallPath, Path.GetFileName(name), SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (recursive is not null)
                {
                    emulator.ExecutablePath = recursive;
                    return recursive;
                }
            }
            catch
            {
                // Fall through to the next known executable name.
            }
        }

        return null;
    }
}
