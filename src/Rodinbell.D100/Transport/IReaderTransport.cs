namespace Rodinbell.D100;

internal interface IReaderTransport : IDisposable
{
    // Returns zero on a bounded idle read. Throws IOException on disconnection.
    int Read(byte[] buffer);
    void Write(byte[] data);
}

internal interface IReaderTransportFactory
{
    IReadOnlyList<PortDescriptor> GetPorts();
    IReaderTransport Open(PortDescriptor port, int baudRate);
}
