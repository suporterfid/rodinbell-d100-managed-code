using Xunit;

namespace Rodinbell.D100.Tests;

public sealed class PortCatalogTests
{
    private const string GhostId = @"FTDIBUS\VID_0403+PID_6001+GHOST\0000";
    private const string PresentId = @"FTDIBUS\VID_0403+PID_6001+LIVE\0000";

    [Fact]
    public void Present_identity_wins_when_a_ghost_registration_reuses_the_same_COM_name()
    {
        var registryPorts = new[]
        {
            Ftdi("COM4", PresentId, "Live D100"),
            Ftdi("COM4", GhostId, "Removed D100")
        };

        var catalog = PortCatalog.Build(["COM4", "COM9"], registryPorts, [PresentId]);

        var com4 = Assert.Single(catalog, port => port.PortName == "COM4");
        Assert.Equal(PresentId, com4.DeviceId);
        Assert.Equal("Live D100", com4.FriendlyName);
        var unrelated = Assert.Single(catalog, port => port.PortName == "COM9");
        Assert.Null(unrelated.DeviceId);
        Assert.Null(unrelated.VendorId);
        Assert.Null(unrelated.ProductId);
    }

    [Fact]
    public void Multiple_present_identities_for_one_COM_name_leave_the_port_unclassified()
    {
        var registryPorts = new[]
        {
            Ftdi("COM4", PresentId, "First D100"),
            Ftdi("COM4", GhostId, "Second D100")
        };

        var port = Assert.Single(PortCatalog.Build(["COM4"], registryPorts, [PresentId, GhostId]));

        Assert.Equal("COM4", port.PortName);
        Assert.Null(port.DeviceId);
        Assert.Null(port.FriendlyName);
        Assert.Null(port.VendorId);
        Assert.Null(port.ProductId);
    }

    [Fact]
    public void Missing_presence_data_does_not_trust_a_retained_registry_identity()
    {
        var port = Assert.Single(PortCatalog.Build(["COM4"],
            [Ftdi("COM4", GhostId, "Removed D100")], []));

        Assert.Equal("COM4", port.PortName);
        Assert.Null(port.DeviceId);
        Assert.Null(port.VendorId);
        Assert.Null(port.ProductId);
    }

    private static PortDescriptor Ftdi(string portName, string deviceId, string friendlyName) =>
        new(portName, deviceId, friendlyName, 0x0403, 0x6001);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reconnection_cannot_open_a_reused_COM_under_its_ghost_identity(bool liveRegistrationAvailable)
    {
        var ghost = Ftdi("COM4", GhostId, "Removed D100");
        var registrations = new List<PortDescriptor>();
        if (liveRegistrationAvailable) registrations.Add(Ftdi("com4", PresentId, "Replacement reader"));
        registrations.Add(ghost);
        var device = new FakeReader();
        device.Ports.Clear();
        device.Ports.AddRange(PortCatalog.Build(["COM4"], registrations, [PresentId.ToLowerInvariant()]));

        var previous = new ReaderInfo(ghost, 115200, 1, 1, 9);
        Assert.Throws<ReaderDiscoveryException>(() => ReaderDiscovery.OpenPinned(device, previous, new ReaderOptions(), CancellationToken.None));
        Assert.Empty(device.OpenedPorts);
    }
}
