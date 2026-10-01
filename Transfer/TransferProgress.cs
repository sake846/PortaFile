using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

internal static class ProgressBrush
{
    public static Brush Create(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class ProgressBlock : INotifyPropertyChanged
{
    private static readonly Brush ActiveBrush = ProgressBrush.Create(Color.FromRgb(42, 112, 184));
    private static readonly Brush DoneBrush = ProgressBrush.Create(Color.FromRgb(47, 142, 100));
    private static readonly Brush RetryingBrush = ProgressBrush.Create(Color.FromRgb(190, 128, 36));
    private static readonly Brush FailedBrush = ProgressBrush.Create(Color.FromRgb(185, 56, 56));
    private static readonly Brush VerifyingBrush = ProgressBrush.Create(Color.FromRgb(126, 87, 194));
    private static readonly Brush DefaultBrush = ProgressBrush.Create(Color.FromRgb(203, 213, 225));

    private BlockState _state;
    private uint? _dataHash;
    private Brush? _dataBrush;

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

    public uint? DataHash
    {
        get => _dataHash;
        set
        {
            if (_dataHash == value)
            {
                return;
            }

            _dataHash = value;
            _dataBrush = value.HasValue ? ProgressBrush.Create(HashToColor(value.Value)) : null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Fill));
        }
    }

    public void SetPayload(ReadOnlySpan<byte> payload)
    {
        DataHash = ComputeHash(payload);
    }

    public Brush Fill
    {
        get
        {
            if (State == BlockState.Pending)
            {
                return DefaultBrush;
            }

            if (State == BlockState.Failed)
            {
                return FailedBrush;
            }

            if (_dataBrush is not null)
            {
                return _dataBrush;
            }

            return State switch
            {
                BlockState.Active => ActiveBrush,
                BlockState.Done => DoneBrush,
                BlockState.Retrying => RetryingBrush,
                BlockState.Verifying => VerifyingBrush,
                _ => DefaultBrush
            };
        }
    }

    public static uint ComputeHash(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        uint hash = 2166136261u;
        foreach (var b in data)
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }

    public static Color HashToColor(uint hash)
    {
        double hue = hash % 360;
        double saturation = 0.65 + (((hash >> 8) & 0x1F) / 100.0);
        double lightness = 0.45 + (((hash >> 16) & 0x0F) / 100.0);
        return HslToRgb(hue, Math.Clamp(saturation, 0.6, 0.95), Math.Clamp(lightness, 0.4, 0.6));
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs((2 * l) - 1)) * s;
        double x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
        double m = l - (c / 2.0);

        double r = 0, g = 0, b = 0;

        if (0 <= h && h < 60) { r = c; g = x; b = 0; }
        else if (60 <= h && h < 120) { r = x; g = c; b = 0; }
        else if (120 <= h && h < 180) { r = 0; g = c; b = x; }
        else if (180 <= h && h < 240) { r = 0; g = x; b = c; }
        else if (240 <= h && h < 300) { r = x; g = 0; b = c; }
        else if (300 <= h && h < 360) { r = c; g = 0; b = x; }

        byte red = (byte)Math.Clamp((r + m) * 255.0, 0, 255);
        byte green = (byte)Math.Clamp((g + m) * 255.0, 0, 255);
        byte blue = (byte)Math.Clamp((b + m) * 255.0, 0, 255);

        return Color.FromRgb(red, green, blue);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class TransferProgress : INotifyPropertyChanged
{
    private static readonly Brush SendingAccentBrush = ProgressBrush.Create(Color.FromRgb(219, 39, 119));
    private static readonly Brush ReceivingAccentBrush = ProgressBrush.Create(Color.FromRgb(101, 163, 13));
    private static readonly Brush DefaultAccentBrush = ProgressBrush.Create(Color.FromRgb(142, 157, 171));
    private static readonly Brush SendingPanelBackgroundBrush = ProgressBrush.Create(Color.FromRgb(253, 232, 240));
    private static readonly Brush ReceivingPanelBackgroundBrush = ProgressBrush.Create(Color.FromRgb(236, 252, 203));
    private static readonly Brush DefaultPanelBackgroundBrush = ProgressBrush.Create(Color.FromRgb(248, 251, 253));

    private const int MaxProgressBlocks = 512;
    private const double BytesInKiB = 1024.0;
    private int _totalBlocks = 1;

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

    public string DropZoneTargetText =>
        string.IsNullOrWhiteSpace(TransferName) ? "転送対象" : TransferName;

    public string DropZoneTransferredText =>
        $"{FormatBytes(BytesTransferred)} / {FormatBytes(TotalBytes)}";

    public string DropZoneCurrentFileText =>
        string.IsNullOrWhiteSpace(CurrentFile) ? "-" : CurrentFile;

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
        TransferDirection.Sending => SendingAccentBrush,
        TransferDirection.Receiving => ReceivingAccentBrush,
        _ => DefaultAccentBrush
    };

    public Brush DirectionPanelBackground => Direction switch
    {
        TransferDirection.Sending => SendingPanelBackgroundBrush,
        TransferDirection.Receiving => ReceivingPanelBackgroundBrush,
        _ => DefaultPanelBackgroundBrush
    };

    private int _activeBlockIndex = -1;

    public int ActiveBlockIndex
    {
        get => _activeBlockIndex;
        set => SetField(ref _activeBlockIndex, value);
    }

    public void Reset(string status, long totalBytes, int blocks)
    {
        _totalBlocks = Math.Max(1, blocks);
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
        ActiveBlockIndex = -1;
        Blocks.Clear();
        for (var i = 0; i < Math.Min(_totalBlocks, MaxProgressBlocks); i++)
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

    public void SetBlock(int index, BlockState state, byte[]? payload = null)
    {
        if (index >= 0 && index < _totalBlocks)
        {
            // A visible block represents a range when the transfer exceeds the display cap.
            var displayIndex = (int)((long)index * Blocks.Count / _totalBlocks);
            var block = Blocks[displayIndex];
            if (payload is { Length: > 0 } && block.DataHash is null)
            {
                block.SetPayload(payload);
            }

            block.State = state;
            ActiveBlockIndex = displayIndex;
        }
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= BytesInKiB && unit < units.Length - 1)
        {
            value /= BytesInKiB;
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
            OnPropertyChanged(nameof(DropZoneTargetText));
            OnPropertyChanged(nameof(DropZoneTransferredText));
            OnPropertyChanged(nameof(DropZoneCurrentFileText));
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
            or nameof(TransferFolderCount)
            or nameof(ErrorCount)
            or nameof(RetryCount);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
