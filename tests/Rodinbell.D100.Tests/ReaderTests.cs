using Xunit;

namespace Rodinbell.D100.Tests;

public sealed class ReaderTests
{
    private static ReaderOptions Options => new()
    {
        InitialFastTidEnabled = false,
        ProbeTimeout = TimeSpan.FromMilliseconds(100), CommandTimeout = TimeSpan.FromMilliseconds(200),
        RoundTimeout = TimeSpan.FromMilliseconds(300), ReconnectDelay = TimeSpan.FromMilliseconds(10), ReconnectAttempts = 2
    };

    [Fact]
    public async Task Connect_discovers_and_closes_probe_handles()
    {
        var device = new FakeReader();
        await using var reader = new D100Reader(device, Options);
        var info = await reader.ConnectAsync();
        Assert.Equal("COM4", info.Port.PortName);
        Assert.Equal("1.9", info.FirmwareVersion);
        Assert.Equal(1, info.Address);
        Assert.Equal(1, device.ActiveTransports);
        await reader.DisconnectAsync();
        Assert.Equal(0, device.ActiveTransports);
    }

    [Fact]
    public async Task Controls_encode_commands_and_parse_ack_data_and_temperature_sign()
    {
        var device = new FakeReader { TemperatureSign = 0, Temperature = 7 };
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        await reader.SetPowerAsync(20);
        Assert.Equal(20, await reader.GetPowerAsync());
        await reader.SetFastTidAsync(true);
        Assert.True(await reader.GetFastTidAsync());
        Assert.DoesNotContain(device.Requests, r => r[3] == 0x8D);
        await reader.SetBuzzerAsync(BuzzerMode.Silent);
        Assert.Equal(BuzzerMode.Silent, device.Buzzer);
        Assert.Equal(-7, await reader.GetTemperatureAsync());
        Assert.All(device.Requests, r => Assert.Equal(0, r.Sum(b => b) & 255));
    }

    [Fact]
    public async Task Buffer_collection_finishes_by_tag_count_and_can_clear()
    {
        var device = new FakeReader();
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        Assert.Equal((ushort)1, (await reader.InventoryBufferedAsync()).BufferedTagCount);
        var tags = await reader.ReadBufferAsync(clearAfterRead: true);
        Assert.Equal("E2801191A50300650518E799", Assert.Single(tags).Epc);
        Assert.Equal(3, tags[0].ReadCount);
        Assert.Equal((ushort)0, await reader.GetBufferCountAsync());
        Assert.Empty(await reader.ReadBufferAsync());
        await reader.ClearBufferAsync();
    }

    [Fact]
    public async Task Reader_status_is_preserved_without_reconnection()
    {
        var device = new FakeReader { RejectCommand = 0x8E };
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        var error = await Assert.ThrowsAsync<ReaderCommandException>(() => reader.GetFastTidAsync());
        Assert.Equal(0x41, error.Status);
        Assert.Equal(18, await reader.GetPowerAsync());
        Assert.Equal(0, reader.ReconnectionCount);
    }

    [Fact]
    public async Task Tag_is_delivered_before_round_ends_and_stop_drains_the_summary()
    {
        var device = new FakeReader { BlockSummary = true };
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        await using var tags = reader.ReadTagsAsync().GetAsyncEnumerator();
        Assert.True(await tags.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("E2801191A50300650518E799", tags.Current.Epc);
        var stop = reader.StopReadingAsync();
        await Task.Delay(20);
        Assert.False(stop.IsCompleted);
        device.SummaryReleased.Set();
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, device.Inventories);
        Assert.Equal(18, await reader.GetPowerAsync());
    }

    [Fact]
    public async Task Reconnection_follows_device_identity_when_COM_changes()
    {
        var device = new FakeReader { DisconnectNextRound = true };
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        await reader.SetPowerAsync(20);
        await using var tags = reader.ReadTagsAsync().GetAsyncEnumerator();
        Assert.True(await tags.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        await reader.StopReadingAsync();
        Assert.Equal("COM7", reader.Info!.Port.PortName);
        Assert.Equal(1, reader.ReconnectionCount);
        Assert.Equal(20, await reader.GetPowerAsync());
    }

    [Fact]
    public async Task Reconnection_never_opens_a_different_device()
    {
        var device = new FakeReader { DisconnectNextRound = true, ChangeIdentityOnFailure = true };
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        await using var tags = reader.ReadTagsAsync().GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<IOException>(() => tags.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.DoesNotContain("COM7", device.OpenedPorts);
    }

    [Fact]
    public async Task Command_cancellation_invalidates_session_and_does_not_replay_command()
    {
        var device = new FakeReader();
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        device.Intercept = (_, _) => { };
        using var timeout = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.GetTemperatureAsync(timeout.Token));
        Assert.Equal(0, device.ActiveTransports);
        Assert.Single(device.Requests, r => r[3] == 0x7B);
        device.Intercept = null;
        Assert.Equal(18, await reader.GetPowerAsync());
    }
}
