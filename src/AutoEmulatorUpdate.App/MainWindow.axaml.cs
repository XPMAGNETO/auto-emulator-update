using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using AutoEmulatorUpdate.Core.Models;
using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.App;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _boundInstalledViewModel;

    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var iconStream = AssetLoader.Open(new Uri("avares://AutoEmulatorUpdate.App/Assets/app-icon.png"));
                Icon = new WindowIcon(iconStream);
            }
            catch
            {
                // The executable and installed shortcuts still carry the Windows icon.
            }
        }

        DataContextChanged += (_, _) => BindInstalledGrid();
    }

    private void BindInstalledGrid()
    {
        var grid = this.FindControl<DataGrid>("InstalledGrid");
        if (grid is null || DataContext is not MainWindowViewModel vm) return;
        if (ReferenceEquals(_boundInstalledViewModel, vm)) return;

        _boundInstalledViewModel = vm;
        grid.ItemsSource = vm.Installed;
        vm.Installed.CollectionChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Force a safe UI-thread refresh. This also covers discovery/import work
                // that completes on a worker continuation on some desktop runtimes.
                var selected = grid.SelectedItem;
                grid.ItemsSource = null;
                grid.ItemsSource = vm.Installed;
                grid.SelectedItem = selected;
                grid.InvalidateMeasure();
                grid.InvalidateVisual();
            });
        };
    }

    private async void ReportProblem_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var paths = new AppPaths();
            var store = new JsonStore();
            var settings = await store.LoadAsync(paths.SettingsFile, new AppSettings());
            var installed = DataContext is MainWindowViewModel vm
                ? vm.Installed.AsEnumerable()
                : Enumerable.Empty<InstalledEmulator>();

            var zip = await new DiagnosticService(paths).CreateBundleAsync(
                settings,
                installed,
                "Diagnostic bundle created from the Report Problem button.");

            if (DataContext is MainWindowViewModel viewModel)
                viewModel.SettingsStatus = $"Diagnostic ZIP created: {zip}";

            RevealFile(zip);

            var title = Uri.EscapeDataString($"Bug report - Auto Emulator Updater v{AutoEmulatorUpdate.Core.BuildInfo.Version}");
            var body = Uri.EscapeDataString(
                $"Auto Emulator Updater version: {AutoEmulatorUpdate.Core.BuildInfo.Version}\n" +
                $"Platform: {Environment.OSVersion}\n\n" +
                "What happened?\nPlease describe what you were doing and what you expected to happen.\n\n" +
                "Steps to reproduce:\n1. \n2. \n3. \n\n" +
                "A sanitized diagnostic ZIP has been created on this computer. " +
                "Please drag and drop that ZIP into this issue before submitting it.\n\n" +
                "Privacy note: emulator install paths are omitted from the public diagnostic summary.");

            var url = $"https://github.com/XPMAGNETO/auto-emulator-update/issues/new?title={title}&body={body}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SettingsStatus = $"Could not prepare the bug report: {ex.Message}";
        }
    }

    private static void RevealFile(string file)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open", $"-R \"{file}\"") { UseShellExecute = false });
            }
            else
            {
                var directory = Path.GetDirectoryName(file) ?? Environment.CurrentDirectory;
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{directory}\"") { UseShellExecute = false });
            }
        }
        catch
        {
            // The report page still opens even if the platform file manager cannot reveal the ZIP.
        }
    }
}
