using Microsoft.Win32;
using System.Management;
using System.Globalization;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text.RegularExpressions;

namespace Rodinbell.D100;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSerialTransportFactory : IReaderTransportFactory
{
    private static readonly TimeSpan WmiEnumerationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WmiLookupTimeout = TimeSpan.FromSeconds(3);
    private static readonly IReadOnlySet<string> NoPresentDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly BoundedDeviceQuery presence = new(QueryPresentFtdiDeviceIds, WmiLookupTimeout);

    public IReadOnlyList<PortDescriptor> GetPorts()
    {
        var currentPorts = SerialPort.GetPortNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registryPorts = ReadRegistryPorts(currentPorts);
        var presentDeviceIds = presence.Read();
        return PortCatalog.Build(currentPorts, registryPorts, presentDeviceIds);
    }

    private static IReadOnlyList<PortDescriptor> ReadRegistryPorts(IReadOnlySet<string> currentPorts)
    {
        var registryPorts = new List<PortDescriptor>();
        try
        {
            using var bus = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\FTDIBUS");
            if (bus is null) return registryPorts;
            foreach (string deviceName in bus.GetSubKeyNames())
            {
                var ids = Regex.Match(deviceName, @"VID_([0-9A-F]{4})\+PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                if (!ids.Success) continue;
                using var device = bus.OpenSubKey(deviceName);
                if (device is null) continue;
                foreach (string instance in device.GetSubKeyNames())
                {
                    using var node = device.OpenSubKey(instance);
                    using var parameters = node?.OpenSubKey("Device Parameters");
                    if (parameters?.GetValue("PortName") is not string port || !currentPorts.Contains(port)) continue;
                    registryPorts.Add(new PortDescriptor(port, $@"FTDIBUS\{deviceName}\{instance}",
                        node?.GetValue("FriendlyName") as string,
                        ushort.Parse(ids.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        ushort.Parse(ids.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            registryPorts.Clear();
        }
        return registryPorts;
    }

    private static IReadOnlySet<string> QueryPresentFtdiDeviceIds()
    {
        var deviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var enumeration = new System.Management.EnumerationOptions
            {
                ReturnImmediately = true,
                Rewindable = false,
                Timeout = WmiEnumerationTimeout
            };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\CIMV2"),
                new ObjectQuery("SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'FTDIBUS%' AND Present = TRUE"),
                enumeration);
            using var results = searcher.Get();
            foreach (ManagementBaseObject result in results)
            {
                using (result)
                    if (result["PNPDeviceID"] is string deviceId) deviceIds.Add(deviceId);
            }
            return deviceIds;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return NoPresentDevices;
        }
    }

    public IReaderTransport Open(PortDescriptor descriptor, int baudRate) => new SerialTransport(descriptor.PortName, baudRate);

    private sealed class SerialTransport : IReaderTransport
    {
        private readonly SerialPort port;
        public SerialTransport(string name, int baudRate)
        {
            port = new SerialPort(name, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None, DtrEnable = false, RtsEnable = false,
                ReadTimeout = 100, WriteTimeout = 1000
            };
            try { port.Open(); }
            catch { port.Dispose(); throw; }
        }
        public int Read(byte[] buffer)
        {
            try { return port.Read(buffer, 0, buffer.Length); }
            catch (TimeoutException) { return 0; }
            catch (InvalidOperationException ex) { throw new IOException("The serial port closed during a read.", ex); }
        }
        public void Write(byte[] data)
        {
            try { port.Write(data, 0, data.Length); }
            catch (InvalidOperationException ex) { throw new IOException("The serial port closed during a write.", ex); }
        }
        public void Dispose() => port.Dispose();
    }
}
