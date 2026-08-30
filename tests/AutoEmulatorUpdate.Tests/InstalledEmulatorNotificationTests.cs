using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Tests;

public sealed class InstalledEmulatorNotificationTests
{
    [Fact]
    public void ChangingDisplayedUpdateFieldsRaisesPropertyChanged()
    {
        var emulator = new InstalledEmulator
        {
            Definition = new EmulatorDefinition { Id = "test", Name = "Test Emulator" },
            InstallPath = "/emulators/test"
        };
        var changed = new List<string?>();
        emulator.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        emulator.CurrentVersion = "1.0";
        emulator.LatestVersion = "2.0";
        emulator.Status = "Update available";
        emulator.SourceName = "GitHub";

        Assert.Contains(nameof(InstalledEmulator.CurrentVersion), changed);
        Assert.Contains(nameof(InstalledEmulator.LatestVersion), changed);
        Assert.Contains(nameof(InstalledEmulator.Status), changed);
        Assert.Contains(nameof(InstalledEmulator.SourceName), changed);
    }
}
