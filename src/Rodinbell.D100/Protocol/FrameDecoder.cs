namespace Rodinbell.D100;

internal sealed class FrameDecoder
{
    private const byte StartByte = 0xA0;
    private const int MaximumBufferedBytes = 4096;
    private readonly List<byte> buffer = new(MaximumBufferedBytes);

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.Length >= MaximumBufferedBytes)
        {
            buffer.Clear();
            data = data[^MaximumBufferedBytes..];
        }
        else if (buffer.Count + data.Length > MaximumBufferedBytes)
        {
            buffer.RemoveRange(0, buffer.Count + data.Length - MaximumBufferedBytes);
        }

        foreach (byte value in data)
        {
            buffer.Add(value);
        }
    }

    public bool TryRead(out Frame frame)
    {
        while (true)
        {
            int start = buffer.IndexOf(StartByte);
            if (start < 0)
            {
                buffer.Clear();
                frame = null!;
                return false;
            }

            if (start > 0)
            {
                buffer.RemoveRange(0, start);
            }

            if (buffer.Count < 2)
            {
                frame = null!;
                return false;
            }

            int declaredLength = buffer[1];
            if (declaredLength < 3)
            {
                buffer.RemoveAt(0);
                continue;
            }

            int frameLength = declaredLength + 2;
            if (buffer.Count < frameLength)
            {
                frame = null!;
                return false;
            }

            if (!HasValidChecksum(0, frameLength))
            {
                buffer.RemoveAt(0);
                continue;
            }

            byte[] payload = buffer.GetRange(4, declaredLength - 3).ToArray();
            frame = new Frame(buffer[2], buffer[3], payload);
            buffer.RemoveRange(0, frameLength);
            return true;
        }
    }

    public void Reset() => buffer.Clear();

    private bool HasValidChecksum(int offset, int length)
    {
        byte sum = 0;
        for (int index = offset; index < offset + length; index++)
        {
            sum = unchecked((byte)(sum + buffer[index]));
        }

        return sum == 0;
    }
}
