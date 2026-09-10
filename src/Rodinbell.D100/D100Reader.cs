using System.Runtime.Versioning;

namespace Rodinbell.D100;

/// <summary>Owns one reader connection. Commands are serialized with inventory rounds.</summary>
public sealed partial class D100Reader : IAsyncDisposable
{
    private readonly IReaderTransportFactory factory;
    private readonly ReaderOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource connectionStop = new();
    private ReaderSession? session;
    private ReaderInfo? info;
    private volatile ReaderState state;
    private volatile bool connectionRequested;
    private volatile bool disposed;
    private byte? desiredPower;
    private bool? desiredFastTid;
    private BuzzerMode? desiredBuzzer;
    private bool? fastTidEnabled;
    private long reconnectionCount;
    private Task? disposal;

    [SupportedOSPlatform("windows")]
    public D100Reader(ReaderOptions? options = null) : this(new WindowsSerialTransportFactory(), options) { }

    internal D100Reader(IReaderTransportFactory factory, ReaderOptions? options = null)
    {
        this.factory = factory;
        this.options = (options ?? new ReaderOptions()).Validated();
        fastTidEnabled = this.options.InitialFastTidEnabled;
    }

    public ReaderInfo? Info => Volatile.Read(ref info);
    public ReaderState State => state;
    public Exception? LastError { get; private set; }
    public long ReconnectionCount => Interlocked.Read(ref reconnectionCount);

    [SupportedOSPlatform("windows")]
    public static Task<IReadOnlyList<ReaderInfo>> DiscoverAsync(ReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        var settings = (options ?? new ReaderOptions()).Validated();
        return Task.Run(() => ReaderDiscovery.Scan(new WindowsSerialTransportFactory(), settings, cancellationToken), cancellationToken);
    }

    public async Task<ReaderInfo> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (connectionRequested)
            {
                using var recovery = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, connectionStop.Token);
                await EnsureConnectionAsync(recovery.Token).ConfigureAwait(false);
                return session!.Info;
            }
            connectionStop.Dispose();
            connectionStop = new CancellationTokenSource();
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, connectionStop.Token);
            connectionRequested = true;
            try
            {
                session = await Task.Run(() =>
                {
                    var found = ReaderDiscovery.Scan(factory, options, connect.Token);
                    if (found.Count == 0) throw new ReaderDiscoveryException("No compatible reader replied on the selected serial candidates.");
                    if (found.Count > 1) throw new ReaderDiscoveryException("Multiple compatible readers found. Select PortName or DeviceId explicitly.");
                    return OpenConfigured(() => ReaderDiscovery.Open(factory, found[0].Port, options, connect.Token), connect.Token);
                }, CancellationToken.None).ConfigureAwait(false);
                info = session.Info;
                fastTidEnabled = desiredFastTid ?? options.InitialFastTidEnabled;
                state = ReaderState.Connected;
                return info;
            }
            catch { connectionRequested = false; CloseSession(); throw; }
        }
        finally { gate.Release(); }
    }

    private async Task<T> RunAsync<T>(Func<ReaderSession, CancellationToken, T> command, CancellationToken token)
    {
        ThrowIfDisposed();
        if (!connectionRequested) throw new InvalidOperationException("Call ConnectAsync before issuing commands.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, connectionStop.Token, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await EnsureConnectionAsync(linked.Token).ConfigureAwait(false);
            try { return await Task.Run(() => command(session!, linked.Token), CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                LastError = ex;
                CloseSession();
                throw;
            }
        }
        finally { gate.Release(); }
    }

    private async Task EnsureConnectionAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!connectionRequested) throw new OperationCanceledException(token);
        if (session is not null) return;
        if (!options.AutoReconnect || info is null) throw new IOException("Reader disconnected; automatic reconnection is disabled.");
        var previous = info;
        state = ReaderState.Reconnecting;
        try
        {
            Exception? last = null;
            for (int attempt = 0; attempt < options.ReconnectAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (attempt > 0) await Task.Delay(options.ReconnectDelay, token).ConfigureAwait(false);
                try
                {
                    session = await Task.Run(() => OpenConfigured(() => ReaderDiscovery.OpenPinned(factory, previous, options, token), token), CancellationToken.None).ConfigureAwait(false);
                    info = session.Info;
                    fastTidEnabled = desiredFastTid ?? options.InitialFastTidEnabled;
                    Interlocked.Increment(ref reconnectionCount);
                    state = ReaderState.Connected;
                    return;
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException) { LastError = last = ex; }
            }
            throw new IOException($"Unable to reconnect the same reader after {options.ReconnectAttempts} attempts.", last);
        }
        finally
        {
            if (state == ReaderState.Reconnecting)
                state = disposed ? ReaderState.Disposed : ReaderState.Disconnected;
        }
    }

    private ReaderSession OpenConfigured(Func<ReaderSession> open, CancellationToken token)
    {
        var opened = open();
        try
        {
            if (desiredPower is byte power) opened.Acknowledge(0x66, [power], options.CommandTimeout, token);
            if (desiredFastTid is bool fast) opened.Acknowledge(0x8C, [fast ? (byte)0x8D : (byte)0], options.CommandTimeout, token);
            if (desiredBuzzer is BuzzerMode buzzer) opened.Acknowledge(0x7A, [(byte)buzzer], options.CommandTimeout, token);
            return opened;
        }
        catch { opened.Dispose(); throw; }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        connectionRequested = false;
        connectionStop.Cancel();
        await DisconnectCoreAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DisconnectCoreAsync()
    {
        await StopReadingCoreAsync().ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false);
        try { CloseSession(); }
        finally { gate.Release(); }
    }

    private void CloseSession()
    {
        var previous = session;
        session = null;
        try { previous?.Dispose(); }
        finally { if (!disposed) state = ReaderState.Disconnected; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public ValueTask DisposeAsync()
    {
        lock (streamSync) return new ValueTask(disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        disposed = true;
        connectionRequested = false;
        lifetime.Cancel();
        connectionStop.Cancel();
        await DisconnectCoreAsync().ConfigureAwait(false);
        state = ReaderState.Disposed;
        // Keep these synchronization objects valid for outstanding cancelled callers.
    }
}
