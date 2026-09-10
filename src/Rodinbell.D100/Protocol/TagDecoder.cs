namespace Rodinbell.D100;

internal static class TagDecoder
{
    public static TagRead DecodeRealtime(
        Frame frame,
        DateTimeOffset timestamp,
        bool fastTidEnabled = false,
        int? fastTidEpcLengthBytes = null)
    {
        if (frame.Command != 0x89)
        {
            throw ProtocolError($"Command 0x{frame.Command:X2} is not a real-time inventory response.");
        }

        ReadOnlySpan<byte> payload = frame.Payload;
        if (payload.Length < 6)
        {
            throw ProtocolError("A real-time tag payload must contain FreqAnt, PC, an identifier, and RSSI.");
        }

        ushort pc = ReadUInt16(payload, 1);
        ReadOnlySpan<byte> identifier = payload[3..^1];
        IdentifierParts parts = DecodeIdentifier(pc, identifier, fastTidEnabled, fastTidEpcLengthBytes, null);
        return CreateTag(
            pc,
            identifier,
            parts,
            payload[0],
            payload[^1],
            timestamp,
            1,
            TagReadSource.RealTime,
            frame.Payload);
    }

    public static BufferedTagRecord DecodeBuffered(
        Frame frame,
        DateTimeOffset timestamp,
        bool fastTidEnabled = false,
        int? fastTidEpcLengthBytes = null)
    {
        if (frame.Command is not (0x90 or 0x91))
        {
            throw ProtocolError($"Command 0x{frame.Command:X2} is not a buffered tag response.");
        }

        ReadOnlySpan<byte> payload = frame.Payload;
        if (payload.Length < 12)
        {
            throw ProtocolError("A buffered tag payload is too short.");
        }

        int dataLength = payload[2];
        if (dataLength < 6 || payload.Length != dataLength + 6)
        {
            throw ProtocolError("A buffered tag payload does not match its declared data length.");
        }

        ReadOnlySpan<byte> data = payload.Slice(3, dataLength);
        ushort pc = ReadUInt16(data, 0);
        ReadOnlySpan<byte> identifier = data[2..^2];
        string recordCrc = Convert.ToHexString(data[^2..]);
        IdentifierParts parts = DecodeIdentifier(pc, identifier, fastTidEnabled, fastTidEpcLengthBytes, recordCrc);
        TagRead tag = CreateTag(
            pc,
            identifier,
            parts,
            payload[dataLength + 4],
            payload[dataLength + 3],
            timestamp,
            payload[dataLength + 5],
            TagReadSource.Buffer,
            frame.Payload);

        return new BufferedTagRecord(ReadUInt16(payload, 0), tag);
    }

    public static InventoryRound DecodeSummary(Frame frame)
    {
        ReadOnlySpan<byte> payload = frame.Payload;
        return frame.Command switch
        {
            0x89 when payload.Length == 7 => new InventoryRound(
                DecodeSummaryAntenna(payload[0]),
                ReadUInt16(payload, 1),
                ReadUInt32(payload, 3)),
            0x80 when payload.Length == 9 => new InventoryRound(
                DecodeSummaryAntenna(payload[0]),
                ReadUInt16(payload, 3),
                ReadUInt32(payload, 5),
                ReadUInt16(payload, 1)),
            0x89 => throw ProtocolError("A real-time inventory summary must contain exactly 7 payload bytes."),
            0x80 => throw ProtocolError("A buffered inventory summary must contain exactly 9 payload bytes."),
            _ => throw ProtocolError($"Command 0x{frame.Command:X2} does not contain an inventory summary."),
        };
    }

    private static IdentifierParts DecodeIdentifier(
        ushort pc,
        ReadOnlySpan<byte> identifier,
        bool fastTidEnabled,
        int? configuredEpcLength,
        string? fallbackCrc)
    {
        int pcLength = ((pc >> 11) & 0x1F) * 2;
        if (identifier.Length == 0 || identifier.Length != pcLength)
        {
            throw ProtocolError(
                $"PC declares {pcLength} identifier bytes, but the record contains {identifier.Length}.");
        }

        string identifierHex = Convert.ToHexString(identifier);
        if (!fastTidEnabled)
        {
            return new IdentifierParts(identifierHex, fallbackCrc, null, false);
        }

        if (configuredEpcLength is null)
        {
            return new IdentifierParts(null, fallbackCrc, null, true);
        }

        int epcLength = configuredEpcLength.Value;
        if (epcLength <= 0 || (epcLength & 1) != 0)
        {
            throw ProtocolError("The configured FastTID EPC length must be a positive even byte count.");
        }

        if (identifier.Length == epcLength)
        {
            return new IdentifierParts(identifierHex, fallbackCrc, null, false);
        }

        int tidLength = identifier.Length - epcLength - 2;
        if (tidLength <= 0 || (tidLength & 1) != 0)
        {
            throw ProtocolError("The FastTID identifier cannot be split into EPC, CRC, and a non-empty word-aligned TID.");
        }

        return new IdentifierParts(
            Convert.ToHexString(identifier[..epcLength]),
            Convert.ToHexString(identifier.Slice(epcLength, 2)),
            Convert.ToHexString(identifier[(epcLength + 2)..]),
            false);
    }

    private static TagRead CreateTag(
        ushort pc,
        ReadOnlySpan<byte> identifier,
        IdentifierParts parts,
        byte freqAnt,
        byte rssi,
        DateTimeOffset timestamp,
        long readCount,
        TagReadSource source,
        byte[] rawPayload) => new()
    {
        Epc = parts.Epc,
        IdentifierHex = Convert.ToHexString(identifier),
        Tid = parts.Tid,
        Crc = parts.Crc,
        FastTidAmbiguous = parts.Ambiguous,
        Pc = pc,
        Antenna = checked((byte)((freqAnt & 0x03) + 1)),
        FrequencyIndex = checked((byte)(freqAnt >> 2)),
        RssiRaw = rssi,
        Timestamp = timestamp,
        FirstSeen = timestamp,
        LastSeen = timestamp,
        ReadCount = readCount,
        Source = source,
        RawPayloadHex = Convert.ToHexString(rawPayload),
    };

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        checked((ushort)((bytes[offset] << 8) | bytes[offset + 1]));

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        ((uint)bytes[offset] << 24) |
        ((uint)bytes[offset + 1] << 16) |
        ((uint)bytes[offset + 2] << 8) |
        bytes[offset + 3];

    private static byte DecodeSummaryAntenna(byte antenna)
    {
        if (antenna > 3)
        {
            throw ProtocolError($"Inventory summary antenna index {antenna} is outside the supported range 0 through 3.");
        }

        return checked((byte)(antenna + 1));
    }

    private static ReaderProtocolException ProtocolError(string message) => new(message);

    private readonly record struct IdentifierParts(string? Epc, string? Crc, string? Tid, bool Ambiguous);
}
