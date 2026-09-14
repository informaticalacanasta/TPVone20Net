namespace TPVOne.LegacyAccess.Core.Binary;

public static class LegacyBinaryPayloadInterpreter
{
    private static readonly int[] DibHeaderSizes = [12, 40, 56, 108, 124];
    private static readonly int[] BitCounts = [1, 4, 8, 16, 24, 32];

    public static byte[] Interpret(byte[] decoded)
    {
        if (decoded.Length == 0)
        {
            return decoded;
        }

        if (TryReadValidBmp(decoded, 0, out var fullLength) &&
            fullLength == decoded.Length)
        {
            return decoded;
        }

        for (var offset = 0; offset <= decoded.Length - 14; offset++)
        {
            if (decoded[offset] != 0x42 || decoded[offset + 1] != 0x4D)
            {
                continue;
            }

            if (!TryReadValidBmp(decoded, offset, out var bmpLength))
            {
                continue;
            }

            var bmp = new byte[bmpLength];
            Buffer.BlockCopy(decoded, offset, bmp, 0, bmpLength);
            return bmp;
        }

        return decoded;
    }

    public static bool TryReadValidBmp(byte[] data, int offset, out int length)
    {
        length = 0;
        if (offset < 0 || data.Length - offset < 26)
        {
            return false;
        }

        if (data[offset] != 0x42 || data[offset + 1] != 0x4D)
        {
            return false;
        }

        var remaining = data.Length - offset;
        var declaredSize = ReadUInt32(data, offset + 2);
        var pixelOffset = ReadUInt32(data, offset + 10);
        var dibSize = ReadUInt32(data, offset + 14);

        if (declaredSize < 26 || declaredSize > remaining)
        {
            return false;
        }

        if (!DibHeaderSizes.Contains((int)dibSize))
        {
            return false;
        }

        if (pixelOffset < 14 + dibSize || pixelOffset >= declaredSize)
        {
            return false;
        }

        if (dibSize == 12)
        {
            var planes = ReadUInt16(data, offset + 22);
            var bitCount = ReadUInt16(data, offset + 24);
            if (planes != 1 || !BitCounts.Contains(bitCount))
            {
                return false;
            }
        }
        else
        {
            if (remaining < 30)
            {
                return false;
            }

            var planes = ReadUInt16(data, offset + 26);
            var bitCount = ReadUInt16(data, offset + 28);
            if (planes != 1 || !BitCounts.Contains(bitCount))
            {
                return false;
            }
        }

        length = (int)declaredSize;
        return true;
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)(data[offset]
            | data[offset + 1] << 8
            | data[offset + 2] << 16
            | data[offset + 3] << 24);
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        return (ushort)(data[offset] | data[offset + 1] << 8);
    }
}
