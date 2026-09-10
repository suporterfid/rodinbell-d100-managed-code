using System.IO.Ports;

namespace Rodinbell.D100;

/// <summary>
/// A serial transport built on <see cref="SerialPort"/> alone, with the port named rather than
/// discovered.
/// </summary>
/// <remarks>
/// <para>
/// The Windows transport pins a reader to its FTDI device identity through the registry and WMI, so
/// it survives the port number moving between reconnects. That costs <c>System.Management</c>, which
/// is Windows-only and does not survive trimming - which rules it out for a host that publishes
/// Native AOT for Linux.
/// </para>
/// <para>
/// This is the trade in the other direction: no identity pinning, no discovery beyond what
/// <see cref="SerialPort.GetPortNames"/> reports, and it works anywhere <c>System.IO.Ports</c> does -
/// <c>COM4</c> on Windows, <c>/dev/ttyUSB0</c> on Linux. A host that already knows which device it is
/// driving loses nothing by naming it.
/// </para>
/// </remarks>
public sealed class SerialPortTransportFactory : IReaderTransportFactory
{
    private readonly string[] _portNames;

    /// <summary>Offers every serial port the platform reports.</summary>
    public SerialPortTransportFactory() : this(SerialPort.GetPortNames())
    {
    }

    /// <summary>
    /// Offers only the named ports. Preferred on a deployed instance: the reader is configured, so
    /// probing every serial port on the machine would open devices that belong to something else.
    /// </summary>
    public SerialPortTransportFactory(params string[] portNames)
    {
        ArgumentNullException.ThrowIfNull(portNames);

        _portNames = [.. portNames];
    }

    /// <summary>Read timeout for a single blocking read. Bounded so a silent reader does not hang a caller.</summary>
    public int ReadTimeoutMilliseconds { get; init; } = 200;

    public IReadOnlyList<PortDescriptor> GetPorts() =>
        [.. _portNames.Select(static name => new PortDescriptor(name))];

    public IReaderTransport Open(PortDescriptor port, int baudRate)
    {
        ArgumentNullException.ThrowIfNull(port);

        return new SerialPortTransport(port.PortName, baudRate, ReadTimeoutMilliseconds);
    }
}

/// <summary>One open serial port. See <see cref="SerialPortTransportFactory"/>.</summary>
internal sealed class SerialPortTransport : IReaderTransport
{
    private readonly SerialPort _port;

    public SerialPortTransport(string portName, int baudRate, int readTimeoutMilliseconds)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, dataBits: 8, StopBits.One)
        {
            ReadTimeout = readTimeoutMilliseconds,
            WriteTimeout = 1000,

            // The reader does not use flow control, and leaving these unset has been known to keep
            // an FTDI bridge from asserting the lines a device waits on before it will talk.
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true,
        };

        _port.Open();

        // Anything the previous owner of this port left behind is not a frame of ours.
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    /// <summary>
    /// Returns zero on a bounded idle read, matching <see cref="IReaderTransport"/>: a reader with
    /// nothing to say is idle, not disconnected, and the caller polls again.
    /// </summary>
    public int Read(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        try
        {
            return _port.Read(buffer, 0, buffer.Length);
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public void Write(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        _port.Write(data, 0, data.Length);
    }

    public void Dispose()
    {
        try
        {
            if (_port.IsOpen)
            {
                _port.Close();
            }
        }
        catch (IOException)
        {
            // Closing a port whose device has already gone is not a failure worth propagating.
        }

        _port.Dispose();
    }
}
