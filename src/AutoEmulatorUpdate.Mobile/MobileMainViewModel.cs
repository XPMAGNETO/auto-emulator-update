using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AutoEmulatorUpdate.Mobile;

public sealed class MobileMainViewModel : INotifyPropertyChanged
{
    private readonly CompanionClient _client;
    private CompanionSnapshot _snapshot = CompanionSnapshot.Empty;
    private string _desktopAddress = string.Empty;
    private string _pairingCode = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isPaired;
    private bool _isBusy;
    private int _selectedTab;

    public MobileMainViewModel(CompanionClient client)
    {
        _client = client;
        PairCommand = new AsyncCommand(PairAsync, () => !IsBusy);
        CheckAllCommand = new AsyncCommand(() => RunCommandAsync("check-all"), () => IsPaired && !IsBusy);
        UpdateAllCommand = new AsyncCommand(() => RunCommandAsync("update-all"), () => CanInstallUpdates && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand PairCommand { get; }
    public ICommand CheckAllCommand { get; }
    public ICommand UpdateAllCommand { get; }
    public CompanionSnapshot Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }
    public string DesktopAddress { get => _desktopAddress; set => Set(ref _desktopAddress, value); }
    public string PairingCode { get => _pairingCode; set => Set(ref _pairingCode, value); }
    public string ErrorMessage { get => _errorMessage; private set { Set(ref _errorMessage, value); OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsPaired { get => _isPaired; private set { Set(ref _isPaired, value); OnPropertyChanged(nameof(IsPairingRequired)); OnPropertyChanged(nameof(ConnectionLabel)); } }
    public bool IsPairingRequired => !IsPaired;
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); RefreshCommands(); } }
    public bool CanInstallUpdates => IsPaired && Snapshot.UpdateCount > 0;
    public string ConnectionLabel => IsPaired ? "Connected" : "Not paired";
    public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }

    private async Task PairAsync()
    {
        await ExecuteAsync(async () =>
        {
            Snapshot = await _client.PairAsync(DesktopAddress, PairingCode);
            IsPaired = true;
            PairingCode = string.Empty;
        });
    }

    private Task RunCommandAsync(string command) => ExecuteAsync(async () =>
    {
        Snapshot = await _client.RunCommandAsync(command);
        OnPropertyChanged(nameof(CanInstallUpdates));
    });

    private async Task ExecuteAsync(Func<Task> operation)
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try { await operation(); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private void RefreshCommands()
    {
        (PairCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (CheckAllCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (UpdateAllCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) => await execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
