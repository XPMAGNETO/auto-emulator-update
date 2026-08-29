using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.App;

public partial class AdvancedWindow : Window
{
    private string _discoveryReport = "Run discovery diagnostics to generate a sanitized report.";

    public AdvancedWindow()
    {
        InitializeComponent();
        DiscoveryText.Text = _discoveryReport;
    }

    private async void RunDiscoveryDiagnostics_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            DiscoveryStatus.Text = "Scanning...";
            var paths = new AppPaths();
            var platform = new PlatformService();
            var catalog = new CatalogService(paths);
            var discovery = new DiscoveryService(platform, new VersionService());
            var frontends = new FrontendImportService(platform);
            var bundled = Path.Combine(AppContext.BaseDirectory, "manifests", "emulators");
            var definitions = await catalog.LoadAsync(bundled);
            var found = await discovery.ScanAsync(definitions, platform.DefaultSearchRoots(),
                new Progress<(string message, double? percent)>(x => DiscoveryStatus.Text = x.message));
            var frontendRoots = frontends.DetectRoots().Select(x => (x.Name, x.Path)).ToArray();

            _discoveryReport = DiscoveryDiagnosticsFormatter.Build(
                discovery.LastDiagnostics,
                definitions,
                frontendRoots,
                found);
            DiscoveryText.Text = _discoveryReport;
            DiscoveryStatus.Text = $"Done: {found.Count} emulator profile(s), {frontendRoots.Length} frontend root(s).";
        }
        catch (Exception ex)
        {
            DiscoveryStatus.Text = $"Diagnostics failed: {ex.Message}";
        }
    }

    private async void CopyDiscoveryDiagnostics_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            DiscoveryStatus.Text = "Clipboard is unavailable on this platform/session.";
            return;
        }
        await clipboard.SetTextAsync(_discoveryReport);
        DiscoveryStatus.Text = "Sanitized discovery report copied.";
    }

    private async void ExportDiscoveryDiagnostics_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export sanitized discovery diagnostics",
            SuggestedFileName = $"auto-emulator-update-discovery-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            FileTypeChoices =
            [
                new FilePickerFileType("Text report") { Patterns = ["*.txt"] }
            ]
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(_discoveryReport);
        DiscoveryStatus.Text = "Sanitized discovery report exported.";
    }
}
