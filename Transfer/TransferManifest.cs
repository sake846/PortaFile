using PortaFile.Services;

namespace PortaFile.Transfer;

public sealed class TransferManifest
{
    public Guid TransferId { get; set; }
    public string RootName { get; set; } = "Transfer";
    public long TotalBytes { get; set; }
    public TransferReliabilityMode ReliabilityMode { get; set; } = TransferReliabilityMode.Arq;
    public List<FileManifestEntry> Files { get; set; } = [];
}

public sealed class FileManifestEntry
{
    public int Index { get; set; }
    public string RelativePath { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public uint Crc32 { get; set; }
    public int BlockCount => (int)Math.Max(1, (Size + TransferConstants.BlockSize - 1) / TransferConstants.BlockSize);
}

public static class TransferConstants
{
    public const int BlockSize = 10 * 1024;
    public const int ArqWindowSize = 10;
    public const int MaxRetries = 10;
}
