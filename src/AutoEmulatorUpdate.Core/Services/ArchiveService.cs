using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class ArchiveService
{
    public async Task ExtractAsync(string archive, string destination, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destination);
        var lower = archive.ToLowerInvariant();

        if (lower.EndsWith(".zip"))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, destination, true), ct);
            return;
        }

        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz"))
        {
            await using var fs = File.OpenRead(archive);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gz, destination, true);
            return;
        }

        if (lower.EndsWith(".tar"))
        {
            await Task.Run(() => TarFile.ExtractToDirectory(archive, destination, true), ct);
            return;
        }

        if (lower.EndsWith(".7z"))
        {
            var seven = FindSevenZip();
            if (seven is null) throw new InvalidOperationException("7-Zip/7zz is required to extract .7z archives.");
            await RunProcessAsync(seven, $"x \"{archive}\" -o\"{destination}\" -y", progress, ct);
            return;
        }

        // Some emulator releases are self-extracting installers.
        if (lower.EndsWith(".exe") && OperatingSystem.IsWindows())
            throw new NotSupportedException("Installer EXE requires an emulator-specific install rule.");

        throw new NotSupportedException($"Unsupported archive type: {Path.GetExtension(archive)}");
    }

    private static string? FindSevenZip()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "7z.exe", "7zz.exe" } : new[] { "7zz", "7z" };
        foreach (var name in names)
        {
            var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(p => Path.Combine(p, name)).FirstOrDefault(File.Exists);
            if (path is not null) return path;
        }
        if (OperatingSystem.IsWindows())
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static async Task RunProcessAsync(string exe, string args, IProgress<string>? progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exe}");
        while (!p.HasExited)
        {
            progress?.Report(await p.StandardOutput.ReadLineAsync(ct) ?? "Extracting...");
            await Task.Delay(50, ct);
        }
        if (p.ExitCode != 0) throw new InvalidOperationException($"{exe} exited with code {p.ExitCode}: {await p.StandardError.ReadToEndAsync(ct)}");
    }
}
