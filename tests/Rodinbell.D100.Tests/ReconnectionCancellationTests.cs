using Xunit;

namespace Rodinbell.D100.Tests;

public sealed class ReconnectionCancellationTests
{
    private static ReaderOptions Options => new()
    {
        InitialFastTidEnabled = false,
        ProbeTimeout = TimeSpan.FromMilliseconds(100),
        CommandTimeout = TimeSpan.FromMilliseconds(40),
        RoundTimeout = TimeSpan.FromMilliseconds(300),
        ReconnectDelay = TimeSpan.FromMilliseconds(400),
        ReconnectAttempts = 3
    };

    [Fact]
    public async Task Disconnect_cancels_ConnectAsync_recovery_before_another_transport_opens()
    {
        var device = new FakeReader();
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        await InvalidateSessionAsync(reader, device);
        int opensBeforeRecovery = device.OpenedPorts.Count;
        lock (device.Ports) device.Ports.Clear();

        var recovery = reader.ConnectAsync();
        await WaitForRetryDelayAsync(reader);
        var disconnect = reader.DisconnectAsync();
        lock (device.Ports) device.Ports.Add(FakeReader.Unit);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => recovery.WaitAsync(TimeSpan.FromSeconds(1)));
        await disconnect.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(opensBeforeRecovery, device.OpenedPorts.Count);
        Assert.Equal(0, device.ActiveTransports);
        Assert.Equal(ReaderState.Disconnected, reader.State);
    }

    [Fact]
    public async Task Cancelling_command_during_reconnect_delay_restores_disconnected_state()
    {
        var device = new FakeReader();
        await using var reader = new D100Reader(device, Options);
        await reader.ConnectAsync();
        await InvalidateSessionAsync(reader, device);
        lock (device.Ports) device.Ports.Clear();
        using var cancellation = new CancellationTokenSource();

        var command = reader.GetPowerAsync(cancellation.Token);
        await WaitForRetryDelayAsync(reader);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
        Assert.Equal(0, device.ActiveTransports);
        Assert.Equal(ReaderState.Disconnected, reader.State);
    }

    private static async Task InvalidateSessionAsync(D100Reader reader, FakeReader device)
    {
        device.CorruptNextReply = true;
        await Assert.ThrowsAsync<TimeoutException>(() => reader.GetPowerAsync());
        Assert.Equal(0, device.ActiveTransports);
    }

    private static async Task WaitForRetryDelayAsync(D100Reader reader)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (reader.LastError is not ReaderDiscoveryException)
            await Task.Delay(5, deadline.Token);
        Assert.Equal(ReaderState.Reconnecting, reader.State);
    }
}
