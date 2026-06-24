using PortaFile.Services;

namespace PortaFile.Transfer;

public sealed record HelloPayload(ulong NodeId);
public sealed record SendRequestPayload(
    ulong NodeId,
    Guid TransferId,
    string RootName,
    int FileCount,
    long TotalBytes,
    TransferReliabilityMode ReliabilityMode,
    int BaudRate,
    DuplexMode DuplexMode);
public sealed record ReadyPayload(ulong NodeId);
public sealed record BusyPayload(ulong NodeId, string Reason);
public sealed record FileStartPayload(int FileIndex, string RelativePath, long Size, uint Crc32);
public sealed record FileEndPayload(int FileIndex, uint Crc32);
public sealed record AckPayload(int Sequence);
public sealed record NakPayload(int ExpectedSequence, string Reason);
public sealed record DataBatchCheckPayload(int[] Sequences);
public sealed record DataBatchAckPayload(int[] MissingSequences);
public sealed record ErrorPayload(string Message);
