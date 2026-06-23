namespace PortaFile.Protocol;

public sealed record Packet(
    PacketType Type,
    Guid TransferId,
    int Sequence,
    byte[] Payload,
    bool IsValid = true);
