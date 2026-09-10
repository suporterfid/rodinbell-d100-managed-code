using Rodinbell.D100.Testing;
using Xunit;

namespace Rodinbell.D100.Tests;

/// <summary>
/// Reconnecting when the transport reports no USB device identity - the portable serial case, where
/// the caller named a port instead of having one discovered.
/// </summary>
public sealed class PortNameReconnectTests
{
    /// <summary>
    /// A device whose ports carry no identity, like SerialPortTransportFactory's. Wraps a FakeReader
    /// rather than deriving from it, which is sealed, and strips the identity on the way out.
    /// </summary>
    private sealed class AnonymousPortDevice(FakeReader inner) : IReaderTransportFactory
    {
        public IReadOnlyList<PortDescriptor> GetPorts() =>
            [.. inner.GetPorts().Select(static p => new PortDescriptor(p.PortName))];

        public IReaderTransport Open(PortDescriptor port, int baudRate) => inner.Open(port, baudRate);
    }

    [Fact]
    public void AReaderOnANamedPortReconnectsByThatName()
    {
        // Before this was allowed, AutoReconnect was unusable for any portable host: the first
        // transient became a permanent failure, reported as needing an identity the caller never had.
        var device = new FakeReader();
        IReaderTransportFactory factory = new AnonymousPortDevice(device);

        var previous = new ReaderInfo(new PortDescriptor(FakeReader.Unit.PortName), 115200, 1, 1, 9);

        using var session = ReaderDiscovery.OpenPinned(factory, previous, new ReaderOptions().Validated(), CancellationToken.None);

        Assert.Equal(1, device.ActiveTransports);
    }

    [Fact]
    public void APortThatIsNoLongerPresentIsReportedByName()
    {
        IReaderTransportFactory factory = new AnonymousPortDevice(new FakeReader());

        var previous = new ReaderInfo(new PortDescriptor("COM99"), 115200, 1, 1, 9);

        var error = Assert.Throws<ReaderDiscoveryException>(() =>
            ReaderDiscovery.OpenPinned(factory, previous, new ReaderOptions().Validated(), CancellationToken.None));

        // Names the port, because that is the only identity this caller ever had.
        Assert.Contains("COM99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReaderWithAnIdentityIsStillPinnedToItRatherThanToItsPortName()
    {
        // The Windows path keeps the stronger guarantee: a reader whose COM number moved is found,
        // and a different device that took the old number is not mistaken for it.
        var device = new FakeReader();
        var previous = new ReaderInfo(FakeReader.Unit with { PortName = "COM7" }, 115200, 1, 1, 9);

        using var session = ReaderDiscovery.OpenPinned(device, previous, new ReaderOptions().Validated(), CancellationToken.None);

        Assert.Equal(FakeReader.Unit.PortName, device.OpenedPorts.Single());
    }
}
