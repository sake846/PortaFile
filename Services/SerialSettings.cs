using System.IO.Ports;

namespace PortaFile.Services;

public enum DuplexMode
{
    FullDuplex,
    HalfDuplex
}

public enum HalfDuplexControl
{
    DriverManaged,
    Rts
}

public sealed class SerialSettings
{
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public Parity Parity { get; set; } = Parity.None;
    public DuplexMode DuplexMode { get; set; } = DuplexMode.FullDuplex;
    public HalfDuplexControl HalfDuplexControl { get; set; } = HalfDuplexControl.DriverManaged;
}
