using System.IO.Ports;

namespace PortaFile.Services;

public sealed class ApplicationLastState
{
    public string? PortName { get; init; }
    public int BaudRate { get; init; } = 115200;
    public Parity Parity { get; init; } = Parity.None;
    public DuplexMode DuplexMode { get; init; } = DuplexMode.HalfDuplex;
    public HalfDuplexControl HalfDuplexControl { get; init; } = HalfDuplexControl.DriverManaged;
    public TransferReliabilityMode ReliabilityMode { get; init; } = TransferReliabilityMode.Arq;
    public string? SendFileDirectory { get; init; }
    public string? UiLanguage { get; init; } = "auto";
}
