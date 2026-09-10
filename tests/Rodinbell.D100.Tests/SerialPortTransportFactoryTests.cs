using Xunit;

namespace Rodinbell.D100.Tests;

/// <summary>
/// The portable transport factory. Nothing here opens a port: the behaviour worth pinning is which
/// ports the factory will offer, because that is what decides whether a deployed instance probes the
/// device it was configured with or every serial device on the machine.
/// </summary>
public sealed class SerialPortTransportFactoryTests
{
    [Fact]
    public void Named_ports_are_offered_in_the_order_they_were_given()
    {
        var factory = new SerialPortTransportFactory("COM4", "/dev/ttyUSB0");

        Assert.Equal(["COM4", "/dev/ttyUSB0"], factory.GetPorts().Select(p => p.PortName));
    }

    [Fact]
    public void A_named_port_carries_no_device_identity()
    {
        // Identity pinning is what the Windows transport buys with System.Management. The portable
        // one does not have it, and must not imply otherwise - a null DeviceId is the honest answer,
        // not a fabricated one.
        var port = Assert.Single(new SerialPortTransportFactory("COM4").GetPorts());

        Assert.Equal("COM4", port.PortName);
        Assert.Null(port.DeviceId);
        Assert.Null(port.VendorId);
        Assert.Null(port.ProductId);
    }

    [Fact]
    public void Naming_no_ports_offers_none_rather_than_falling_back_to_every_port()
    {
        // An explicit empty list is a caller asking for nothing. Falling back to GetPortNames here
        // would turn "probe nothing" into "open every serial device on the host".
        Assert.Empty(new SerialPortTransportFactory([]).GetPorts());
    }

    [Fact]
    public void The_factory_rejects_a_null_port_list_and_a_null_port()
    {
        Assert.Throws<ArgumentNullException>(() => new SerialPortTransportFactory(null!));
        Assert.Throws<ArgumentNullException>(() => new SerialPortTransportFactory("COM4").Open(null!, 115200));
    }

    [Fact]
    public void The_read_timeout_is_bounded_by_default()
    {
        // A blocking read with no timeout hangs the reader loop on a silent device rather than
        // letting it poll again.
        Assert.InRange(new SerialPortTransportFactory().ReadTimeoutMilliseconds, 1, 5000);
    }
}
