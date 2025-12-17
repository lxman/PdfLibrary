namespace ImageLibrary.Png;

/// <summary>
/// CRC-32 calculator for PNG chunks.
/// Uses the polynomial 0xEDB88320 (reflected form of 0x04C11DB7).
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = new uint[256];

    static Crc32()
    {
        const uint polynomial = 0xEDB88320;

        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (var j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
            Table[i] = crc;
        }
    }

    /// <summary>
    /// Calculate CRC-32 for a byte array.
    /// </summary>
    public static uint Calculate(byte[] data)
    {
        return Calculate(data, 0, data.Length);
    }

    /// <summary>
    /// Calculate CRC-32 for a portion of a byte array.
    /// </summary>
    public static uint Calculate(byte[] data, int offset, int length)
    {
        var crc = 0xFFFFFFFF;

        for (var i = 0; i < length; i++)
        {
            var index = (byte)((crc ^ data[offset + i]) & 0xFF);
            crc = (crc >> 8) ^ Table[index];
        }

        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>
    /// Update a running CRC with additional data.
    /// </summary>
    public static uint Update(uint crc, byte[] data, int offset, int length)
    {
        crc ^= 0xFFFFFFFF;

        for (var i = 0; i < length; i++)
        {
            var index = (byte)((crc ^ data[offset + i]) & 0xFF);
            crc = (crc >> 8) ^ Table[index];
        }

        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>
    /// Calculate CRC-32 for chunk type + data (as used in PNG).
    /// </summary>
    public static uint CalculateChunkCrc(string type, byte[] data, int offset, int length)
    {
        var crc = 0xFFFFFFFF;

        // Include type bytes
        foreach (char c in type)
        {
            var index = (byte)((crc ^ (byte)c) & 0xFF);
            crc = (crc >> 8) ^ Table[index];
        }

        // Include data bytes
        for (var i = 0; i < length; i++)
        {
            var index = (byte)((crc ^ data[offset + i]) & 0xFF);
            crc = (crc >> 8) ^ Table[index];
        }

        return crc ^ 0xFFFFFFFF;
    }
}
