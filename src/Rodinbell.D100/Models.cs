namespace Rodinbell.D100;

public enum BuzzerMode : byte { Silent = 0, AfterRound = 1, AfterEveryTag = 2 }
public enum ReaderState { Disconnected, Connected, Reading, Reconnecting, Disposed }
public enum TagReadSource { RealTime, Buffer }

public sealed record PortDescriptor(string PortName, string? DeviceId = null,
    string? FriendlyName = null, ushort? VendorId = null, ushort? ProductId = null);

public sealed record ReaderInfo(PortDescriptor Port, int BaudRate, byte Address, byte FirmwareMajor, byte FirmwareMinor)
{
    public string FirmwareVersion => $"{FirmwareMajor}.{FirmwareMinor}";
}

/// <summary>A decoded observation. Epc is null when a FastTID payload cannot be split safely.</summary>
public sealed record TagRead
{
    public string? Epc { get; init; }
    public required string IdentifierHex { get; init; }
    public string? Tid { get; init; }
    public string? Crc { get; init; }
    public bool FastTidAmbiguous { get; init; }
    public ushort Pc { get; init; }
    public byte Antenna { get; init; }
    public byte FrequencyIndex { get; init; }
    public byte RssiRaw { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; init; }
    public long ReadCount { get; init; } = 1;
    public TagReadSource Source { get; init; }
    public string? PortName { get; init; }
    public required string RawPayloadHex { get; init; }
}

public sealed record InventoryRound(byte Antenna, ushort ReadRate, uint TotalRead, ushort? BufferedTagCount = null);
internal sealed record BufferedTagRecord(ushort TotalTags, TagRead Tag);

public sealed class ReaderCommandException(byte command, byte status)
    : Exception($"Reader command 0x{command:X2} returned status 0x{status:X2}.")
{
    public byte Command { get; } = command;
    public byte Status { get; } = status;
}

public sealed class ReaderProtocolException(string message) : IOException(message);
public sealed class ReaderDiscoveryException(string message) : IOException(message);
