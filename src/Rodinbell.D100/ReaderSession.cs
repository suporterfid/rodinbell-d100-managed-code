using System.Diagnostics;

namespace Rodinbell.D100;

internal sealed class ReaderSession(IReaderTransport transport, ReaderInfo info) : IDisposable
{
    private readonly FrameDecoder decoder = new();
    private readonly byte[] receiveBuffer = new byte[1024];
    public ReaderInfo Info { get; private set; } = info;

    public void Handshake(TimeSpan timeout, CancellationToken token)
    {
        var frame = Single(0x72, [], timeout, token);
        if (frame.Payload.Length != 2) throw new ReaderProtocolException("Firmware reply must contain major and minor bytes.");
        Info = Info with { Address = frame.Address, FirmwareMajor = frame.Payload[0], FirmwareMinor = frame.Payload[1] };
    }

    public Frame Single(byte command, byte[] payload, TimeSpan timeout, CancellationToken token)
    {
        Frame? response = null;
        Exchange(command, payload, timeout, token, frame => { response = frame; return true; });
        return response!;
    }

    public void Acknowledge(byte command, byte[] payload, TimeSpan timeout, CancellationToken token)
    {
        var reply = Single(command, payload, timeout, token);
        if (reply.Payload.Length != 1) throw new ReaderProtocolException($"Command 0x{command:X2} did not return a status byte.");
        if (reply.Payload[0] != 0x10) throw new ReaderCommandException(command, reply.Payload[0]);
    }

    public void Exchange(byte command, byte[] payload, TimeSpan timeout, CancellationToken token, Func<Frame, bool> accept)
    {
        token.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        transport.Write(FrameCodec.Encode(Info.Address, command, payload));
        while (clock.Elapsed < timeout)
        {
            token.ThrowIfCancellationRequested();
            while (decoder.TryRead(out var frame))
            {
                if (frame.Command == command && (Info.Address == 0xFF || frame.Address == Info.Address) && accept(frame)) return;
            }
            int count = transport.Read(receiveBuffer);
            if (count < 0 || count > receiveBuffer.Length) throw new ReaderProtocolException("Invalid transport byte count.");
            if (count > 0) decoder.Append(receiveBuffer.AsSpan(0, count));
        }
        throw new TimeoutException($"Reader command 0x{command:X2} did not complete within {timeout.TotalMilliseconds:0} ms.");
    }

    public void Dispose() => transport.Dispose();
}
