using System.Buffers.Binary;

namespace PortaFile.Protocol;

public static class PacketCodec
{
    private const uint Magic = 0x50465431;
    private const byte Version = 1;
    private const int HeaderLength = 34;
    private const int MaxPayloadLength = 2 * 1024 * 1024;

    public static byte[] Encode(Packet packet)
    {
        var payload = packet.Payload;
        var frame = new byte[HeaderLength + payload.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), Magic);
        frame[4] = Version;
        frame[5] = (byte)packet.Type;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6, 4), packet.Sequence);
        packet.TransferId.TryWriteBytes(frame.AsSpan(10, 16));
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(26, 4), payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(30, 4), Crc32.Compute(payload));
        payload.CopyTo(frame.AsSpan(HeaderLength));

        return frame;
    }

    public static async Task<Packet> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderLength];
        await ReadUntilMagicAsync(stream, header, cancellationToken);

        if (header[4] != Version)
        {
            throw new InvalidDataException($"Unsupported packet version {header[4]}.");
        }

        var type = (PacketType)header[5];
        var sequence = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(6, 4));
        var transferId = new Guid(header.AsSpan(10, 16));
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(26, 4));
        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(30, 4));

        if (payloadLength < 0 || payloadLength > MaxPayloadLength)
        {
            throw new InvalidDataException($"Invalid payload length {payloadLength}.");
        }

        var payload = new byte[payloadLength];
        await ReadExactAsync(stream, payload, cancellationToken);
        var actualCrc = Crc32.Compute(payload);

        return new Packet(type, transferId, sequence, payload, actualCrc == expectedCrc);
    }

    private static async Task ReadUntilMagicAsync(Stream stream, byte[] header, CancellationToken cancellationToken)
    {
        var window = new Queue<byte>(4);
        while (true)
        {
            var one = new byte[1];
            await ReadExactAsync(stream, one, cancellationToken);
            window.Enqueue(one[0]);
            if (window.Count > 4)
            {
                window.Dequeue();
            }

            if (window.Count != 4)
            {
                continue;
            }

            var bytes = window.ToArray();
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
            {
                continue;
            }

            bytes.CopyTo(header, 0);
            await ReadExactAsync(stream, header.AsMemory(4), cancellationToken);
            return;
        }
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Serial stream closed.");
            }

            offset += read;
        }
    }
}
