using System.Collections.Concurrent;

namespace Rodinbell.D100.Tests;

internal sealed class FakeReader : IReaderTransportFactory
{
    internal static readonly PortDescriptor Unit = new("COM4", "unit-A", "Test D100", 0x0403, 0x6001);
    public List<PortDescriptor> Ports { get; } = [Unit];
    public ConcurrentQueue<byte[]> Requests { get; } = new();
    public ConcurrentQueue<string> OpenedPorts { get; } = new();
    public byte Power = 18;
    public bool FastTid;
    public BuzzerMode Buzzer;
    public byte TemperatureSign = 1;
    public byte Temperature = 25;
    public bool BufferEmpty;
    public bool DisconnectNextRound;
    public bool ChangeIdentityOnFailure;
    public bool BlockSummary;
    public bool CorruptNextReply;
    public byte? RejectCommand;
    public int Inventories;
    public int ActiveTransports;
    public int MaxChunk = 3;
    public ManualResetEventSlim SummaryReleased { get; } = new(false);
    public Action<byte, Transport>? Intercept;

    public IReadOnlyList<PortDescriptor> GetPorts() { lock (Ports) return Ports.ToArray(); }
    public IReaderTransport Open(PortDescriptor port, int baudRate)
    {
        OpenedPorts.Enqueue(port.PortName);
        Interlocked.Increment(ref ActiveTransports);
        return new Transport(this);
    }

    internal static byte[] Frame(byte cmd, params byte[] payload)
    {
        var bytes = new byte[payload.Length + 5];
        bytes[0] = 0xA0; bytes[1] = (byte)(payload.Length + 3); bytes[2] = 1; bytes[3] = cmd;
        payload.CopyTo(bytes, 4);
        bytes[^1] = unchecked((byte)-bytes.Take(bytes.Length - 1).Sum(b => b));
        return bytes;
    }
    internal static byte[] Tag => Convert.FromHexString("083000E2801191A50300650518E79963");
    internal static byte[] Summary => [0, 0, 17, 0, 0, 0, 1];
    internal static byte[] BufferTag => Convert.FromHexString("0001103000E2801191A50300650518E7991234630803");

    internal sealed class Transport(FakeReader device) : IReaderTransport
    {
        private readonly ConcurrentQueue<byte> incoming = new();
        private bool waitingSummary;
        private bool disposed;
        public void Reply(byte cmd, params byte[] payload)
        {
            var frame = Frame(cmd, payload);
            if (device.CorruptNextReply) { device.CorruptNextReply = false; frame[^1] ^= 1; }
            foreach (byte b in frame) incoming.Enqueue(b);
        }
        public void Write(byte[] data)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            device.Requests.Enqueue(data.ToArray());
            byte cmd = data[3];
            if (device.RejectCommand == cmd) { Reply(cmd, 0x41); return; }
            if (device.Intercept is not null) { device.Intercept(cmd, this); return; }
            switch (cmd)
            {
                case 0x72: Reply(cmd, 1, 9); break;
                case 0x77: Reply(cmd, device.Power); break;
                case 0x66: device.Power = data[4]; Reply(cmd, 0x10); break;
                case 0x8E: Reply(cmd, device.FastTid ? (byte)0x8D : (byte)0); break;
                case 0x8C: case 0x8D: device.FastTid = data[4] == 0x8D; Reply(cmd, 0x10); break;
                case 0x7A: device.Buzzer = (BuzzerMode)data[4]; Reply(cmd, 0x10); break;
                case 0x7B: Reply(cmd, device.TemperatureSign, device.Temperature); break;
                case 0x80: device.BufferEmpty = false; Reply(cmd, 0, 0, 1, 0, 17, 0, 0, 0, 1); break;
                case 0x92: Reply(cmd, 0, device.BufferEmpty ? (byte)0 : (byte)1); break;
                case 0x93: device.BufferEmpty = true; Reply(cmd, 0x10); break;
                case 0x90: case 0x91:
                    if (device.BufferEmpty) Reply(cmd, 0x38); else Reply(cmd, BufferTag);
                    if (cmd == 0x91) device.BufferEmpty = true;
                    break;
                case 0x89:
                    Interlocked.Increment(ref device.Inventories);
                    if (device.DisconnectNextRound)
                    {
                        device.DisconnectNextRound = false;
                        lock (device.Ports) device.Ports[0] = Unit with { PortName = "COM7", DeviceId = device.ChangeIdentityOnFailure ? "unit-B" : Unit.DeviceId };
                        throw new IOException("Simulated USB removal.");
                    }
                    Reply(cmd, Tag);
                    if (device.BlockSummary) waitingSummary = true; else Reply(cmd, Summary);
                    break;
                default: Reply(cmd, 0x41); break;
            }
        }
        public int Read(byte[] buffer)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (waitingSummary && device.SummaryReleased.IsSet) { waitingSummary = false; Reply(0x89, Summary); }
            int count = 0;
            while (count < Math.Min(buffer.Length, device.MaxChunk) && incoming.TryDequeue(out byte value)) buffer[count++] = value;
            if (count == 0) Thread.Sleep(5);
            return count;
        }
        public void Dispose() { if (!disposed) { disposed = true; Interlocked.Decrement(ref device.ActiveTransports); } }
    }
}
