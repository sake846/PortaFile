using System.IO.Ports;
using System.Windows.Threading;
using PortaFile.Models;
using PortaFile.Services;
using PortaFile.Transfer;

namespace PortaFile.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly SerialTransport _transport = new();
    private readonly TransferEngine _engine;
    private readonly IUserDialogService _dialogs;
    private readonly ILastStateService _lastStateService;
    private readonly AppLocalization _localization;
    private readonly DispatcherTimer _speedTimer = new();
    private string? _selectedPortName;
    private int _selectedBaudRate = 115200;
    private TransferReliabilityMode _selectedReliabilityMode = TransferReliabilityMode.Arq;
    private DuplexMode _selectedDuplexMode = DuplexMode.HalfDuplex;
    private HalfDuplexControl _selectedHalfDuplexControl = HalfDuplexControl.DriverManaged;
    private string? _sendFileDirectory;
    private bool _isConnected;
    private bool _isDisposed;

    public MainWindowViewModel(IUserDialogService dialogs, ILastStateService lastStateService, AppLocalization localization, Action<Action> ui)
    {
        _dialogs = dialogs;
        _lastStateService = lastStateService;
        _localization = localization;

        ReliabilityModes = [
            new(localization.Option_ReliabilityArq, TransferReliabilityMode.Arq),
            new(localization.Option_ReliabilityOneWay, TransferReliabilityMode.OneWay)
        ];
        DuplexModes = [
            new(localization.Option_DuplexFull, DuplexMode.FullDuplex),
            new(localization.Option_DuplexHalf, DuplexMode.HalfDuplex)
        ];
        HalfDuplexControls = [
            new(localization.Option_HalfDuplexDriver, HalfDuplexControl.DriverManaged),
            new(localization.Option_HalfDuplexRts, HalfDuplexControl.Rts)
        ];

        ApplyLastState(_lastStateService.Load());
        _engine = new TransferEngine(
            _transport,
            GetSerialSettings,
            Progress,
            message => _dialogs.ConfirmWarningAsync(message, _localization.Title_ConfirmRetry),
            ui);

        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        ConnectCommand = new RelayCommand(Connect, () => !IsConnected);
        DisconnectCommand = new RelayCommand(Disconnect, () => IsConnected);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => IsConnected);
        SelectFileCommand = new AsyncRelayCommand(SelectFilesAsync, () => IsConnected);
        OpenDownloadsCommand = new RelayCommand(OpenDownloads);
        DragOverCommand = new RelayCommand(OnDragOver);
        DropCommand = new RelayCommand(OnDrop);

        RefreshPorts();

        _speedTimer.Interval = TimeSpan.FromSeconds(1);
        _speedTimer.Tick += (_, _) => Progress.RefreshTransientText();
        _speedTimer.Start();
    }

    public AppLocalization Localization => _localization;

    public TransferProgress Progress { get; } = new();

    public ObservableCollection<string> PortNames { get; } = [];

    public IReadOnlyList<int> BaudRates { get; } =
    [
        115200,
        230400,
        460800,
        921600,
        1000000,
        2000000,
        3000000
    ];

    public IReadOnlyList<OptionItem<TransferReliabilityMode>> ReliabilityModes { get; }

    public IReadOnlyList<OptionItem<DuplexMode>> DuplexModes { get; }

    public IReadOnlyList<OptionItem<HalfDuplexControl>> HalfDuplexControls { get; }

    public string? SelectedPortName
    {
        get => _selectedPortName;
        set => SetField(ref _selectedPortName, value);
    }

    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set => SetField(ref _selectedBaudRate, value);
    }

    public TransferReliabilityMode SelectedReliabilityMode
    {
        get => _selectedReliabilityMode;
        set => SetField(ref _selectedReliabilityMode, value);
    }

    public DuplexMode SelectedDuplexMode
    {
        get => _selectedDuplexMode;
        set => SetField(ref _selectedDuplexMode, value);
    }

    public HalfDuplexControl SelectedHalfDuplexControl
    {
        get => _selectedHalfDuplexControl;
        set => SetField(ref _selectedHalfDuplexControl, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (!SetField(ref _isConnected, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsSettingsEnabled));
        }
    }

    public bool IsSettingsEnabled => !IsConnected;

    public RelayCommand RefreshPortsCommand { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand SelectFileCommand { get; }
    public RelayCommand OpenDownloadsCommand { get; }
    public RelayCommand DragOverCommand { get; }
    public RelayCommand DropCommand { get; }

    public bool CanAcceptFilesForSend() => _transport.IsOpen && !_engine.IsBusy;

    public async Task SendPathsAsync(IEnumerable<string> paths)
    {
        if (!_transport.IsOpen)
        {
            _dialogs.ShowInformation(_localization.Message_ConnectFirst, "PortaFile");
            return;
        }

        if (_engine.IsBusy)
        {
            _dialogs.ShowInformation(_localization.Message_Transferring, "PortaFile");
            return;
        }

        var selectedPaths = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (selectedPaths.Length == 0)
        {
            return;
        }

        try
        {
            await _engine.SendPathsAsync(selectedPaths);
        }
        catch (OperationCanceledException)
        {
            Progress.Status = "キャンセル";
        }
        catch (Exception ex)
        {
            Progress.Status = "送信失敗";
            Progress.CurrentFile = ex.Message;
            _dialogs.ShowError(ex.Message, _localization.Title_SendError);
        }
    }

    private void RefreshPorts()
    {
        var selected = SelectedPortName;
        var ports = SerialPort.GetPortNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

        PortNames.Clear();
        if (selected is not null && !ports.Contains(selected))
        {
            PortNames.Add(selected);
        }

        foreach (var port in ports)
        {
            PortNames.Add(port);
        }

        SelectedPortName = selected ?? ports.FirstOrDefault();
    }

    private void Connect()
    {
        try
        {
            var settings = GetSerialSettings();
            if (string.IsNullOrWhiteSpace(settings.PortName))
            {
                _dialogs.ShowWarning(_localization.Message_SelectComPort, "PortaFile");
                return;
            }

            _transport.Open(settings);
            _engine.StartReceiving();
            IsConnected = true;
            Progress.Status = "接続済み";
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, _localization.Title_ConnectError);
        }
    }

    private void Disconnect()
    {
        _engine.Stop();
        _transport.Close();
        IsConnected = false;
        Progress.Status = "切断";
    }

    private async Task CancelAsync()
    {
        await _engine.CancelAsync();
    }

    private async Task SelectFilesAsync()
    {
        var files = _dialogs.SelectFiles(_localization.Title_SelectFiles, _sendFileDirectory);
        if (files.Length > 0)
        {
            _sendFileDirectory = Path.GetDirectoryName(files[0]);
            SaveLastState();
            await SendPathsAsync(files);
        }
    }

    private void OpenDownloads()
    {
        try
        {
            _dialogs.OpenFolder(PathResolver.DownloadsDirectory);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, _localization.Title_CannotOpenFolder);
        }
    }

    private SerialSettings GetSerialSettings()
    {
        return new SerialSettings
        {
            PortName = SelectedPortName ?? "",
            BaudRate = SelectedBaudRate,
            Parity = Parity.None,
            DuplexMode = SelectedDuplexMode,
            HalfDuplexControl = SelectedHalfDuplexControl,
            ReliabilityMode = SelectedReliabilityMode
        };
    }

    private void ApplyLastState(ApplicationLastState state)
    {
        _selectedPortName = state.PortName;
        _selectedBaudRate = BaudRates.Contains(state.BaudRate) ? state.BaudRate : 115200;
        _selectedReliabilityMode = state.ReliabilityMode;
        _selectedDuplexMode = state.DuplexMode;
        _selectedHalfDuplexControl = state.HalfDuplexControl;
        _sendFileDirectory = Directory.Exists(state.SendFileDirectory) ? state.SendFileDirectory : null;
    }

    private void SaveLastState()
    {
        try
        {
            var state = new ApplicationLastState
            {
                PortName = SelectedPortName,
                BaudRate = SelectedBaudRate,
                Parity = Parity.None,
                DuplexMode = SelectedDuplexMode,
                HalfDuplexControl = SelectedHalfDuplexControl,
                ReliabilityMode = SelectedReliabilityMode,
                SendFileDirectory = _sendFileDirectory,
                UiLanguage = _lastStateService.Load().UiLanguage
            };
            _lastStateService.Save(state);
        }
        catch
        {
            // Last-state persistence should never block application shutdown.
        }
    }

    private void OnDragOver(object? parameter)
    {
        if (parameter is System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) && CanAcceptFilesForSend()
                ? System.Windows.DragDropEffects.Copy
                : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }
    }

    private async void OnDrop(object? parameter)
    {
        if (parameter is System.Windows.DragEventArgs e && e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!;
            await SendPathsAsync(paths);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        SaveLastState();
        _speedTimer.Stop();
        _engine.Stop();
        _transport.Dispose();
    }
}
