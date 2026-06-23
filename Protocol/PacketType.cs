namespace PortaFile.Protocol;

public enum PacketType : byte
{
    Hello = 1,
    SendRequest = 2,
    Ready = 3,
    Busy = 4,
    Manifest = 5,
    FileStart = 6,
    Data = 7,
    Ack = 8,
    Nak = 9,
    FileEnd = 10,
    FileOk = 11,
    FileError = 12,
    TransferEnd = 13,
    Cancel = 14,
    Error = 15
}
