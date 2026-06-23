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
        BlockState.Active => Brushes.DodgerBlue,
        BlockState.Done => Brushes.SeaGreen,
        BlockState.Retrying => Brushes.Goldenrod,
        BlockState.Failed => Brushes.Firebrick,
        BlockState.Verifying => Brushes.MediumPurple,
        _ => Brushes.DimGray
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
    private int _retryCount;
    private int _errorCount;
    private DateTime _startedAt = DateTime.UtcNow;

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
            if (SetField(ref _bytesTransferred, value))
            {
                OnPropertyChanged(nameof(SpeedText));
            }
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set => SetField(ref _totalBytes, value);
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
            var seconds = Math.Max(1, (DateTime.UtcNow - _startedAt).TotalSeconds);
            return FormatBytes((long)(BytesTransferred / seconds)) + "/s";
        }
    }

    public void Reset(string status, long totalBytes, int blocks)
    {
        _startedAt = DateTime.UtcNow;
        Status = status;
        CurrentFile = "";
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
