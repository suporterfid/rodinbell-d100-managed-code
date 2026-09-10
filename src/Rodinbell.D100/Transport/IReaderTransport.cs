namespace Rodinbell.D100;

/// <summary>
/// A byte pipe to the reader. Public so a host can supply its own - the library ships a Windows
/// identity-pinned transport and a portable serial one, and neither suits every deployment.
/// </summary>
public interface IReaderTransport : IDisposable
{
    // Returns zero on a bounded idle read. Throws IOException on disconnection.
    int Read(byte[] buffer);
    void Write(byte[] data);
}

/// <summary>Enumerates candidate ports and opens one. See <see cref="IReaderTransport"/>.</summary>
public interface IReaderTransportFactory
{
    IReadOnlyList<PortDescriptor> GetPorts();
    IReaderTransport Open(PortDescriptor port, int baudRate);
}
