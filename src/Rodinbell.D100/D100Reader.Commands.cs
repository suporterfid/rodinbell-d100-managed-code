using System.Buffers.Binary;

namespace Rodinbell.D100;

public sealed partial class D100Reader
{
    public Task<byte> GetPowerAsync(CancellationToken cancellationToken = default) => RunAsync((s, token) =>
    {
        var f = s.Single(0x77, [], options.CommandTimeout, token);
        RequireLength(f, 1);
        if (f.Payload[0] > 33) throw new ReaderCommandException(f.Command, f.Payload[0]);
        return f.Payload[0];
    }, cancellationToken);

    /// <summary>Changes power without writing flash. D100 documented range: 18–26 dBm.</summary>
    public Task SetPowerAsync(byte dbm, CancellationToken cancellationToken = default)
    {
        if (dbm is < 18 or > 26) throw new ArgumentOutOfRangeException(nameof(dbm));
        return RunAsync((s, token) => { s.Acknowledge(0x66, [dbm], options.CommandTimeout, token); desiredPower = dbm; return true; }, cancellationToken);
    }

    public Task<bool> GetFastTidAsync(CancellationToken cancellationToken = default) => RunAsync((s, token) =>
    {
        var f = s.Single(0x8E, [], options.CommandTimeout, token);
        RequireLength(f, 1);
        if (f.Payload[0] is not (0 or 0x8D)) throw new ReaderCommandException(f.Command, f.Payload[0]);
        fastTidEnabled = f.Payload[0] == 0x8D;
        return fastTidEnabled.Value;
    }, cancellationToken);

    public Task SetFastTidAsync(bool enabled, bool persist = false, CancellationToken cancellationToken = default) => RunAsync((s, token) =>
    {
        s.Acknowledge(persist ? (byte)0x8D : (byte)0x8C, [enabled ? (byte)0x8D : (byte)0], options.CommandTimeout, token);
        desiredFastTid = fastTidEnabled = enabled;
        return true;
    }, cancellationToken);

    public Task SetBuzzerAsync(BuzzerMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return RunAsync((s, token) => { s.Acknowledge(0x7A, [(byte)mode], options.CommandTimeout, token); desiredBuzzer = mode; return true; }, cancellationToken);
    }

    public Task<int> GetTemperatureAsync(CancellationToken cancellationToken = default) => RunAsync((s, token) =>
    {
        var f = s.Single(0x7B, [], options.CommandTimeout, token);
        RequireLength(f, 2);
        if (f.Payload[0] > 1) throw new ReaderProtocolException("Invalid temperature sign byte.");
        return f.Payload[0] == 0 ? -f.Payload[1] : f.Payload[1];
    }, cancellationToken);

    public Task<InventoryRound> InventoryBufferedAsync(byte repeat = 1, CancellationToken cancellationToken = default)
    {
        if (repeat == 0) throw new ArgumentOutOfRangeException(nameof(repeat));
        return RunAsync((s, token) =>
        {
            RequireStoppedInventory();
            var f = s.Single(0x80, [repeat], options.RoundTimeout, token);
            if (f.Payload.Length == 1)
            {
                if (f.Payload[0] == 0x36) return new InventoryRound(1, 0, 0, 0);
                throw new ReaderCommandException(f.Command, f.Payload[0]);
            }
            return TagDecoder.DecodeSummary(f);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<TagRead>> ReadBufferAsync(bool clearAfterRead = false, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<TagRead>>((s, token) =>
        {
            RequireStoppedInventory();
            var tags = new List<TagRead>();
            ushort? expected = null;
            byte command = clearAfterRead ? (byte)0x91 : (byte)0x90;
            s.Exchange(command, [], options.RoundTimeout, token, f =>
            {
                if (f.Payload.Length == 1)
                {
                    if ((f.Payload[0] is 0x36 or 0x38) && tags.Count == 0) return true;
                    throw new ReaderCommandException(f.Command, f.Payload[0]);
                }
                var record = TagDecoder.DecodeBuffered(f, DateTimeOffset.UtcNow, fastTidEnabled != false, options.FastTidEpcLengthBytes);
                expected ??= record.TotalTags;
                if (expected != record.TotalTags || expected == 0 || tags.Count >= expected) throw new ReaderProtocolException("Inconsistent buffered tag count.");
                tags.Add(record.Tag with { PortName = s.Info.Port.PortName });
                return tags.Count == expected;
            });
            return tags.AsReadOnly();
        }, cancellationToken);

    public Task<ushort> GetBufferCountAsync(CancellationToken cancellationToken = default) => RunAsync((s, token) =>
    {
        RequireStoppedInventory();
        var f = s.Single(0x92, [], options.CommandTimeout, token);
        RequireLength(f, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(f.Payload);
    }, cancellationToken);

    public Task ClearBufferAsync(CancellationToken cancellationToken = default) => RunAsync((s, token) =>
    { RequireStoppedInventory(); s.Acknowledge(0x93, [], options.CommandTimeout, token); return true; }, cancellationToken);

    private static void RequireLength(Frame frame, int length)
    {
        if (frame.Payload.Length == length) return;
        if (frame.Payload.Length == 1) throw new ReaderCommandException(frame.Command, frame.Payload[0]);
        throw new ReaderProtocolException($"Command 0x{frame.Command:X2}: expected {length} data bytes, received {frame.Payload.Length}.");
    }
}
