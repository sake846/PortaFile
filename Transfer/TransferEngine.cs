using PortaFile.Protocol;
using PortaFile.Services;
using System.Diagnostics;
using System.Security.Cryptography;

namespace PortaFile.Transfer;

public sealed class TransferEngine
{
    private readonly SerialTransport _transport;
    private readonly Func<SerialSettings> _settingsProvider;
    private readonly Func<string, Task<bool>> _confirmRetryAsync;
    private readonly Action<Action> _ui;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _stateLock = new();
    private readonly Dictionary<int, TaskCompletionSource<bool>> _ackWaiters = new();
    private readonly object _dataBatchAckLock = new();
    private readonly ulong _nodeId = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));

    private CancellationTokenSource? _receiveCts;
    private CancellationTokenSource? _transferCts;
    private TransferManifest? _pendingManifest;
    private List<string> _pendingSources = [];
    private TaskCompletionSource<bool>? _readyWaiter;
    private TaskCompletionSource<DataBatchAckPayload>? _dataBatchAckWaiter;
    private Guid? _expectedManifestTransferId;
    private int _expectedReceiveSequence;
    private bool _isBusy;
    private FileStream? _receiveFileStream;
    private string? _receivePartPath;
    private uint _receiveFileCrc;
    private long _receiveBytesTransferred;
    private long _receiveCurrentFileBytes;
    private FileManifestEntry? _receiveCurrentEntry;
    private TransferReliabilityMode _activeReceiveReliabilityMode = TransferReliabilityMode.Arq;
    private readonly Dictionary<int, byte[]> _receiveDataBuffer = [];
    private int _receiveNextBlockIndex;

    private sealed record PendingDataBlock(
        int Sequence,
        byte[] Payload,
        int BlockIndex,
        long FileBytesAfterBlock,
        long TotalBytesAfterBlock)
    {
        public int RetryCount { get; set; }
    }

    public TransferEngine(
        SerialTransport transport,
        Func<SerialSettings> settingsProvider,
        TransferProgress progress,
        Func<string, Task<bool>> confirmRetryAsync,
        Action<Action> ui)
    {
        _transport = transport;
        _settingsProvider = settingsProvider;
        Progress = progress;
        _confirmRetryAsync = confirmRetryAsync;
        _ui = ui;
    }

    public TransferProgress Progress { get; }
    public bool IsBusy => _isBusy;

    public void StartReceiving()
    {
        _receiveCts?.Cancel();
        _receiveCts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
    }

    public async Task SendPathsAsync(IEnumerable<string> paths)
    {
        if (_isBusy)
        {
            throw new InvalidOperationException("転送中です。");
        }

        _transferCts = new CancellationTokenSource();
        var cancellationToken = _transferCts.Token;

        SetBusy(true);
        try
        {
            UpdateProgress(p =>
            {
                p.Direction = TransferDirection.Sending;
                p.Status = "マニフェスト作成中";
                p.CurrentFile = "";
            });

            var built = await ManifestBuilder.BuildAsync(paths, cancellationToken);
            _pendingManifest = built.Manifest;
            _pendingSources = built.SourceFiles;
            var blockCount = built.Manifest.Files.Sum(f => f.BlockCount);
            var reliabilityMode = _settingsProvider().ReliabilityMode;
            built.Manifest.ReliabilityMode = reliabilityMode;

            if (reliabilityMode == TransferReliabilityMode.OneWay)
            {
                UpdateProgress(p => p.Reset("片方向送信中", built.Manifest.TotalBytes, blockCount));
            }
            else
            {
                UpdateProgress(p => p.Reset("送信要求中", built.Manifest.TotalBytes, blockCount));

                _readyWaiter = NewWaiter();
                await SendJsonAsync(PacketType.SendRequest, built.Manifest.TransferId, 0,
                    new SendRequestPayload(_nodeId, built.Manifest.TransferId, built.Manifest.RootName, built.Manifest.Files.Count, built.Manifest.TotalBytes, reliabilityMode),
                    cancellationToken);

                if (!await WaitWithTimeoutAsync(_readyWaiter.Task, TimeSpan.FromSeconds(5), cancellationToken))
                {
                    throw new TimeoutException("送信要求がタイムアウトしました。");
                }
            }

            await SendJsonAsync(PacketType.Manifest, built.Manifest.TransferId, 0, built.Manifest, cancellationToken);
            if (reliabilityMode == TransferReliabilityMode.OneWay)
            {
                await SendFilesOneWayAsync(built.Manifest, built.SourceFiles, cancellationToken);
            }
            else
            {
                await SendFilesAsync(built.Manifest, built.SourceFiles, cancellationToken);
            }

            await SendJsonAsync(PacketType.TransferEnd, built.Manifest.TransferId, 0, new { }, cancellationToken);

            UpdateProgress(p =>
            {
                p.Direction = TransferDirection.Idle;
                p.Status = "送信完了";
                p.OverallPercent = 100;
                p.FilePercent = 100;
            });
        }
        finally
        {
            _pendingManifest = null;
            _pendingSources = [];
            _readyWaiter = null;
            SetBusy(false);
        }
    }

    public async Task CancelAsync()
    {
        _transferCts?.Cancel();
        _expectedManifestTransferId = null;
        try
        {
            await SendJsonAsync(PacketType.Cancel, _pendingManifest?.TransferId ?? Guid.Empty, 0,
                new ErrorPayload("キャンセルされました。"), CancellationToken.None);
        }
        catch
        {
            // Best effort.
        }

        await CleanupReceiveFileAsync(deletePart: true);
        UpdateProgress(p =>
        {
            p.Direction = TransferDirection.Idle;
            p.Status = "キャンセル";
        });
        SetBusy(false);
    }

    public void Stop()
    {
        _receiveCts?.Cancel();
        _transferCts?.Cancel();
        _expectedManifestTransferId = null;
    }

    private async Task SendFilesAsync(TransferManifest manifest, List<string> sources, CancellationToken cancellationToken)
    {
        var globalBlockIndex = 0;
        var transferred = 0L;
        var sequence = 1;

        for (var fileIndex = 0; fileIndex < manifest.Files.Count; fileIndex++)
        {
            var entry = manifest.Files[fileIndex];
            var sourcePath = sources[fileIndex];
            var fileBlockStart = globalBlockIndex;

            UpdateProgress(p =>
            {
                p.Status = "送信中";
                p.CurrentFile = entry.RelativePath;
                p.FilePercent = 0;
            });

            await SendControlWithRetryAsync(PacketType.FileStart, manifest.TransferId, sequence++,
                new FileStartPayload(entry.Index, entry.RelativePath, entry.Size, entry.Crc32), cancellationToken);

            await using var stream = File.OpenRead(sourcePath);
            var buffer = new byte[TransferConstants.BlockSize];
            var fileTransferred = 0L;
            var localBlock = 0;
            var pending = new List<PendingDataBlock>();
            var reachedEndOfFile = false;

            while (!reachedEndOfFile || pending.Count > 0)
            {
                while (pending.Count < TransferConstants.ArqWindowSize && !reachedEndOfFile)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        reachedEndOfFile = true;
                        break;
                    }

                    var payload = buffer.AsSpan(0, read).ToArray();
                    pending.Add(new PendingDataBlock(
                        sequence++,
                        payload,
                        fileBlockStart + localBlock,
                        fileTransferred + read,
                        transferred + read));

                    fileTransferred += read;
                    transferred += read;
                    localBlock++;
                    globalBlockIndex++;
                }

                if (pending.Count == 0)
                {
                    continue;
                }

                await SendDataWindowAsync(manifest.TransferId, pending, manifest.TotalBytes, entry.Size, cancellationToken);
            }

            await SendControlWithRetryAsync(PacketType.FileEnd, manifest.TransferId, sequence++,
                new FileEndPayload(entry.Index, entry.Crc32), cancellationToken);
        }
    }

    private async Task SendFilesOneWayAsync(TransferManifest manifest, List<string> sources, CancellationToken cancellationToken)
    {
        var globalBlockIndex = 0;
        var transferred = 0L;
        var sequence = 1;

        for (var fileIndex = 0; fileIndex < manifest.Files.Count; fileIndex++)
        {
            var entry = manifest.Files[fileIndex];
            var sourcePath = sources[fileIndex];

            UpdateProgress(p =>
            {
                p.Status = "送信中(ARQなし)";
                p.CurrentFile = entry.RelativePath;
                p.FilePercent = 0;
            });

            await SendJsonAsync(PacketType.FileStart, manifest.TransferId, sequence++,
                new FileStartPayload(entry.Index, entry.RelativePath, entry.Size, entry.Crc32), cancellationToken);

            await using var stream = File.OpenRead(sourcePath);
            var buffer = new byte[TransferConstants.BlockSize];
            var fileTransferred = 0L;

            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                var payload = buffer.AsSpan(0, read).ToArray();
                var sendDuration = await SendMeasuredAsync(
                    new Packet(PacketType.Data, manifest.TransferId, sequence++, payload),
                    cancellationToken);

                transferred += read;
                fileTransferred += read;
                var blockIndex = globalBlockIndex++;
                UpdateProgress(p =>
                {
                    p.SetLinkTiming(sendDuration, TimeSpan.Zero);
                    p.BytesTransferred = transferred;
                    p.OverallPercent = manifest.TotalBytes == 0 ? 100 : transferred * 100.0 / manifest.TotalBytes;
                    p.FilePercent = entry.Size == 0 ? 100 : fileTransferred * 100.0 / entry.Size;
                    p.SetBlock(blockIndex, BlockState.Done);
                });
            }

            await SendJsonAsync(PacketType.FileEnd, manifest.TransferId, sequence++,
                new FileEndPayload(entry.Index, entry.Crc32), cancellationToken);
        }
    }

    private async Task SendControlWithRetryAsync<T>(
        PacketType type,
        Guid transferId,
        int sequence,
        T payload,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
        var packet = new Packet(type, transferId, sequence, bytes);
        var retries = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var waiter = RegisterAckWaiter(sequence);
            var sendDuration = await SendMeasuredAsync(packet, cancellationToken);

            var ackStopwatch = Stopwatch.StartNew();
            var ok = await WaitWithTimeoutAsync(waiter.Task, CalculateTimeout(_settingsProvider().BaudRate), cancellationToken);
            ackStopwatch.Stop();
            UpdateProgress(p => p.SetLinkTiming(sendDuration, ackStopwatch.Elapsed));
            if (ok && waiter.Task.Result)
            {
                return;
            }

            retries++;
            UpdateProgress(p =>
            {
                p.RetryCount++;
                p.ErrorCount++;
            });

            if (retries <= TransferConstants.MaxRetries)
            {
                continue;
            }

            if (await _confirmRetryAsync("最大再送回数を超えました。続行しますか？"))
            {
                retries = 0;
                continue;
            }

            throw new OperationCanceledException("最大再送回数を超えたため中止しました。", cancellationToken);
        }
    }

    private async Task SendDataWindowAsync(
        Guid transferId,
        List<PendingDataBlock> pending,
        long totalBytes,
        long currentFileSize,
        CancellationToken cancellationToken)
    {
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var block in pending)
            {
                UpdateProgress(p => p.SetBlock(block.BlockIndex, block.RetryCount == 0 ? BlockState.Active : BlockState.Retrying));
            }

            var sendStopwatch = Stopwatch.StartNew();
            foreach (var block in pending)
            {
                await _transport.SendAsync(new Packet(PacketType.Data, transferId, block.Sequence, block.Payload), _settingsProvider(), cancellationToken);
            }

            var waiter = RegisterDataBatchAckWaiter();
            await SendJsonAsync(PacketType.DataBatchCheck, transferId, 0,
                new DataBatchCheckPayload(pending.Select(x => x.Sequence).ToArray()), cancellationToken);
            sendStopwatch.Stop();

            var ackStopwatch = Stopwatch.StartNew();
            var ack = await WaitForDataBatchAckAsync(waiter, CalculateTimeout(_settingsProvider().BaudRate), cancellationToken);
            ackStopwatch.Stop();
            UpdateProgress(p => p.SetLinkTiming(sendStopwatch.Elapsed, ackStopwatch.Elapsed));

            if (ack is null)
            {
                foreach (var block in pending)
                {
                    block.RetryCount++;
                }

                UpdateProgress(p =>
                {
                    p.RetryCount++;
                    p.ErrorCount++;
                });

                if (pending.All(x => x.RetryCount <= TransferConstants.MaxRetries))
                {
                    continue;
                }

                foreach (var block in pending)
                {
                    UpdateProgress(p => p.SetBlock(block.BlockIndex, BlockState.Failed));
                }

                if (await _confirmRetryAsync("最大再送回数を超えました。続行しますか？"))
                {
                    foreach (var block in pending)
                    {
                        block.RetryCount = 0;
                    }

                    continue;
                }

                throw new OperationCanceledException("最大再送回数を超えたため中止しました。", cancellationToken);
            }

            var missing = ack.MissingSequences.ToHashSet();
            var completed = pending.Where(x => !missing.Contains(x.Sequence)).ToArray();
            foreach (var block in completed)
            {
                UpdateProgress(p =>
                {
                    p.SetBlock(block.BlockIndex, BlockState.Done);
                    p.BytesTransferred = Math.Max(p.BytesTransferred, block.TotalBytesAfterBlock);
                    p.OverallPercent = totalBytes == 0 ? 100 : p.BytesTransferred * 100.0 / totalBytes;
                    p.FilePercent = currentFileSize == 0 ? 100 : block.FileBytesAfterBlock * 100.0 / currentFileSize;
                });
            }

            pending.RemoveAll(x => !missing.Contains(x.Sequence));
            if (pending.Count == 0)
            {
                return;
            }

            foreach (var block in pending)
            {
                block.RetryCount++;
            }

            UpdateProgress(p =>
            {
                p.RetryCount += pending.Count;
                p.ErrorCount += pending.Count;
            });

            if (pending.All(x => x.RetryCount <= TransferConstants.MaxRetries))
            {
                return;
            }

            foreach (var block in pending)
            {
                UpdateProgress(p => p.SetBlock(block.BlockIndex, BlockState.Failed));
            }

            if (!await _confirmRetryAsync("最大再送回数を超えました。続行しますか？"))
            {
                throw new OperationCanceledException("最大再送回数を超えたため中止しました。", cancellationToken);
            }

            foreach (var block in pending)
            {
                block.RetryCount = 0;
            }

            return;
        }
    }

    private TaskCompletionSource<bool> RegisterAckWaiter(int sequence)
    {
        var waiter = NewWaiter();
        lock (_ackWaiters)
        {
            _ackWaiters[sequence] = waiter;
        }

        return waiter;
    }

    private TaskCompletionSource<DataBatchAckPayload> RegisterDataBatchAckWaiter()
    {
        var waiter = new TaskCompletionSource<DataBatchAckPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_dataBatchAckLock)
        {
            _dataBatchAckWaiter = waiter;
        }

        return waiter;
    }

    private async Task<DataBatchAckPayload?> WaitForDataBatchAckAsync(
        TaskCompletionSource<DataBatchAckPayload> waiter,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var delay = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(waiter.Task, delay);
        lock (_dataBatchAckLock)
        {
            if (ReferenceEquals(_dataBatchAckWaiter, waiter))
            {
                _dataBatchAckWaiter = null;
            }
        }

        return completed == waiter.Task ? await waiter.Task : null;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var packet = await PacketCodec.ReadAsync(_transport.Stream, cancellationToken);
                await HandlePacketAsync(packet, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                UpdateProgress(p =>
                {
                    p.Status = "受信エラー";
                    p.CurrentFile = ex.Message;
                    p.ErrorCount++;
                });
                await Task.Delay(500, cancellationToken).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task HandlePacketAsync(Packet packet, CancellationToken cancellationToken)
    {
        if (!packet.IsValid)
        {
            UpdateProgress(p => p.ErrorCount++);
            if (!IsActiveReceiveOneWay() && packet.Type != PacketType.Data)
            {
                await SendJsonAsync(PacketType.Nak, packet.TransferId, 0,
                    new NakPayload(_expectedReceiveSequence, "CRC不一致"), cancellationToken);
            }

            return;
        }

        switch (packet.Type)
        {
            case PacketType.SendRequest:
                await HandleSendRequestAsync(ReadJson<SendRequestPayload>(packet.Payload), packet.TransferId, cancellationToken);
                break;
            case PacketType.Ready:
                _readyWaiter?.TrySetResult(true);
                break;
            case PacketType.Busy:
                _readyWaiter?.TrySetException(new InvalidOperationException(ReadJson<BusyPayload>(packet.Payload).Reason));
                break;
            case PacketType.Manifest:
                await HandleManifestAsync(ReadJson<TransferManifest>(packet.Payload), cancellationToken);
                break;
            case PacketType.FileStart:
                await HandleFileStartAsync(ReadJson<FileStartPayload>(packet.Payload), packet.Sequence, packet.TransferId, cancellationToken);
                break;
            case PacketType.Data:
                await HandleDataAsync(packet, cancellationToken);
                break;
            case PacketType.DataBatchCheck:
                await HandleDataBatchCheckAsync(ReadJson<DataBatchCheckPayload>(packet.Payload), packet.TransferId, cancellationToken);
                break;
            case PacketType.DataBatchAck:
                CompleteDataBatchAck(ReadJson<DataBatchAckPayload>(packet.Payload));
                break;
            case PacketType.FileEnd:
                await HandleFileEndAsync(ReadJson<FileEndPayload>(packet.Payload), packet.Sequence, packet.TransferId, cancellationToken);
                break;
            case PacketType.Ack:
                CompleteAck(ReadJson<AckPayload>(packet.Payload).Sequence, true);
                break;
            case PacketType.Nak:
                CompleteAck(ReadJson<NakPayload>(packet.Payload).ExpectedSequence, false);
                break;
            case PacketType.TransferEnd:
                UpdateProgress(p =>
                {
                    p.Direction = TransferDirection.Idle;
                    p.Status = "受信完了";
                });
                _pendingManifest = null;
                SetBusy(false);
                break;
            case PacketType.Cancel:
                await CleanupReceiveFileAsync(deletePart: true);
                UpdateProgress(p =>
                {
                    p.Direction = TransferDirection.Idle;
                    p.Status = "相手側がキャンセル";
                });
                _pendingManifest = null;
                SetBusy(false);
                break;
            case PacketType.Error:
                UpdateProgress(p =>
                {
                    p.Status = "相手側エラー";
                    p.CurrentFile = ReadJson<ErrorPayload>(packet.Payload).Message;
                    p.ErrorCount++;
                });
                break;
        }
    }

    private async Task HandleSendRequestAsync(SendRequestPayload request, Guid transferId, CancellationToken cancellationToken)
    {
        if (_isBusy && (_pendingManifest is null || request.NodeId > _nodeId))
        {
            await SendJsonAsync(PacketType.Busy, transferId, 0, new BusyPayload(_nodeId, "転送中です。"), cancellationToken);
            return;
        }

        if (_isBusy && _pendingManifest is not null && request.NodeId < _nodeId)
        {
            _transferCts?.Cancel();
        }

        SetBusy(true);
        _activeReceiveReliabilityMode = request.ReliabilityMode;
        _expectedManifestTransferId = transferId;
        _expectedReceiveSequence = 1;
        _receiveDataBuffer.Clear();
        _receiveNextBlockIndex = 0;
        var status = IsActiveReceiveOneWay() ? "受信準備完了(ARQなし)" : "受信準備完了";
        UpdateProgress(p =>
        {
            p.Direction = TransferDirection.Receiving;
            p.Reset(status, request.TotalBytes, Math.Max(1, (int)((request.TotalBytes + TransferConstants.BlockSize - 1) / TransferConstants.BlockSize)));
        });
        await SendJsonAsync(PacketType.Ready, transferId, 0, new ReadyPayload(_nodeId), cancellationToken);
    }

    private Task HandleManifestAsync(TransferManifest manifest, CancellationToken cancellationToken)
    {
        if (_expectedManifestTransferId is Guid expectedTransferId && expectedTransferId != manifest.TransferId)
        {
            UpdateProgress(p =>
            {
                p.Status = "受信要求無視";
                p.CurrentFile = "想定外のマニフェストです。";
                p.ErrorCount++;
            });
            return Task.CompletedTask;
        }

        if (_isBusy && _expectedManifestTransferId is null && _pendingManifest is not null)
        {
            UpdateProgress(p =>
            {
                p.Status = "受信要求無視";
                p.CurrentFile = "転送中です。";
                p.ErrorCount++;
            });
            return Task.CompletedTask;
        }

        _expectedManifestTransferId = null;
        SetBusy(true);
        _activeReceiveReliabilityMode = manifest.ReliabilityMode;
        _pendingManifest = manifest;
        _expectedReceiveSequence = 1;
        _receiveDataBuffer.Clear();
        _receiveNextBlockIndex = 0;
        _receiveBytesTransferred = 0;
        _receiveCurrentFileBytes = 0;
        _receiveCurrentEntry = null;
        var blocks = manifest.Files.Sum(x => x.BlockCount);
        UpdateProgress(p =>
        {
            p.Direction = TransferDirection.Receiving;
            p.Reset(IsActiveReceiveOneWay() ? "受信中(ARQなし)" : "受信中", manifest.TotalBytes, blocks);
        });
        return Task.CompletedTask;
    }

    private async Task HandleFileStartAsync(FileStartPayload payload, int sequence, Guid transferId, CancellationToken cancellationToken)
    {
        if (!ValidateSequence(sequence, transferId, cancellationToken, out var task))
        {
            await task;
            return;
        }

        await CleanupReceiveFileAsync(deletePart: true);
        _receiveDataBuffer.Clear();
        var manifest = _pendingManifest ?? throw new InvalidOperationException("マニフェスト未受信です。");
        var entry = manifest.Files.Single(x => x.Index == payload.FileIndex);
        _receivePartPath = PathResolver.CreatePartPath(manifest, entry);
        _receiveFileStream = File.Create(_receivePartPath);
        _receiveFileCrc = payload.Crc32;
        _receiveCurrentFileBytes = 0;
        _receiveCurrentEntry = entry;

        UpdateProgress(p =>
        {
            p.CurrentFile = entry.RelativePath;
            p.FilePercent = 0;
        });

        _expectedReceiveSequence = sequence + 1;
        if (!IsActiveReceiveOneWay())
        {
            await AckAsync(transferId, sequence, cancellationToken);
        }
    }

    private async Task HandleDataAsync(Packet packet, CancellationToken cancellationToken)
    {
        if (IsActiveReceiveOneWay())
        {
            await HandleOneWayDataAsync(packet, cancellationToken);
            return;
        }

        if (_receiveFileStream is null)
        {
            if (!IsActiveReceiveOneWay())
            {
                await SendJsonAsync(PacketType.Nak, packet.TransferId, 0,
                    new NakPayload(_expectedReceiveSequence, "FILE_START未受信です。"), cancellationToken);
            }

            return;
        }

        if (packet.Sequence < _expectedReceiveSequence)
        {
            return;
        }

        _receiveDataBuffer.TryAdd(packet.Sequence, packet.Payload);
    }

    private async Task HandleOneWayDataAsync(Packet packet, CancellationToken cancellationToken)
    {
        if (!ValidateSequence(packet.Sequence, packet.TransferId, cancellationToken, out var task))
        {
            await task;
            return;
        }

        if (_receiveFileStream is null)
        {
            return;
        }

        await WriteReceivedDataAsync(packet.Payload, cancellationToken);
        _expectedReceiveSequence = packet.Sequence + 1;
    }

    private async Task HandleDataBatchCheckAsync(DataBatchCheckPayload payload, Guid transferId, CancellationToken cancellationToken)
    {
        if (IsActiveReceiveOneWay())
        {
            return;
        }

        var missingSequences = payload.Sequences
            .Where(sequence => sequence >= _expectedReceiveSequence && !_receiveDataBuffer.ContainsKey(sequence))
            .ToArray();

        if (missingSequences.Length == 0)
        {
            await CommitBufferedDataAsync(cancellationToken);
        }

        await SendJsonAsync(PacketType.DataBatchAck, transferId, 0,
            new DataBatchAckPayload(missingSequences), cancellationToken);
    }

    private async Task CommitBufferedDataAsync(CancellationToken cancellationToken)
    {
        while (_receiveDataBuffer.Remove(_expectedReceiveSequence, out var payload))
        {
            await WriteReceivedDataAsync(payload, cancellationToken);
            _expectedReceiveSequence++;
        }
    }

    private async Task WriteReceivedDataAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (_receiveFileStream is null)
        {
            return;
        }

        await _receiveFileStream.WriteAsync(payload, cancellationToken);
        _receiveBytesTransferred += payload.Length;
        _receiveCurrentFileBytes += payload.Length;
        var total = _receiveBytesTransferred;
        var currentFileBytes = _receiveCurrentFileBytes;
        var currentEntry = _receiveCurrentEntry;
        var blockIndex = _receiveNextBlockIndex++;

        UpdateProgress(p =>
        {
            p.BytesTransferred = total;
            p.OverallPercent = p.TotalBytes == 0 ? 100 : total * 100.0 / p.TotalBytes;
            p.FilePercent = currentEntry is null || currentEntry.Size == 0 ? 100 : currentFileBytes * 100.0 / currentEntry.Size;
            p.SetBlock(blockIndex, BlockState.Done);
        });
    }

    private async Task HandleFileEndAsync(FileEndPayload payload, int sequence, Guid transferId, CancellationToken cancellationToken)
    {
        if (!ValidateSequence(sequence, transferId, cancellationToken, out var task))
        {
            await task;
            return;
        }

        if (_receiveFileStream is null || _receivePartPath is null)
        {
            if (!IsActiveReceiveOneWay())
            {
                await SendJsonAsync(PacketType.Nak, transferId, 0,
                    new NakPayload(_expectedReceiveSequence, "受信ファイルがありません。"), cancellationToken);
            }

            return;
        }

        await _receiveFileStream.DisposeAsync();
        _receiveFileStream = null;

        UpdateProgress(p => p.Status = "検証中");
        var actualCrc = await Crc32.ComputeFileAsync(_receivePartPath, cancellationToken);
        if (actualCrc != _receiveFileCrc || actualCrc != payload.Crc32)
        {
            File.Delete(_receivePartPath);
            _receivePartPath = null;
            UpdateProgress(p =>
            {
                p.Status = "受信CRCエラー";
                p.CurrentFile = "ファイルCRC不一致";
                p.ErrorCount++;
            });

            if (!IsActiveReceiveOneWay())
            {
                await SendJsonAsync(PacketType.FileError, transferId, sequence, new ErrorPayload("ファイルCRC不一致"), cancellationToken);
                await SendJsonAsync(PacketType.Nak, transferId, 0, new NakPayload(sequence, "ファイルCRC不一致"), cancellationToken);
            }

            return;
        }

        var finalPath = PathResolver.GetAvailableFinalPath(_receivePartPath);
        File.Move(_receivePartPath, finalPath);
        var entry = _pendingManifest?.Files.SingleOrDefault(x => x.Index == payload.FileIndex);
        if (entry is not null)
        {
            File.SetLastWriteTimeUtc(finalPath, entry.LastWriteTimeUtc);
        }

        _receivePartPath = null;
        _expectedReceiveSequence = sequence + 1;
        if (!IsActiveReceiveOneWay())
        {
            await AckAsync(transferId, sequence, cancellationToken);
        }

        UpdateProgress(p => p.Status = IsActiveReceiveOneWay() ? "受信中(ARQなし)" : "受信中");
    }

    private bool ValidateSequence(int sequence, Guid transferId, CancellationToken cancellationToken, out Task recoveryTask)
    {
        if (sequence == _expectedReceiveSequence)
        {
            recoveryTask = Task.CompletedTask;
            return true;
        }

        UpdateProgress(p => p.ErrorCount++);
        if (IsActiveReceiveOneWay())
        {
            _expectedReceiveSequence = sequence;
            recoveryTask = Task.CompletedTask;
            return true;
        }

        recoveryTask = SendJsonAsync(PacketType.Nak, transferId, 0,
            new NakPayload(_expectedReceiveSequence, $"想定外シーケンス {sequence}"), cancellationToken);
        return false;
    }

    private bool IsActiveReceiveOneWay() =>
        _activeReceiveReliabilityMode == TransferReliabilityMode.OneWay;

    private Task AckAsync(Guid transferId, int sequence, CancellationToken cancellationToken) =>
        SendJsonAsync(PacketType.Ack, transferId, 0, new AckPayload(sequence), cancellationToken);

    private void CompleteAck(int sequence, bool ok)
    {
        TaskCompletionSource<bool>? waiter;
        lock (_ackWaiters)
        {
            if (!_ackWaiters.Remove(sequence, out waiter))
            {
                return;
            }
        }

        waiter.TrySetResult(ok);
    }

    private void CompleteDataBatchAck(DataBatchAckPayload payload)
    {
        TaskCompletionSource<DataBatchAckPayload>? waiter;
        lock (_dataBatchAckLock)
        {
            waiter = _dataBatchAckWaiter;
            _dataBatchAckWaiter = null;
        }

        waiter?.TrySetResult(payload);
    }

    private async Task CleanupReceiveFileAsync(bool deletePart)
    {
        if (_receiveFileStream is not null)
        {
            await _receiveFileStream.DisposeAsync();
            _receiveFileStream = null;
        }

        if (deletePart && _receivePartPath is not null && File.Exists(_receivePartPath))
        {
            File.Delete(_receivePartPath);
        }

        _receivePartPath = null;
    }

    private Task SendJsonAsync<T>(PacketType type, Guid transferId, int sequence, T payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
        return _transport.SendAsync(new Packet(type, transferId, sequence, bytes), _settingsProvider(), cancellationToken);
    }

    private async Task<TimeSpan> SendMeasuredAsync(Packet packet, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await _transport.SendAsync(packet, _settingsProvider(), cancellationToken);
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private T ReadJson<T>(byte[] payload) =>
        JsonSerializer.Deserialize<T>(payload, _jsonOptions) ?? throw new InvalidDataException($"Invalid {typeof(T).Name} payload.");

    private void UpdateProgress(Action<TransferProgress> update) => _ui(() => update(Progress));

    private void SetBusy(bool value)
    {
        lock (_stateLock)
        {
            _isBusy = value;
        }
    }

    private static TaskCompletionSource<bool> NewWaiter() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<bool> WaitWithTimeoutAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var delay = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(task, delay);
        if (completed != task)
        {
            return false;
        }

        await task;
        return true;
    }

    private static async Task<bool> WaitWithTimeoutAsync(Task<bool> task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var delay = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(task, delay);
        if (completed != task)
        {
            return false;
        }

        return await task;
    }

    private static TimeSpan CalculateTimeout(int baudRate)
    {
        var seconds = TransferConstants.BlockSize * 10.0 / Math.Max(1, baudRate) + 2;
        return TimeSpan.FromSeconds(Math.Max(3, seconds));
    }
}
