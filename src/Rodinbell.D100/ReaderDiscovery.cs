namespace Rodinbell.D100;

internal static class ReaderDiscovery
{
    public static IReadOnlyList<ReaderInfo> Scan(IReaderTransportFactory factory, ReaderOptions options, CancellationToken token)
    {
        var readers = new List<ReaderInfo>();
        foreach (var port in factory.GetPorts().Where(p => Matches(p, options)))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var session = Open(factory, port, options, token);
                readers.Add(session.Info);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException) { }
        }
        return readers;
    }

    private static bool Matches(PortDescriptor port, ReaderOptions options)
    {
        if (options.DeviceId is not null && !string.Equals(port.DeviceId, options.DeviceId, StringComparison.OrdinalIgnoreCase)) return false;
        if (options.PortName is not null) return string.Equals(port.PortName, options.PortName, StringComparison.OrdinalIgnoreCase);
        return port.VendorId == options.VendorId && port.ProductId == options.ProductId;
    }

    public static ReaderSession Open(IReaderTransportFactory factory, PortDescriptor port, ReaderOptions options, CancellationToken token)
    {
        Exception? last = null;
        foreach (int baud in options.BaudRates)
        {
            token.ThrowIfCancellationRequested();
            ReaderSession? session = null;
            try
            {
                session = new ReaderSession(factory.Open(port, baud), new ReaderInfo(port, baud, 0xFF, 0, 0));
                session.Handshake(options.ProbeTimeout, token);
                return session;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            {
                session?.Dispose();
                last = ex;
            }
            catch { session?.Dispose(); throw; }
        }
        throw new IOException($"No valid firmware response from {port.PortName}.", last);
    }

    /// <summary>
    /// Reopens the reader that was previously connected, pinned to whatever identity is available.
    /// </summary>
    /// <remarks>
    /// A transport that reports a device identity is pinned to it, so a reader whose COM number moved
    /// is still found and a different device that took the old number is not mistaken for it.
    ///
    /// A transport that reports none - the portable serial one, which is given its port by name - is
    /// pinned to the port name instead, because that name is the identity the caller chose. Refusing
    /// here instead, as this once did, made AutoReconnect unusable for every portable host: the first
    /// transient became a permanent failure reported as "requires a stable USB device identity",
    /// which is true and unhelpful when the caller never had one to give.
    ///
    /// The weaker guarantee is real and belongs to the caller who named a port: if the operating
    /// system reassigns that name to a different device, this reconnects to the different device. A
    /// host that cannot accept that supplies a transport that reports identity, or turns
    /// <see cref="ReaderOptions.AutoReconnect"/> off.
    /// </remarks>
    public static ReaderSession OpenPinned(IReaderTransportFactory factory, ReaderInfo previous, ReaderOptions options, CancellationToken token)
    {
        var candidates = previous.Port.DeviceId is null
            ? factory.GetPorts()
                .Where(p => string.Equals(p.PortName, previous.Port.PortName, StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : factory.GetPorts()
                .Where(p => string.Equals(p.DeviceId, previous.Port.DeviceId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (candidates.Length != 1)
        {
            throw new ReaderDiscoveryException(previous.Port.DeviceId is null
                ? $"The port {previous.Port.PortName} is not present."
                : "The previously selected USB reader is not present or has an ambiguous identity.");
        }

        return Open(factory, candidates[0], options, token);
    }
}
