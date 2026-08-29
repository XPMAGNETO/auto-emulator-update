using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using AutoEmulatorUpdate.Core.Models;
using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly Window? _owner;
    private readonly AppPaths _paths = new();
    private readonly PlatformService _platform = new();
    private readonly VersionService _versions = new();
    private readonly JsonStore _store = new();
    private readonly HttpClient _http;
    private readonly CatalogService _catalog;
    private readonly DiscoveryService _discovery;
    private readonly FrontendImportService _frontends;
    private readonly ReleaseResolverService _resolver;
    private readonly DownloadService _downloads;
    private readonly ArchiveService _archives = new();
    private readonly BackupService _backups;
    private readonly UpdateCoordinator _updates;
    private readonly InstallService _installer;
    private readonly DiagnosticService _diagnostics;
    private readonly FriendlyErrorService _friendly = new();
    private readonly AppLogService _log;
    private readonly SelfUpdateService _selfUpdate;
    private readonly ISchedulerService _scheduler = new CrossPlatformSchedulerService();
    private readonly NotificationService _notifications = new();
    private AppSettings _settings = new();
    private IReadOnlyList<EmulatorDefinition> _definitions = [];
    private CancellationTokenSource _cts = new();
    private bool _queuePaused;

    public ObservableCollection<InstalledEmulator> Installed { get; } = [];
    public ObservableCollection<LibraryItemViewModel> NotInstalled { get; } = [];
    public ObservableCollection<LibraryItemViewModel> FilteredNotInstalled { get; } = [];
    public ObservableCollection<UpdateQueueItem> Queue { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public ObservableCollection<HistoryEntry> RecentHistory { get; } = [];
    public ObservableCollection<FailureEntry> Failures { get; } = [];
    public ObservableCollection<BackupRecord> Backups { get; } = [];

    public AsyncCommand ScanCommand { get; }
    public AsyncCommand ImportFrontendsCommand { get; }
    public AsyncCommand CheckCommand { get; }
    public AsyncCommand CheckSelectedCommand { get; }
    public RelayCommand UpdateSelectedCommand { get; }
    public RelayCommand UpdateAllCommand { get; }
    public AsyncCommand InstallSelectedCommand { get; }
    public AsyncCommand RunQueueCommand { get; }
    public RelayCommand PauseQueueCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearCompletedCommand { get; }
    public RelayCommand RefreshBackupsCommand { get; }
    public AsyncCommand RestoreBackupCommand { get; }
    public RelayCommand CleanupBackupsCommand { get; }
    public AsyncCommand DiagnosticCommand { get; }
    public AsyncCommand CheckAppUpdateCommand { get; }
    public AsyncCommand ApplyScheduleCommand { get; }
    public RelayCommand AdvancedCommand { get; }

    public string PlatformText => $"v{AutoEmulatorUpdate.Core.BuildInfo.Version} • {_platform.RuntimeId}";
    public string InstalledHeader => $"Installed ({Installed.Count})";
    public string NotInstalledHeader => $"Available to install ({FilteredNotInstalled.Count})";
    public int InstalledCount => Installed.Count;
    public int UpdatesAvailableCount => Installed.Count(x => x.Status == "Update available");
    public string LastCheckText { get; private set; } = "Not checked yet";
    public string HomeSummary => UpdatesAvailableCount switch
    {
        0 when Installed.Count == 0 => "No emulators have been detected yet. Scan your system or import your frontend configuration.",
        0 => "Everything we've checked is up to date.",
        1 => "1 emulator update is ready. Auto Emulator Update will back up the current version before installing it.",
        _ => $"{UpdatesAvailableCount} emulator updates are ready. Backups and post-update validation are enabled."
    };

    public Array MaintenanceModes => Enum.GetValues<MaintenanceMode>();
    public Array StartupBehaviors => Enum.GetValues<StartupBehavior>();

    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    private string _statusText = "Starting...";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private string _logText = "";
    public string LogText { get => _logText; set => Set(ref _logText, value); }

    private string _librarySearch = "";
    public string LibrarySearch
    {
        get => _librarySearch;
        set { if (Set(ref _librarySearch, value)) RefilterLibrary(); }
    }

    private int _selectedMainTab;
    public int SelectedMainTab { get => _selectedMainTab; set => Set(ref _selectedMainTab, value); }

    private BackupRecord? _selectedBackup;
    public BackupRecord? SelectedBackup { get => _selectedBackup; set => Set(ref _selectedBackup, value); }

    private string _settingsStatus = "";
    public string SettingsStatus { get => _settingsStatus; set => Set(ref _settingsStatus, value); }

    public MaintenanceMode MaintenanceMode
    {
        get => _settings.MaintenanceMode;
        set { _settings.MaintenanceMode = value; _ = SaveSettingsAsync(); Raise(); Raise(nameof(HomeSummary)); }
    }

    public StartupBehavior StartupBehavior
    {
        get => _settings.StartupBehavior;
        set { _settings.StartupBehavior = value; _ = SaveSettingsAsync(); Raise(); }
    }

    public bool AutoAppUpdates
    {
        get => _settings.AutoAppUpdates;
        set { _settings.AutoAppUpdates = value; _ = SaveSettingsAsync(); Raise(); }
    }

    public bool BackgroundMode
    {
        get => _settings.BackgroundMode;
        set { _settings.BackgroundMode = value; _ = SaveSettingsAsync(); Raise(); }
    }

    public bool ScheduleEnabled
    {
        get => _settings.ScheduleEnabled;
        set { _settings.ScheduleEnabled = value; _ = SaveSettingsAsync(); Raise(); }
    }

    public string ScheduleTime
    {
        get => _settings.ScheduleTime;
        set { _settings.ScheduleTime = value; _ = SaveSettingsAsync(); Raise(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel(Window? owner = null)
    {
        _owner = owner;
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultNetworkCredentials
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _catalog = new CatalogService(_paths);
        _discovery = new DiscoveryService(_platform, _versions);
        _frontends = new FrontendImportService(_platform);
        _resolver = new ReleaseResolverService(_http, _platform);
        _downloads = new DownloadService(_http, _paths);
        _backups = new BackupService(_paths);
        _updates = new UpdateCoordinator(_resolver, _downloads, _archives, _backups, _store, _paths, _versions);
        _installer = new InstallService(_resolver, _downloads, _archives, _platform);
        _diagnostics = new DiagnosticService(_paths);
        _log = new AppLogService(_paths);
        _selfUpdate = new SelfUpdateService(_http);

        ScanCommand = new AsyncCommand(ScanAsync);
        ImportFrontendsCommand = new AsyncCommand(ImportFrontendsAsync);
        CheckCommand = new AsyncCommand(CheckAllAsync);
        CheckSelectedCommand = new AsyncCommand(CheckSelectedAsync);
        UpdateSelectedCommand = new RelayCommand(() => QueueUpdates(Installed.Where(x => x.Selected)));
        UpdateAllCommand = new RelayCommand(() => { QueueUpdates(Installed.Where(x => x.Status == "Update available")); SelectedMainTab = 2; });
        InstallSelectedCommand = new AsyncCommand(InstallSelectedAsync);
        RunQueueCommand = new AsyncCommand(RunQueueAsync);
        PauseQueueCommand = new RelayCommand(() => { _queuePaused = !_queuePaused; StatusText = _queuePaused ? "Update queue paused." : "Update queue resumed."; });
        CancelCommand = new RelayCommand(() => { _cts.Cancel(); Append("Cancellation requested."); });
        ClearCompletedCommand = new RelayCommand(() =>
        {
            foreach (var q in Queue.Where(q => q.State is QueueState.Complete or QueueState.Failed or QueueState.Cancelled).ToArray())
                Queue.Remove(q);
        });
        RefreshBackupsCommand = new RelayCommand(RefreshBackups);
        RestoreBackupCommand = new AsyncCommand(RestoreSelectedBackupAsync, () => SelectedBackup is not null);
        CleanupBackupsCommand = new RelayCommand(() => { _backups.Cleanup(_settings.BackupRetentionDays, _settings.BackupMaxGb); RefreshBackups(); Append("Old backups cleaned."); });
        DiagnosticCommand = new AsyncCommand(CreateDiagnosticAsync);
        CheckAppUpdateCommand = new AsyncCommand(() => CheckAppUpdateAsync(true));
        ApplyScheduleCommand = new AsyncCommand(ApplyScheduleAsync);
        AdvancedCommand = new RelayCommand(OpenAdvanced);
    }

    public async Task InitializeAsync()
    {
        _settings = await _store.LoadAsync(_paths.SettingsFile, new AppSettings());
        var bundled = Path.Combine(AppContext.BaseDirectory, "manifests", "emulators");
        _definitions = await _catalog.LoadAsync(bundled);
        ReloadHistoryFailures();
        RebuildNotInstalled();
        RefreshBackups();
        StatusText = $"Ready. {_definitions.Count} emulator definitions loaded.";
        Append(StatusText);

        if (_settings.AutoAppUpdates)
            _ = CheckAppUpdateAsync(false);

        if (_settings.StartupBehavior != StartupBehavior.Manual)
        {
            await ScanAsync();
            await ImportFrontendsAsync();
            await CheckAllAsync();

            if (_settings.StartupBehavior == StartupBehavior.UpdateOnLaunch &&
                _settings.MaintenanceMode == MaintenanceMode.BackupUpdateVerify)
            {
                QueueUpdates(Installed.Where(x => x.Status == "Update available"));
                await RunQueueAsync();
            }
        }
    }

    private async Task ScanAsync()
    {
        ResetCancellation();
        StatusText = "Finding installed emulators...";
        var found = await _discovery.ScanAsync(_definitions, _platform.DefaultSearchRoots(),
            new Progress<(string message, double? percent)>(x => { StatusText = x.message; if (x.percent is not null) Progress = x.percent.Value; }), _cts.Token);
        MergeFound(found, "System scan");
        Progress = 0;
        StatusText = $"Found {Installed.Count} installed emulator profile(s).";
        Append(StatusText);
    }

    private async Task ImportFrontendsAsync()
    {
        ResetCancellation();
        var roots = _frontends.DetectRoots().ToArray();
        if (roots.Length == 0) { Append("No supported frontend folders were detected."); return; }

        StatusText = $"Importing {roots.Length} frontend configuration(s)...";
        var paths = await _frontends.ImportExecutablePathsAsync(roots, _definitions, _cts.Token);
        var extras = new List<InstalledEmulator>();

        foreach (var pair in paths)
        {
            var def = _definitions.FirstOrDefault(d => d.Id.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            if (def is null) continue;
            foreach (var exe in pair.Value)
            {
                extras.Add(new InstalledEmulator
                {
                    Definition = def,
                    InstallPath = Path.GetDirectoryName(exe)!,
                    ExecutablePath = exe,
                    FrontendOwner = "Imported frontend",
                    Status = "Detected"
                });
            }
        }

        MergeFound(extras, "Frontend import");
        StatusText = $"Frontend import complete. {extras.Count} reference(s) found.";
        Append(StatusText);
    }

    private async Task CheckAllAsync() => await CheckEmulatorsAsync(Installed);

    private async Task CheckSelectedAsync() =>
        await CheckEmulatorsAsync(Installed.Where(x => x.Selected).ToArray());

    private async Task CheckEmulatorsAsync(IEnumerable<InstalledEmulator> emulators)
    {
        ResetCancellation();
        var list = emulators.ToArray();
        if (list.Length == 0) { StatusText = "Nothing selected."; return; }

        var gate = new SemaphoreSlim(Math.Clamp(_settings.ParallelChecks, 1, 8));
        var tasks = list.Select(async e =>
        {
            await gate.WaitAsync(_cts.Token);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Checking {e.Definition.Name}...");
                var r = await _updates.CheckAsync(e, _cts.Token);
                Append($"{e.Definition.Name}: {e.CurrentVersion} → {r.Version} ({e.Status})");
            }
            catch (Exception ex)
            {
                e.SourceHealth = SourceHealthState.Failed;
                e.Status = "Needs attention";
                var friendly = _friendly.Present(ex, e.Definition.Name);
                Append($"{e.Definition.Name}: {friendly.Title} — {ex.Message}");
            }
            finally { gate.Release(); }
        }).ToArray();

        await Task.WhenAll(tasks);
        LastCheckText = DateTime.Now.ToString("g");
        StatusText = UpdatesAvailableCount == 0 ? "Everything checked is current." : $"{UpdatesAvailableCount} update(s) available.";
        Raise(nameof(LastCheckText)); RaiseHome();
        await _notifications.ShowAsync("Auto Emulator Update", StatusText);
    }

    private void QueueUpdates(IEnumerable<InstalledEmulator> emulators)
    {
        foreach (var e in emulators.Where(x => x.Status == "Update available"))
        {
            if (Queue.Any(q => ReferenceEquals(q.Emulator, e) && q.State == QueueState.Queued)) continue;
            Queue.Add(new UpdateQueueItem { Emulator = e, Action = QueueAction.Update });
        }
        Append($"{Queue.Count(q => q.State == QueueState.Queued)} update(s) queued.");
    }

    private async Task InstallSelectedAsync()
    {
        var selected = FilteredNotInstalled.Where(x => x.Selected).ToArray();
        if (selected.Length == 0) { StatusText = "Select one or more emulators to install."; return; }

        ResetCancellation();
        foreach (var item in selected)
        {
            try
            {
                StatusText = $"Installing {item.Definition.Name}...";
                var installed = await _installer.InstallAsync(item.Definition, _settings,
                    new Progress<(double percent, string message)>(x => { Progress = x.percent; StatusText = $"{item.Definition.Name}: {x.message}"; }),
                    _cts.Token);
                MergeFound([installed], "Auto Emulator Update");
                Append($"{item.Definition.Name} installed successfully.");
            }
            catch (Exception ex)
            {
                var friendly = _friendly.Present(ex, item.Definition.Name);
                StatusText = friendly.Message;
                Append($"{item.Definition.Name}: {friendly.Title} — {ex.Message}");
            }
        }
        Progress = 0;
    }

    private async Task RunQueueAsync()
    {
        ResetCancellation();
        foreach (var item in Queue.Where(q => q.State == QueueState.Queued).ToArray())
        {
            while (_queuePaused && !_cts.IsCancellationRequested)
                await Task.Delay(150, _cts.Token);

            try
            {
                await _updates.ProcessAsync(item, _settings,
                    new Progress<(double percent, string message)>(x =>
                    {
                        Progress = x.percent;
                        StatusText = $"{item.Emulator.Definition.Name}: {x.message}";
                        item.Progress = x.percent; item.Message = x.message;
                    }), _cts.Token);
                Append($"{item.Emulator.Definition.Name}: update completed and validated.");
            }
            catch (OperationCanceledException)
            {
                Append($"{item.Emulator.Definition.Name}: cancelled.");
                break;
            }
            catch (Exception ex)
            {
                var friendly = _friendly.Present(ex, item.Emulator.Definition.Name);
                StatusText = friendly.Message;
                Append($"{item.Emulator.Definition.Name}: {friendly.Title} — {ex.Message}");
            }
        }

        Progress = 0;
        StatusText = "Update queue finished.";
        ReloadHistoryFailures();
        RefreshBackups();
        RaiseHome();
        await _notifications.ShowAsync("Auto Emulator Update", StatusText);
        if (_settings.AutoCloseAfterRun && Queue.All(q => q.State != QueueState.Running))
            _owner?.Close();
    }

    private async Task RestoreSelectedBackupAsync()
    {
        if (SelectedBackup is null) return;
        var emu = Installed.FirstOrDefault(x => x.Definition.Name == SelectedBackup.Emulator);
        if (emu is null) { StatusText = "The matching emulator is not currently detected."; return; }

        try
        {
            StatusText = $"Restoring {emu.Definition.Name}...";
            await _backups.RollbackAsync(emu, SelectedBackup,
                new Progress<string>(m => StatusText = $"Restoring {m}"), _cts.Token);
            StatusText = $"{emu.Definition.Name} was restored.";
            Append(StatusText);
        }
        catch (Exception ex)
        {
            var friendly = _friendly.Present(ex, emu.Definition.Name);
            StatusText = friendly.Message;
            Append(ex.ToString());
        }
    }

    private async Task CreateDiagnosticAsync()
    {
        var path = await _diagnostics.CreateBundleAsync(_settings, Installed, StatusText);
        SettingsStatus = $"Diagnostic ZIP created: {path}";
        Append(SettingsStatus);
    }

    private async Task CheckAppUpdateAsync(bool interactive)
    {
        try
        {
            var update = await _selfUpdate.CheckAsync();
            if (update is null)
            {
                if (interactive)
                    SettingsStatus = AutoEmulatorUpdate.Core.BuildInfo.HasConfiguredRepository
                        ? "Auto Emulator Update is current."
                        : "App update feed will activate after the GitHub repository owner is configured.";
                return;
            }

            SettingsStatus = $"Auto Emulator Update {update.Version} is available.";
            if (interactive && _owner is not null)
            {
                var box = new Window
                {
                    Title = "Application update available",
                    Width = 520,
                    Height = 220,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new StackPanel
                    {
                        Margin = new Avalonia.Thickness(20),
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock { Text = $"Auto Emulator Update {update.Version} is available.", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                            new TextBlock { Text = "Open the release page to download the correct installer for this computer.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                            new Button { Content = "OPEN UPDATE PAGE", Command = new RelayCommand(() => Process.Start(new ProcessStartInfo(update.ReleaseUrl) { UseShellExecute = true })) }
                        }
                    }
                };
                await box.ShowDialog(_owner);
            }
        }
        catch (Exception ex)
        {
            if (interactive) SettingsStatus = _friendly.Present(ex).Message;
            Append($"App update check: {ex.Message}");
        }
    }

    private async Task ApplyScheduleAsync()
    {
        if (!TimeOnly.TryParse(_settings.ScheduleTime, out var time))
        {
            SettingsStatus = "Enter the schedule time as HH:mm, for example 09:00.";
            return;
        }

        try
        {
            if (!_settings.ScheduleEnabled)
            {
                await _scheduler.DisableAsync();
                SettingsStatus = "Scheduled maintenance disabled.";
                return;
            }

            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Application executable path is unavailable.");
            await _scheduler.ApplyAsync(new ScheduleDefinition(true, time, _settings.ScheduleDays, exe));
            SettingsStatus = $"Scheduled maintenance configured for {_settings.ScheduleTime}.";
        }
        catch (Exception ex)
        {
            SettingsStatus = _friendly.Present(ex).Message;
            Append($"Schedule error: {ex}");
        }
    }

    private void OpenAdvanced()
    {
        var window = new AdvancedWindow { DataContext = this };
        if (_owner is not null) window.Show(_owner); else window.Show();
    }

    private void MergeFound(IEnumerable<InstalledEmulator> found, string owner)
    {
        foreach (var e in found)
        {
            e.FrontendOwner = e.FrontendOwner == "Manual / Scan" ? owner : e.FrontendOwner;
            if (Installed.Any(x => x.Definition.Id.Equals(e.Definition.Id, StringComparison.OrdinalIgnoreCase) &&
                                   x.InstallPath.Equals(e.InstallPath, StringComparison.OrdinalIgnoreCase))) continue;
            e.ProfileName = Installed.Any(x => x.Definition.Id.Equals(e.Definition.Id, StringComparison.OrdinalIgnoreCase))
                ? $"Profile {Installed.Count(x => x.Definition.Id.Equals(e.Definition.Id, StringComparison.OrdinalIgnoreCase)) + 1}"
                : "Primary";
            Installed.Add(e);
        }
        RebuildNotInstalled();
        RaiseHome();
    }

    private void RebuildNotInstalled()
    {
        NotInstalled.Clear();
        var ids = Installed.Select(x => x.Definition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in _definitions.Where(x => !ids.Contains(x.Id)))
            NotInstalled.Add(new LibraryItemViewModel { Definition = d });
        RefilterLibrary();
        Raise(nameof(InstalledHeader)); Raise(nameof(NotInstalledHeader));
    }

    private void RefilterLibrary()
    {
        FilteredNotInstalled.Clear();
        var q = LibrarySearch.Trim();
        foreach (var item in NotInstalled.Where(x =>
                     string.IsNullOrWhiteSpace(q) ||
                     x.Definition.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                     x.Definition.Aliases.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase))))
            FilteredNotInstalled.Add(item);
        Raise(nameof(NotInstalledHeader));
    }

    private void ReloadHistoryFailures()
    {
        History.Clear();
        foreach (var h in _store.ReadJsonLines<HistoryEntry>(_paths.HistoryFile).OrderByDescending(x => x.Timestamp).Take(500))
            History.Add(h);

        RecentHistory.Clear();
        foreach (var h in History.Take(10)) RecentHistory.Add(h);

        Failures.Clear();
        foreach (var f in _store.ReadJsonLines<FailureEntry>(_paths.FailureFile).OrderByDescending(x => x.Timestamp).Take(500))
            Failures.Add(f);
    }

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (var e in Installed)
            foreach (var b in _backups.List(e.Definition.Id, e.Definition.Name).Take(20))
                Backups.Add(b);
    }

    private async Task SaveSettingsAsync()
    {
        try { await _store.SaveAsync(_paths.SettingsFile, _settings); }
        catch (Exception ex) { Append($"Settings save error: {ex.Message}"); }
    }

    private void ResetCancellation()
    {
        if (!_cts.IsCancellationRequested) return;
        _cts.Dispose();
        _cts = new CancellationTokenSource();
    }

    private void Append(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogText = string.IsNullOrEmpty(LogText) ? line : LogText + Environment.NewLine + line;
        _ = _log.WriteAsync(message);
    }

    private void RaiseHome()
    {
        Raise(nameof(InstalledCount)); Raise(nameof(UpdatesAvailableCount)); Raise(nameof(HomeSummary)); Raise(nameof(InstalledHeader));
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
}
