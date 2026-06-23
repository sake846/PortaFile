using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PortaFile.Services;
using PortaFile.Transfer;

namespace PortaFile;

public partial class MainWindow : Window
{
    private readonly SerialTransport _transport = new();
    private readonly TransferProgress _progress = new();
    private readonly TransferEngine _engine;
    private readonly DispatcherTimer _speedTimer = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _progress;
        _engine = new TransferEngine(
            _transport,
            GetSerialSettings,
            _progress,
            ConfirmRetryAsync,
            action => Dispatcher.Invoke(action));

        BaudRateComboBox.ItemsSource = new[] { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600, 1000000, 2000000, 3000000 };
        BaudRateComboBox.SelectedItem = 115200;

        RefreshPorts();

        _speedTimer.Interval = TimeSpan.FromSeconds(1);
        _speedTimer.Tick += (_, _) => _progress.RefreshTransientText();
        _speedTimer.Start();
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void RefreshPorts()
    {
        var selected = PortComboBox.SelectedItem as string;
        var ports = SerialPort.GetPortNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        PortComboBox.ItemsSource = ports;
        PortComboBox.SelectedItem = ports.Contains(selected) ? selected : ports.FirstOrDefault();
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = GetSerialSettings();
            if (string.IsNullOrWhiteSpace(settings.PortName))
            {
                MessageBox.Show(this, "COMポートを選択してください。", "PortaFile", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _transport.Open(settings);
            _engine.StartReceiving();
            SetConnected(true);
            _progress.Status = "接続済み";
            ConnectionText.Text = $"{settings.PortName} / {settings.BaudRate}bps";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "接続エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _engine.Stop();
        _transport.Close();
        SetConnected(false);
        _progress.Status = "切断";
        ConnectionText.Text = "未接続";
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        await _engine.CancelAsync();
        CancelButton.IsEnabled = _transport.IsOpen;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _transport.IsOpen && !_engine.IsBusy
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!_transport.IsOpen)
        {
            MessageBox.Show(this, "先にCOMポートへ接続してください。", "PortaFile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_engine.IsBusy)
        {
            MessageBox.Show(this, "転送中です。", "PortaFile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var paths = ((string[])e.Data.GetData(DataFormats.FileDrop)!).Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        CancelButton.IsEnabled = true;
        try
        {
            await _engine.SendPathsAsync(paths);
        }
        catch (OperationCanceledException)
        {
            _progress.Status = "キャンセル";
        }
        catch (Exception ex)
        {
            _progress.Status = "送信失敗";
            _progress.CurrentFile = ex.Message;
            MessageBox.Show(this, ex.Message, "送信エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CancelButton.IsEnabled = _transport.IsOpen;
        }
    }

    private SerialSettings GetSerialSettings()
    {
        return new SerialSettings
        {
            PortName = PortComboBox.SelectedItem as string ?? "",
            BaudRate = BaudRateComboBox.SelectedItem is int baudRate ? baudRate : 115200,
            Parity = GetTag<Parity>(ParityComboBox),
            DuplexMode = GetTag<DuplexMode>(DuplexComboBox),
            HalfDuplexControl = GetTag<HalfDuplexControl>(HalfDuplexControlComboBox)
        };
    }

    private T GetTag<T>(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem item && item.Tag is T value
            ? value
            : default!;
    }

    private Task<bool> ConfirmRetryAsync(string message)
    {
        var result = Dispatcher.Invoke(() =>
            MessageBox.Show(this, message, "再送確認", MessageBoxButton.YesNo, MessageBoxImage.Warning));
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    private void SetConnected(bool connected)
    {
        ConnectButton.IsEnabled = !connected;
        DisconnectButton.IsEnabled = connected;
        CancelButton.IsEnabled = connected;
        PortComboBox.IsEnabled = !connected;
        BaudRateComboBox.IsEnabled = !connected;
        ParityComboBox.IsEnabled = !connected;
        DuplexComboBox.IsEnabled = !connected;
        HalfDuplexControlComboBox.IsEnabled = !connected;
    }

    protected override void OnClosed(EventArgs e)
    {
        _speedTimer.Stop();
        _engine.Stop();
        _transport.Dispose();
        base.OnClosed(e);
    }
}
