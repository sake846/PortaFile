namespace PortaFile.Transfer.Core;

using PortaFile.Protocol;
using PortaFile.Services;
using PortaFile.Transfer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TransferSession : IDisposable
{
    public Guid TransferId { get; internal set; }
    public int ExpectedReceiveSequence { get; set; }
    public TransferManifest? PendingManifest { get; internal set; }
    public List<string> PendingSources { get; set; } = new();
    public FileStream? ReceiveFileStream { get; set; }
    public string? ReceivePartPath { get; set; }
    public uint ReceiveFileCrc { get; set; }
    public long ReceiveBytesTransferred { get; set; }
    public long ReceiveCurrentFileBytes { get; set; }
    public FileManifestEntry? ReceiveCurrentEntry { get; set; }
    public int ReceiveNextBlockIndex { get; set; }
    public TransferReliabilityMode ActiveReceiveReliabilityMode { get; set; }
    public Guid? ExpectedManifestTransferId { get; set; }
    public Dictionary<int, byte[]> ReceiveDataBuffer { get; } = new();

    public void Dispose()
    {
        ReceiveFileStream?.Dispose();
        ReceiveFileStream = null;
    }
}
