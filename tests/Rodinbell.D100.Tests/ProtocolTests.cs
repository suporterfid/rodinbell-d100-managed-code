using Rodinbell.D100;
using Xunit;

namespace Rodinbell.D100.Tests;

public sealed class ProtocolTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 8, 20, 49, 14, TimeSpan.FromHours(-3));

    [Fact]
    public void EncodeMatchesCapturedFirmwareRequest()
    {
        Assert.Equal(new byte[] { 0xA0, 0x03, 0xFF, 0x72, 0xEC }, FrameCodec.Encode(0xFF, 0x72, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameCodec.Encode(1, 2, new byte[253]));
    }

    [Fact]
    public void EncodeAllowsMaximumPayloadAndProducesZeroSumFrame()
    {
        byte[] encoded = FrameCodec.Encode(0xFE, 0x90, new byte[252]);

        Assert.Equal(257, encoded.Length);
        Assert.Equal((byte)0xFF, encoded[1]);
        Assert.Equal(0, encoded.Aggregate(0, (sum, value) => (sum + value) & 0xFF));
    }

    [Fact]
    public void DecoderReadsCapturedResponseOneByteAtATime()
    {
        var decoder = new FrameDecoder();
        byte[] bytes = [0xA0, 0x05, 0x01, 0x72, 0x01, 0x09, 0xDE];

        foreach (byte value in bytes)
        {
            decoder.Append([value]);
        }

        Assert.True(decoder.TryRead(out Frame frame));
        Assert.Equal(0x01, frame.Address);
        Assert.Equal(0x72, frame.Command);
        Assert.Equal(new byte[] { 0x01, 0x09 }, frame.Payload);
        Assert.False(decoder.TryRead(out _));
    }

    [Fact]
    public void DecoderReadsConcatenatedFrames()
    {
        var decoder = new FrameDecoder();
        byte[] first = FrameCodec.Encode(1, 0x72, [1, 9]);
        byte[] second = FrameCodec.Encode(2, 0x77, [18]);
        decoder.Append([.. first, .. second]);

        Assert.True(decoder.TryRead(out Frame a));
        Assert.True(decoder.TryRead(out Frame b));
        Assert.Equal(0x72, a.Command);
        Assert.Equal(0x77, b.Command);
    }

    [Fact]
    public void DecoderDoesNotTreatPayloadBytesAsAFrameWhileOuterFrameIsFragmented()
    {
        var decoder = new FrameDecoder();
        byte[] embeddedFrame = FrameCodec.Encode(1, 0x77, []);
        byte[] outerFrame = FrameCodec.Encode(1, 0x90, embeddedFrame);

        decoder.Append(outerFrame.AsSpan(0, outerFrame.Length - 1));
        Assert.False(decoder.TryRead(out _));

        decoder.Append(outerFrame.AsSpan(outerFrame.Length - 1));
        Assert.True(decoder.TryRead(out Frame frame));
        Assert.Equal(0x90, frame.Command);
        Assert.Equal(embeddedFrame, frame.Payload);
    }

    [Fact]
    public void DecoderRecoversAfterMalformedLengthAndBadChecksum()
    {
        var decoder = new FrameDecoder();
        byte[] bad = FrameCodec.Encode(1, 0x77, [18]);
        bad[^1]++;
        byte[] good = FrameCodec.Encode(1, 0x72, [1, 9]);
        decoder.Append([0x55, 0xA0, 0x02, .. bad, .. good]);

        Assert.True(decoder.TryRead(out Frame frame));
        Assert.Equal(0x72, frame.Command);
        Assert.Equal(new byte[] { 1, 9 }, frame.Payload);
        Assert.False(decoder.TryRead(out _));
    }

    [Fact]
    public void DecoderBoundsGarbageAndResetClearsState()
    {
        var decoder = new FrameDecoder();
        decoder.Append(new byte[100_000]);
        decoder.Append(FrameCodec.Encode(1, 0x72, [1, 9]));
        Assert.True(decoder.TryRead(out _));

        decoder.Append([0xA0, 0x05]);
        decoder.Reset();
        decoder.Append(FrameCodec.Encode(1, 0x77, [18]));
        Assert.True(decoder.TryRead(out Frame frame));
        Assert.Equal(0x77, frame.Command);
    }

    [Fact]
    public void RealtimeDecodesCapturedTagWithRichObservation()
    {
        var frame = new Frame(1, 0x89,
            [0x08, 0x30, 0x00, 0xE2, 0x80, 0x11, 0x91, 0xA5, 0x03, 0x00, 0x65, 0x05, 0x18, 0xE7, 0x99, 0x63]);

        TagRead tag = TagDecoder.DecodeRealtime(frame, Timestamp);

        Assert.Equal("E2801191A50300650518E799", tag.Epc);
        Assert.Equal(tag.Epc, tag.IdentifierHex);
        Assert.Equal((ushort)0x3000, tag.Pc);
        Assert.Equal((byte)1, tag.Antenna);
        Assert.Equal((byte)2, tag.FrequencyIndex);
        Assert.Equal((byte)0x63, tag.RssiRaw);
        Assert.Equal(Timestamp, tag.Timestamp);
        Assert.Equal(Timestamp, tag.FirstSeen);
        Assert.Equal(Timestamp, tag.LastSeen);
        Assert.Equal(1, tag.ReadCount);
        Assert.Equal(TagReadSource.RealTime, tag.Source);
        Assert.Equal("083000E2801191A50300650518E79963", tag.RawPayloadHex);
    }

    [Fact]
    public void RealtimeAcceptsShortPcConsistentEpcAndRejectsMalformedRecords()
    {
        TagRead tag = TagDecoder.DecodeRealtime(new Frame(1, 0x89, [0x03, 0x08, 0x00, 0x12, 0x34, 0x55]), Timestamp);
        Assert.Equal("1234", tag.Epc);
        Assert.Equal((byte)4, tag.Antenna);

        Assert.Throws<ReaderProtocolException>(() =>
            TagDecoder.DecodeRealtime(new Frame(1, 0x89, [0, 0x30, 0, 0x12, 0x34, 0x55]), Timestamp));
        Assert.Throws<ReaderProtocolException>(() =>
            TagDecoder.DecodeRealtime(new Frame(1, 0x89, [0, 0, 0]), Timestamp));
    }

    [Fact]
    public void FastTidWithoutConfiguredEpcLengthIsKeptAmbiguous()
    {
        byte[] identifier = [
            0xE2, 0x80, 0x11, 0x91, 0xA5, 0x03, 0x00, 0x65, 0x05, 0x18, 0xE7, 0x99,
            0xAA, 0xBB, 0xE2, 0x00, 0x34, 0x12, 0x56, 0x78];
        TagRead tag = TagDecoder.DecodeRealtime(
            new Frame(1, 0x89, [0, 0x50, 0, .. identifier, 0x60]), Timestamp, fastTidEnabled: true);

        Assert.Null(tag.Epc);
        Assert.Null(tag.Crc);
        Assert.Null(tag.Tid);
        Assert.True(tag.FastTidAmbiguous);
        Assert.Equal(Convert.ToHexString(identifier), tag.IdentifierHex);
    }

    [Fact]
    public void FastTidWithConfiguredEpcLengthSplitsExtendedIdentifier()
    {
        byte[] epc = [0xE2, 0x80, 0x11, 0x91, 0xA5, 0x03, 0x00, 0x65, 0x05, 0x18, 0xE7, 0x99];
        byte[] crc = [0xAA, 0xBB];
        byte[] tid = [0xE2, 0x00, 0x34, 0x12, 0x56, 0x78];
        TagRead tag = TagDecoder.DecodeRealtime(
            new Frame(1, 0x89, [0, 0x50, 0, .. epc, .. crc, .. tid, 0x60]), Timestamp, true, epc.Length);

        Assert.Equal(Convert.ToHexString(epc), tag.Epc);
        Assert.Equal("AABB", tag.Crc);
        Assert.Equal(Convert.ToHexString(tid), tag.Tid);
        Assert.False(tag.FastTidAmbiguous);
    }

    [Fact]
    public void FastTidConfiguredPreservesOrdinaryFallback()
    {
        byte[] epc = [0xE2, 0x80, 0x11, 0x91, 0xA5, 0x03, 0x00, 0x65, 0x05, 0x18, 0xE7, 0x99];
        TagRead tag = TagDecoder.DecodeRealtime(
            new Frame(1, 0x89, [0, 0x30, 0, .. epc, 0x60]), Timestamp, true, epc.Length);

        Assert.Equal(Convert.ToHexString(epc), tag.Epc);
        Assert.Null(tag.Crc);
        Assert.Null(tag.Tid);
        Assert.False(tag.FastTidAmbiguous);
    }

    [Fact]
    public void FastTidRejectsImpossibleConfiguredSplitAndInvalidPc()
    {
        Assert.Throws<ReaderProtocolException>(() => TagDecoder.DecodeRealtime(
            new Frame(1, 0x89, [0, 0x40, 0, 1, 2, 3, 4, 5, 6, 7, 8, 0x60]), Timestamp, true, 4));
        Assert.Throws<ReaderProtocolException>(() => TagDecoder.DecodeRealtime(
            new Frame(1, 0x89, [0, 0x30, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0x60]), Timestamp, true, 4));
    }

    [Fact]
    public void BufferedRecordDecodesLengthsCrcAndObservationFields()
    {
        byte[] epc = [0xE2, 0x80, 0x11, 0x91, 0xA5, 0x03, 0x00, 0x65, 0x05, 0x18, 0xE7, 0x99];
        var frame = new Frame(1, 0x90,
            [0, 2, 16, 0x30, 0, .. epc, 0xAA, 0xBB, 0x63, 0x09, 3]);

        BufferedTagRecord record = TagDecoder.DecodeBuffered(frame, Timestamp);

        Assert.Equal((ushort)2, record.TotalTags);
        Assert.Equal(Convert.ToHexString(epc), record.Tag.Epc);
        Assert.Equal("AABB", record.Tag.Crc);
        Assert.Equal((byte)2, record.Tag.Antenna);
        Assert.Equal((byte)2, record.Tag.FrequencyIndex);
        Assert.Equal((byte)0x63, record.Tag.RssiRaw);
        Assert.Equal(3, record.Tag.ReadCount);
        Assert.Equal(TagReadSource.Buffer, record.Tag.Source);
        Assert.Equal(Convert.ToHexString(frame.Payload), record.Tag.RawPayloadHex);
    }

    [Fact]
    public void BufferedRecordRequiresExactDeclaredLengthAndPcConsistency()
    {
        Assert.Throws<ReaderProtocolException>(() => TagDecoder.DecodeBuffered(
            new Frame(1, 0x90, [0, 1, 4, 0, 0, 0, 0, 1, 2, 3, 4]), Timestamp));
        Assert.Throws<ReaderProtocolException>(() => TagDecoder.DecodeBuffered(
            new Frame(1, 0x90, [0, 1, 6, 0x30, 0, 1, 2, 0, 0, 0x60, 0, 1]), Timestamp));
    }

    [Fact]
    public void BufferedFastTidAmbiguityRetainsTheRecordCrc()
    {
        byte[] identifier = [1, 2, 3, 4, 5, 6, 0xAA, 0xBB];
        BufferedTagRecord record = TagDecoder.DecodeBuffered(
            new Frame(1, 0x91, [0, 1, 12, 0x20, 0, .. identifier, 0xCC, 0xDD, 0x60, 0, 1]),
            Timestamp,
            fastTidEnabled: true);

        Assert.Null(record.Tag.Epc);
        Assert.True(record.Tag.FastTidAmbiguous);
        Assert.Equal("CCDD", record.Tag.Crc);
    }

    [Theory]
    [InlineData(0x89, new byte[] { 0, 0, 17, 0, 0, 0, 1 }, 1, 17, 1, -1)]
    [InlineData(0x80, new byte[] { 1, 0, 2, 0, 34, 0, 0, 1, 2 }, 2, 34, 258, 2)]
    public void SummaryDecodesExactCommandSpecificLayout(
        byte command, byte[] payload, byte antenna, ushort rate, uint total, int buffered)
    {
        InventoryRound round = TagDecoder.DecodeSummary(new Frame(1, command, payload));

        Assert.Equal(antenna, round.Antenna);
        Assert.Equal(rate, round.ReadRate);
        Assert.Equal(total, round.TotalRead);
        Assert.Equal(buffered < 0 ? null : checked((ushort)buffered), round.BufferedTagCount);
    }

    [Fact]
    public void SummaryRejectsWrongLengthsAndCommands()
    {
        Assert.Throws<ReaderProtocolException>(() => TagDecoder.DecodeSummary(new Frame(1, 0x89, new byte[9])));
        Assert.Throws<ReaderProtocolException>(() => TagDecoder.DecodeSummary(new Frame(1, 0x80, new byte[7])));
        Assert.Throws<ReaderProtocolException>(() => TagDecoder.DecodeSummary(new Frame(1, 0x72, new byte[7])));
        Assert.Throws<ReaderProtocolException>(() => TagDecoder.DecodeSummary(
            new Frame(1, 0x89, [4, 0, 1, 0, 0, 0, 1])));
    }
}
