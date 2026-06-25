using System.Windows.Media;

namespace PortaFile.Transfer;

public enum BlockState
{
    Pending,
    Active,
    Done,
    Retrying,
    Failed,
    Verifying
}

public enum TransferDirection
{
    Idle,
    Sending,
    Receiving
}

public sealed class ProgressBlock : INotifyPropertyChanged
{
    private BlockState _state;

    public BlockState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Fill));
        }
    }

    public Brush Fill => State switch
    {
        BlockState.Active => new SolidColorBrush(Color.FromRgb(42, 112, 184)),
        BlockState.Done => new SolidColorBrush(Color.FromRgb(47, 142, 100)),
        BlockState.Retrying => new SolidColorBrush(Color.FromRgb(190, 128, 36)),
        BlockState.Failed => new SolidColorBrush(Color.FromRgb(185, 56, 56)),
        BlockState.Verifying => new SolidColorBrush(Color.FromRgb(126, 87, 194)),
        _ => new SolidColorBrush(Color.FromRgb(203, 213, 225))
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class TransferProgress : INotifyPropertyChanged
{
    private string _status = "未接続";
    private string _currentFile = "";
    private double _overallPercent;
    private double _filePercent;
    private long _bytesTransferred;
    private long _totalBytes;
    private string _transferName = "";
    private int _transferFileCount;
    private int _transferFolderCount;
    private int _retryCount;
    private int _errorCount;
    private DateTime? _dataStartedAt;
    private DateTime? _lastDataAt;
    private TimeSpan? _lastSendDuration;
    private TimeSpan? _lastAckWaitDuration;
    private TransferDirection _direction;

    public ObservableCollection<ProgressBlock> Blocks { get; } = [];

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string CurrentFile
    {
        get => _currentFile;
        set => SetField(ref _currentFile, value);
    }

    public double OverallPercent
    {
        get => _overallPercent;
        set => SetField(ref _overallPercent, value);
    }

    public double FilePercent
    {
        get => _filePercent;
        set => SetField(ref _filePercent, value);
    }

    public long BytesTransferred
    {
        get => _bytesTransferred;
        set
        {
            var previous = _bytesTransferred;
            if (SetField(ref _bytesTransferred, value))
            {
                if (value > previous)
                {
                    var now = DateTime.UtcNow;
                    _dataStartedAt ??= now;
                    _lastDataAt = now;
                }

                OnPropertyChanged(nameof(SpeedText));
            }
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set => SetField(ref _totalBytes, value);
    }

    public string TransferName
    {
        get => _transferName;
        set => SetField(ref _transferName, value);
    }

    public int TransferFileCount
    {
        get => _transferFileCount;
        set => SetField(ref _transferFileCount, value);
    }

    public int TransferFolderCount
    {
        get => _transferFolderCount;
        set => SetField(ref _transferFolderCount, value);
    }

    public int RetryCount
    {
        get => _retryCount;
        set => SetField(ref _retryCount, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        set => SetField(ref _errorCount, value);
    }

    public string SpeedText
    {
        get
        {
            if (_dataStartedAt is null)
            {
                return "-";
            }

            var through = _lastDataAt ?? DateTime.UtcNow;
            var seconds = Math.Max(0.1, (through - _dataStartedAt.Value).TotalSeconds);
            return FormatBytes((long)(BytesTransferred / seconds)) + "/s";
        }
    }

    public string SendDurationText => FormatDuration(_lastSendDuration);

    public string AckWaitDurationText => FormatDuration(_lastAckWaitDuration);

    public string DropZoneTitle =>
        Direction is TransferDirection.Sending or TransferDirection.Receiving && !string.IsNullOrWhiteSpace(Status)
            ? Status
            : "ファイルまたはフォルダをドロップ";

    public string DropZoneSummary
    {
        get
        {
            if (Direction is not (TransferDirection.Sending or TransferDirection.Receiving))
            {
                return "接続済みの相手へ、相対パスと更新日時を保持して送信します";
            }

            var name = string.IsNullOrWhiteSpace(TransferName) ? "転送対象" : TransferName;
            var itemText = TransferFolderCount > 0
                ? $"ファイル {TransferFileCount} / フォルダ {TransferFolderCount}"
                : $"ファイル {TransferFileCount}";
            var sizeText = $"{FormatBytes(BytesTransferred)} / {FormatBytes(TotalBytes)}";
            var currentText = string.IsNullOrWhiteSpace(CurrentFile) ? "" : $" / 現在: {CurrentFile}";
            return $"{name} / {itemText} / 総サイズ {sizeText}{currentText}";
        }
    }

    public TransferDirection Direction
    {
        get => _direction;
        set
        {
            if (SetField(ref _direction, value))
            {
                OnPropertyChanged(nameof(DirectionAccent));
                OnPropertyChanged(nameof(DirectionPanelBackground));
            }
        }
    }

    public Brush DirectionAccent => Direction switch
    {
        TransferDirection.Sending => new SolidColorBrush(Color.FromRgb(35, 112, 184)),
        TransferDirection.Receiving => new SolidColorBrush(Color.FromRgb(47, 142, 100)),
        _ => new SolidColorBrush(Color.FromRgb(142, 157, 171))
    };

    public Brush DirectionPanelBackground => Direction switch
    {
        TransferDirection.Sending => new SolidColorBrush(Color.FromRgb(232, 242, 252)),
        TransferDirection.Receiving => new SolidColorBrush(Color.FromRgb(230, 247, 239)),
        _ => new SolidColorBrush(Color.FromRgb(248, 251, 253))
    };

    public void Reset(string status, long totalBytes, int blocks)
    {
        _dataStartedAt = null;
        _lastDataAt = null;
        _lastSendDuration = null;
        _lastAckWaitDuration = null;
        Status = status;
        CurrentFile = "";
        TransferName = "";
        TransferFileCount = 0;
        TransferFolderCount = 0;
        TotalBytes = totalBytes;
        BytesTransferred = 0;
        OverallPercent = 0;
        FilePercent = 0;
        RetryCount = 0;
        ErrorCount = 0;
        Blocks.Clear();
        for (var i = 0; i < Math.Max(1, Math.Min(blocks, 3000)); i++)
        {
            Blocks.Add(new ProgressBlock());
        }
    }

    public void SetTransferDetails(TransferManifest manifest)
    {
        TransferName = manifest.RootName;
        TransferFileCount = manifest.Files.Count;
        TransferFolderCount = manifest.RootFolderCount;
    }

    public void SetLinkTiming(TimeSpan sendDuration, TimeSpan ackWaitDuration)
    {
        _lastSendDuration = sendDuration;
        _lastAckWaitDuration = ackWaitDuration;
        OnPropertyChanged(nameof(SendDurationText));
        OnPropertyChanged(nameof(AckWaitDurationText));
    }

    public void SetBlock(int index, BlockState state)
    {
        if (index >= 0 && index < Blocks.Count)
        {
            Blocks[index].State = state;
        }
    }

    public void RefreshTransientText() => OnPropertyChanged(nameof(SpeedText));

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan? duration) =>
        duration is null ? "-" : $"{duration.Value.TotalMilliseconds:0.0} ms";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (AffectsDropZone(propertyName))
        {
            OnPropertyChanged(nameof(DropZoneTitle));
            OnPropertyChanged(nameof(DropZoneSummary));
        }
        return true;
    }

    private static bool AffectsDropZone(string? propertyName) =>
        propertyName is nameof(Status)
            or nameof(CurrentFile)
            or nameof(BytesTransferred)
            or nameof(TotalBytes)
            or nameof(Direction)
            or nameof(TransferName)
            or nameof(TransferFileCount)
            or nameof(TransferFolderCount);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
