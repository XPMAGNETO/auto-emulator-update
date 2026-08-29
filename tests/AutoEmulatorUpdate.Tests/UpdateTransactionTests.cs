using AutoEmulatorUpdate.Core.Models;
using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.Tests;

public sealed class UpdateTransactionTests
{
    [Fact]
    public async Task Candidate_PreservesProtectedPaths_AndOverlaysProgramFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "aeu-test-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "emu");
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(Path.Combine(install, "config"));
        Directory.CreateDirectory(Path.Combine(payload, "config"));
        await File.WriteAllTextAsync(Path.Combine(install, "emu.exe"), "old");
        await File.WriteAllTextAsync(Path.Combine(install, "config", "user.ini"), "user-value");
        await File.WriteAllTextAsync(Path.Combine(payload, "emu.exe"), "new");
        await File.WriteAllTextAsync(Path.Combine(payload, "config", "user.ini"), "package-default");

        try
        {
            var emulator = new InstalledEmulator
            {
                Definition = new EmulatorDefinition
                {
                    Id = "test",
                    Name = "Test Emulator",
                    ProtectedPaths = ["config"],
                    Executables = new Dictionary<string, string[]> { ["windows"] = ["emu.exe"] }
                },
                InstallPath = install,
                ExecutablePath = Path.Combine(install, "emu.exe")
            };

            var service = new UpdateTransactionService();
            var candidate = await service.PrepareCandidateAsync(emulator, payload);

            Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(candidate, "emu.exe")));
            Assert.Equal("user-value", await File.ReadAllTextAsync(Path.Combine(candidate, "config", "user.ini")));
            Assert.True(UpdateTransactionService.ValidateCandidate(emulator, candidate));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Commit_ThenRollback_RestoresExactPreviousInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), "aeu-test-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "emu");
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(payload);
        await File.WriteAllTextAsync(Path.Combine(install, "old-only.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(payload, "new-only.txt"), "new");

        try
        {
            var emulator = new InstalledEmulator
            {
                Definition = new EmulatorDefinition { Id = "test", Name = "Test Emulator" },
                InstallPath = install
            };

            var service = new UpdateTransactionService();
            var candidate = await service.PrepareCandidateAsync(emulator, payload);
            var handle = service.Commit(install, candidate);

            Assert.True(File.Exists(Path.Combine(install, "new-only.txt")));
            await service.RollbackAsync(handle);

            Assert.True(File.Exists(Path.Combine(install, "old-only.txt")));
            Assert.False(File.Exists(Path.Combine(install, "new-only.txt")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
