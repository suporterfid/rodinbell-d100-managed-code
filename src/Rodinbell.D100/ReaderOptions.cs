namespace Rodinbell.D100;

public sealed record ReaderOptions
{
    public string? PortName { get; init; }
    public string? DeviceId { get; init; }
    public ushort VendorId { get; init; } = 0x0403;
    public ushort ProductId { get; init; } = 0x6001;
    public IReadOnlyList<int> BaudRates { get; init; } = [115200, 38400];
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromMilliseconds(800);
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan RoundTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public bool AutoReconnect { get; init; } = true;
    public int ReconnectAttempts { get; init; } = 10;
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public int? FastTidEpcLengthBytes { get; init; }
    /// <summary>Known startup mode; null preserves ambiguous identifiers until queried or set. Use false for a verified standard-EPC reader.</summary>
    public bool? InitialFastTidEnabled { get; init; }

    internal ReaderOptions Validated()
    {
        if (BaudRates.Count == 0 || BaudRates.Any(b => b <= 0)) throw new ArgumentException("Provide positive baud rates.");
        if (ProbeTimeout <= TimeSpan.Zero || CommandTimeout <= TimeSpan.Zero || RoundTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(CommandTimeout));
        if (ReconnectAttempts < 1 || ReconnectDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ReconnectAttempts));
        if (FastTidEpcLengthBytes is int size && (size < 2 || size > 62 || size % 2 != 0))
            throw new ArgumentOutOfRangeException(nameof(FastTidEpcLengthBytes));
        return this with { BaudRates = BaudRates.ToArray() };
    }
}

public sealed record ReadingOptions
{
    public byte Repeat { get; init; } = 1;
    public TimeSpan Interval { get; init; } = TimeSpan.Zero;
    public TimeSpan DeduplicationWindow { get; init; } = TimeSpan.Zero;
    public int MaxTrackedTags { get; init; } = 4096;
    public int ChannelCapacity { get; init; } = 1024;

    internal void Validate()
    {
        if (Repeat == 0) throw new ArgumentOutOfRangeException(nameof(Repeat));
        if (Interval < TimeSpan.Zero || DeduplicationWindow < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Interval));
        if (MaxTrackedTags < 1 || ChannelCapacity < 1) throw new ArgumentOutOfRangeException(nameof(ChannelCapacity));
    }
}
