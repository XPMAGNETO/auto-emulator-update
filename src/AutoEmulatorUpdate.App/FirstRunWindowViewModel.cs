using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using AutoEmulatorUpdate.Core.Models;
using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.App;

public sealed class FirstRunWindowViewModel : INotifyPropertyChanged
{
    private readonly Window _window;
    private readonly AppPaths _paths;
    private readonly JsonStore _store;
    private readonly PlatformService _platform;
    private readonly CatalogService _catalog;
    private readonly DiscoveryService _discovery;
    private AppSettings _settings;
    private int _page;

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand BackCommand { get; }
    public AsyncCommand NextCommand { get; }
    public AsyncCommand ScanCommand { get; }
    public AsyncCommand SkipCommand { get; }

    public bool IsWelcomePage => _page == 0;
    public bool IsScanPage => _page == 1;
    public bool IsUpdatePage => _page == 2;
    public bool IsSchedulePage => _page == 3;
    public bool IsFinishPage => _page == 4;
    public bool CanGoBack => _page > 0;
    public string NextText => _page == 4 ? "Finish" : "Next";

    private double _scanProgress;
    public double ScanProgress { get => _scanProgress; set => Set(ref _scanProgress, value); }

    private string _scanStatus = "Ready to scan.";
    public string ScanStatus { get => _scanStatus; set => Set(ref _scanStatus, value); }

    public bool RecommendedMode
    {
        get => _settings.MaintenanceMode == MaintenanceMode.BackupUpdateVerify;
        set { if (value) { _settings.MaintenanceMode = MaintenanceMode.BackupUpdateVerify; RaiseModes(); } }
    }

    public bool AskMode
    {
        get => _settings.MaintenanceMode == MaintenanceMode.AskBeforeInstall;
        set { if (value) { _settings.MaintenanceMode = MaintenanceMode.AskBeforeInstall; RaiseModes(); } }
    }

    public bool CheckOnlyMode
    {
        get => _settings.MaintenanceMode == MaintenanceMode.CheckOnly;
        set { if (value) { _settings.MaintenanceMode = MaintenanceMode.CheckOnly; RaiseModes(); } }
    }

    public bool AutoAppUpdates { get => _settings.AutoAppUpdates; set { _settings.AutoAppUpdates = value; Raise(); } }
    public bool ScheduleEnabled { get => _settings.ScheduleEnabled; set { _settings.ScheduleEnabled = value; Raise(); } }
    public string ScheduleTime { get => _settings.ScheduleTime; set { _settings.ScheduleTime = value; Raise(); } }
    public bool BackgroundMode { get => _settings.BackgroundMode; set { _settings.BackgroundMode = value; Raise(); } }

    public bool CheckOnLaunch
    {
        get => _settings.StartupBehavior == StartupBehavior.CheckOnLaunch;
        set { if (value) { _settings.StartupBehavior = StartupBehavior.CheckOnLaunch; Raise(nameof(CheckOnLaunch)); Raise(nameof(ManualOnly)); } }
    }

    public bool ManualOnly
    {
        get => _settings.StartupBehavior == StartupBehavior.Manual;
        set { if (value) { _settings.StartupBehavior = StartupBehavior.Manual; Raise(nameof(CheckOnLaunch)); Raise(nameof(ManualOnly)); } }
    }

    public string SummaryText =>
        $"Update mode: {_settings.MaintenanceMode}\n" +
        $"Startup: {_settings.StartupBehavior}\n" +
        $"Scheduled maintenance: {(_settings.ScheduleEnabled ? "Enabled at " + _settings.ScheduleTime : "Off")}\n" +
        $"Background mode: {(_settings.BackgroundMode ? "On" : "Off")}";

    private FirstRunWindowViewModel(
        Window window,
        AppPaths paths,
        JsonStore store,
        PlatformService platform,
        CatalogService catalog,
        DiscoveryService discovery,
        AppSettings settings)
    {
        _window = window; _paths = paths; _store = store; _platform = platform; _catalog = catalog; _discovery = discovery; _settings = settings;
        BackCommand = new RelayCommand(() => { if (_page > 0) { _page--; RaisePage(); } });
        NextCommand = new AsyncCommand(NextAsync);
        ScanCommand = new AsyncCommand(ScanAsync);
        SkipCommand = new AsyncCommand(async () => { _settings.FirstRunCompleted = true; await _store.SaveAsync(_paths.SettingsFile, _settings); _window.Close(true); });
    }

    public static async Task<FirstRunWindowViewModel> CreateAsync(Window window)
    {
        var paths = new AppPaths();
        var store = new JsonStore();
        var platform = new PlatformService();
        var version = new VersionService();
        var catalog = new CatalogService(paths);
        var discovery = new DiscoveryService(platform, version);
        var settings = await store.LoadAsync(paths.SettingsFile, new AppSettings());
        settings.ExperienceMode = UserExperienceMode.Simple;
        return new FirstRunWindowViewModel(window, paths, store, platform, catalog, discovery, settings);
    }

    private async Task NextAsync()
    {
        if (_page < 4) { _page++; RaisePage(); return; }
        _settings.FirstRunCompleted = true;
        await _store.SaveAsync(_paths.SettingsFile, _settings);
        _window.Close(true);
    }

    private async Task ScanAsync()
    {
        ScanStatus = "Loading emulator definitions...";
        var bundled = Path.Combine(AppContext.BaseDirectory, "manifests", "emulators");
        var defs = await _catalog.LoadAsync(bundled);
        var found = await _discovery.ScanAsync(defs, _platform.DefaultSearchRoots(),
            new Progress<(string message, double? percent)>(x =>
            {
                ScanStatus = x.message;
                if (x.percent is not null) ScanProgress = x.percent.Value;
            }));
        ScanProgress = 100;
        ScanStatus = $"Found {found.Count} emulator installation profile(s). They'll appear automatically on the main dashboard.";
        Raise(nameof(SummaryText));
    }

    private void RaiseModes() { Raise(nameof(RecommendedMode)); Raise(nameof(AskMode)); Raise(nameof(CheckOnlyMode)); Raise(nameof(SummaryText)); }
    private void RaisePage()
    {
        Raise(nameof(IsWelcomePage)); Raise(nameof(IsScanPage)); Raise(nameof(IsUpdatePage)); Raise(nameof(IsSchedulePage)); Raise(nameof(IsFinishPage));
        Raise(nameof(CanGoBack)); Raise(nameof(NextText)); Raise(nameof(SummaryText));
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; Raise(name);
    }
}
