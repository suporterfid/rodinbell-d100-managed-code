using Xunit;

namespace Rodinbell.D100.Tests;

public sealed class StreamingTests
{
    private static ReaderOptions Options => new()
    {
        ProbeTimeout = TimeSpan.FromMilliseconds(100), CommandTimeout = TimeSpan.FromMilliseconds(200),
        RoundTimeout = TimeSpan.FromMilliseconds(200), ReconnectDelay = TimeSpan.FromMilliseconds(10), ReconnectAttempts = 2
    };

    [Fact]
    public async Task Repeated_round_failures_exhaust_recovery_even_if_handshakes_succeed()
    {
        var device = new FakeReader();
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        device.Intercept = (command, transport) =>
        {
            if (command == 0x72) transport.Reply(command, 1, 9);
            else if (command == 0x89) throw new IOException("Persistent inventory failure.");
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await using var stream = reader.ReadTagsAsync(cancellationToken: deadline.Token).GetAsyncEnumerator();
        await Assert.ThrowsAsync<IOException>(() => stream.MoveNextAsync().AsTask());
        Assert.Equal(Options.ReconnectAttempts, reader.ReconnectionCount);
        Assert.Equal(ReaderState.Disconnected, reader.State);
    }

    [Fact]
    public async Task Buffered_operations_are_rejected_until_live_inventory_stops()
    {
        var device = new FakeReader();
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        await using var stream = reader.ReadTagsAsync(new ReadingOptions { Interval = TimeSpan.FromMilliseconds(50) }).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.InventoryBufferedAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadBufferAsync());
        Assert.DoesNotContain(device.Requests, r => r[3] is 0x80 or 0x90);
        await reader.StopReadingAsync();
        Assert.Single(await reader.ReadBufferAsync());
    }

    [Fact]
    public async Task Slow_consumer_reports_overflow_and_observer_failure_does_not_stop_stream()
    {
        var device = new FakeReader();
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        reader.TagReadReceived += _ => throw new InvalidOperationException("Observer failure.");
        await using var stream = reader.ReadTagsAsync(new ReadingOptions { ChannelCapacity = 1 }).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.IsType<InvalidOperationException>(reader.LastObserverError);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (reader.DroppedTags == 0) await Task.Delay(5, deadline.Token);
        await reader.StopReadingAsync();
        Assert.True(reader.DroppedTags > 0);
        Assert.Equal(18, await reader.GetPowerAsync());
    }

    [Fact]
    public async Task Command_waits_for_current_round_and_iterator_disposal_stops_renewal()
    {
        var device = new FakeReader { BlockSummary = true };
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        var stream = reader.ReadTagsAsync(new ReadingOptions { Interval = TimeSpan.FromSeconds(1) }).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        var power = reader.GetPowerAsync();
        Assert.False(power.IsCompleted);
        Assert.DoesNotContain(device.Requests, r => r[3] == 0x77);
        device.SummaryReleased.Set();
        Assert.Equal(18, await power);
        await stream.DisposeAsync();
        Assert.Equal(1, device.Inventories);
        Assert.Equal(ReaderState.Connected, reader.State);
    }

    [Fact]
    public async Task Discovery_rejects_ambiguity_and_closes_all_handles()
    {
        var device = new FakeReader();
        device.Ports.Add(FakeReader.Unit with { PortName = "COM5", DeviceId = "unit-B" });
        await using var reader = new D100Reader(device, Options);
        await Assert.ThrowsAsync<ReaderDiscoveryException>(() => reader.ConnectAsync());
        Assert.Equal(0, device.ActiveTransports);
    }

    [Fact]
    public async Task Unknown_FastTid_mode_preserves_raw_identifier_without_claiming_EPC()
    {
        var device = new FakeReader();
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        await using var stream = reader.ReadTagsAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.Null(stream.Current.Epc);
        Assert.True(stream.Current.FastTidAmbiguous);
        Assert.Equal("E2801191A50300650518E799", stream.Current.IdentifierHex);
    }

    [Fact]
    public void Deduplication_accumulates_reads_and_evicts_least_recent_identity()
    {
        var tracker = new TagTracker(TimeSpan.FromSeconds(1), 2);
        var time = DateTimeOffset.UtcNow;
        TagRead Tag(string epc, int ms) => new() { Epc = epc, IdentifierHex = epc, RawPayloadHex = "", Timestamp = time.AddMilliseconds(ms) };
        Assert.Equal(1, tracker.Observe(Tag("AA", 0))!.ReadCount);
        Assert.Null(tracker.Observe(Tag("AA", 100)));
        var third = tracker.Observe(Tag("AA", 1000))!;
        Assert.Equal(3, third.ReadCount);
        Assert.Equal(time, third.FirstSeen);
        Assert.Equal(time.AddSeconds(1), third.LastSeen);
        tracker.Observe(Tag("BB", 1100));
        tracker.Observe(Tag("CC", 1200));
        Assert.Equal(2, tracker.Count);
        Assert.Equal(1, tracker.Observe(Tag("AA", 1300))!.ReadCount);
    }
}
