using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Rodinbell.D100;

public sealed partial class D100Reader
{
    private readonly object streamSync = new();
    private CancellationTokenSource? streamStop;
    private Task? streamTask;
    private long droppedTags;
    private long suppressedTags;
    public long DroppedTags => Interlocked.Read(ref droppedTags);
    public long SuppressedDuplicates => Interlocked.Read(ref suppressedTags);
    public InventoryRound? LastRound { get; private set; }
    public Exception? LastObserverError { get; private set; }
    /// <summary>Delivered by the async iterator, outside the serial transaction. Subscriber failures are recorded.</summary>
    public event Action<TagRead>? TagReadReceived;

    public async IAsyncEnumerable<TagRead> ReadTagsAsync(ReadingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!connectionRequested) throw new InvalidOperationException("Call ConnectAsync before reading tags.");
        var settings = options ?? new ReadingOptions();
        settings.Validate();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionStop.Token, lifetime.Token);
        var channel = Channel.CreateBounded<TagRead>(new BoundedChannelOptions(settings.ChannelCapacity)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        Task pump;
        lock (streamSync)
        {
            if (streamTask is not null) throw new InvalidOperationException("Only one tag stream can be enumerated at a time.");
            streamStop = stop;
            streamTask = pump = PumpAsync(channel, settings, stop.Token);
        }
        try
        {
            await foreach (var tag in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (stop.IsCancellationRequested) break;
                foreach (var subscriber in TagReadReceived?.GetInvocationList() ?? [])
                {
                    try { ((Action<TagRead>)subscriber)(tag); }
                    catch (Exception ex) { LastObserverError = ex; }
                }
                yield return tag;
            }
        }
        finally
        {
            stop.Cancel();
            await pump.ConfigureAwait(false);
            lock (streamSync)
            {
                if (ReferenceEquals(streamStop, stop)) { streamStop = null; streamTask = null; }
            }
        }
    }

    public async Task StopReadingAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await StopReadingCoreAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task StopReadingCoreAsync()
    {
        Task? task;
        lock (streamSync) { streamStop?.Cancel(); task = streamTask; }
        if (task is not null) await task.ConfigureAwait(false);
    }

    private async Task PumpAsync(Channel<TagRead> channel, ReadingOptions reading, CancellationToken token)
    {
        Exception? failure = null;
        int consecutiveRoundFailures = 0;
        var tracker = new TagTracker(reading.DeduplicationWindow, reading.MaxTrackedTags);
        try
        {
            while (!token.IsCancellationRequested)
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await EnsureConnectionAsync(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    state = ReaderState.Reading;
                    try
                    {
                        await Task.Run(() => session!.Exchange(0x89, [reading.Repeat], options.RoundTimeout,
                            CancellationToken.None, frame =>
                            {
                                if (frame.Payload.Length == 1)
                                {
                                    if (frame.Payload[0] != 0x36) throw new ReaderCommandException(frame.Command, frame.Payload[0]);
                                    LastRound = new InventoryRound(1, 0, 0);
                                    return true;
                                }
                                if (frame.Payload.Length == 7) { LastRound = TagDecoder.DecodeSummary(frame); return true; }
                                var raw = TagDecoder.DecodeRealtime(frame, DateTimeOffset.UtcNow, fastTidEnabled != false, options.FastTidEpcLengthBytes)
                                    with { PortName = session.Info.Port.PortName };
                                var observation = tracker.Observe(raw);
                                if (observation is null) Interlocked.Increment(ref suppressedTags);
                                else if (!token.IsCancellationRequested && !channel.Writer.TryWrite(observation)) Interlocked.Increment(ref droppedTags);
                                return false;
                            }), CancellationToken.None).ConfigureAwait(false);
                        consecutiveRoundFailures = 0;
                    }
                    catch (Exception ex) when (ex is IOException or TimeoutException)
                    {
                        LastError = ex;
                        CloseSession();
                        if (!options.AutoReconnect || ++consecutiveRoundFailures > options.ReconnectAttempts) throw;
                    }
                }
                finally { gate.Release(); }
                if (reading.Interval > TimeSpan.Zero) await Task.Delay(reading.Interval, token).ConfigureAwait(false);
                else await Task.Yield();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { LastError = failure = ex; }
        finally
        {
            if (!disposed) state = session is null ? ReaderState.Disconnected : ReaderState.Connected;
            channel.Writer.TryComplete(failure);
        }
    }

    private void RequireStoppedInventory()
    {
        lock (streamSync)
            if (streamTask is { IsCompleted: false })
                throw new InvalidOperationException("Stop live inventory before accessing the device buffer.");
    }
}
