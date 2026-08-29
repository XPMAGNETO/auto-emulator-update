using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.App;

public sealed class LibraryItemViewModel
{
    public required EmulatorDefinition Definition { get; init; }
    public bool Selected { get; set; }
    public string AliasesText => string.Join(", ", Definition.Aliases);
    public string PlatformSupport => string.Join(" • ", Definition.Executables.Keys.Select(k => k switch
    {
        "windows" => "Windows",
        "linux" => "Linux",
        "macos" => "macOS",
        _ => k
    }).Distinct());
}
