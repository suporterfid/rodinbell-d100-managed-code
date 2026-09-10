namespace Rodinbell.D100;

internal static class FrameCodec
{
    private const byte StartByte = 0xA0;

    public static byte[] Encode(byte address, byte command, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 252)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "A D100 frame payload cannot exceed 252 bytes.");
        }

        var encoded = new byte[payload.Length + 5];
        encoded[0] = StartByte;
        encoded[1] = checked((byte)(payload.Length + 3));
        encoded[2] = address;
        encoded[3] = command;
        payload.CopyTo(encoded.AsSpan(4));
        encoded[^1] = Checksum(encoded.AsSpan(0, encoded.Length - 1));
        return encoded;
    }

    internal static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        byte sum = 0;
        foreach (byte value in bytes)
        {
            sum = unchecked((byte)(sum + value));
        }

        return unchecked((byte)-sum);
    }
}
