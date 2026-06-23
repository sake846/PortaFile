namespace PortaFile.Protocol;

public static class Crc32
{
    private const uint Polynomial = 0xEDB88320u;
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> data, uint seed = 0xFFFFFFFFu)
    {
        var crc = seed;
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    public static async Task<uint> ComputeFileAsync(string path, CancellationToken cancellationToken)
    {
        var crc = 0xFFFFFFFFu;
        var buffer = new byte[64 * 1024];

        await using var stream = File.OpenRead(path);
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 1 ? Polynomial ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
