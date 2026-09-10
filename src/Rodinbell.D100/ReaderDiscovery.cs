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

    public static ReaderSession OpenPinned(IReaderTransportFactory factory, ReaderInfo previous, ReaderOptions options, CancellationToken token)
    {
        if (previous.Port.DeviceId is null)
            throw new ReaderDiscoveryException("Automatic reconnection requires a stable USB device identity; reconnect explicitly for this port.");
        var candidates = factory.GetPorts().Where(p => string.Equals(p.DeviceId, previous.Port.DeviceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) throw new ReaderDiscoveryException("The previously selected USB reader is not present or has an ambiguous identity.");
        return Open(factory, candidates[0], options, token);
    }
}
