# Rodinbell D100 — managed C# client

.NET 8 library for the D100 serial protocol. No Rodinbell `Reader.dll`, vendor native
DLL, application P/Invoke, or x86 restriction. Uses Microsoft's `System.IO.Ports`
for serial I/O and `System.Management` for current Windows PnP identity, plus the
installed FTDI USB serial driver. The supplied SDK is unchanged.

## Build and run

From the repository root:

```powershell
dotnet build ManagedD100.sln -c Release
dotnet test ManagedD100.sln -c Release
dotnet run --project examples/D100.Console -- --list
dotnet run --project examples/D100.Console -- --port COM4 --standard-epc --seconds 5
```

Add a project reference to `src/Rodinbell.D100/Rodinbell.D100.csproj` in your application.
The public Windows constructor and discovery method carry platform annotations;
use a Windows target framework or guard with `OperatingSystem.IsWindows()`.

## Async reading

```csharp
using Rodinbell.D100;

if (!OperatingSystem.IsWindows()) return;

await using var reader = new D100Reader(new ReaderOptions
{
    // Omit PortName to discover one matching reader automatically.
    PortName = "COM4",
    // This D100 was verified to return ordinary EPC records.
    // Leave null if the reader's startup FastTID mode is unknown.
    InitialFastTidEnabled = false,
    AutoReconnect = true,
    ReconnectAttempts = 10
});

ReaderInfo info = await reader.ConnectAsync();
await reader.SetPowerAsync(20); // 18–26 dBm; temporary command 0x66

using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try
{
    await foreach (TagRead tag in reader.ReadTagsAsync(new ReadingOptions
    {
        DeduplicationWindow = TimeSpan.FromMilliseconds(500),
        ChannelCapacity = 1024,
        MaxTrackedTags = 4096
    }, stop.Token))
    {
        Console.WriteLine($"EPC={tag.Epc} TID={tag.Tid} antenna={tag.Antenna} reads={tag.ReadCount}");
    }
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }

await reader.StopReadingAsync();
await reader.DisconnectAsync();
```

Enumeration starts inventory. Only one iterator can be active per reader; dispose the
old iterator before starting another. `break`, cancellation, iterator disposal,
`StopReadingAsync`, `DisconnectAsync` and `DisposeAsync` all stop renewal of rounds.
You can also subscribe to `TagReadReceived`; it fires for observations delivered by the
active iterator. It does not start a separate background inventory.

Each observation includes EPC (nullable), raw identifier, TID/CRC when available, PC,
one-based antenna, frequency-channel index, raw RSSI byte, UTC receive timestamp,
first/last seen, cumulative count, source, COM port and raw payload. RSSI is deliberately
not converted to dBm without a verified model-specific calibration; the frequency index
is not MHz. Buffer timestamps describe retrieval, because the reader does not supply
the original observation times.

Deduplication uses EPC+TID (or the ambiguous raw identifier), across antennas, within
one enumeration. The window is measured from the last emitted observation. Suppressed
observations still increment the next emitted count. The bounded LRU tracker resets
counts when an identity is evicted; a new enumeration starts a new tracker. Counters
`SuppressedDuplicates`, `DroppedTags` and `ReconnectionCount` accumulate for the reader
instance. A full channel drops incoming observations and increments `DroppedTags` so
a slow consumer cannot stall the serial transaction. There is no durable delivery guarantee.
Observer exceptions are stored in `LastObserverError`; handlers run outside the serial
transaction and should remain brief.

## Methods and wire commands

Except `DisposeAsync`, all methods below accept a `CancellationToken` (named
`cancellationToken`). Async disposal always awaits cleanup.

| API | Command / behavior |
| --- | --- |
| `DiscoverAsync`, `ConnectAsync` | Firmware handshake `0x72`, normally 115200 then 38400 baud |
| `GetPowerAsync`, `SetPowerAsync` | `0x77`, temporary `0x66`; D100 range 18–26 dBm, measured on firmware 1.9. Outside it the reader answers `0x48` |
| `ReadTagsAsync` | Repeated finite `0x89` inventory rounds; `IAsyncEnumerable<TagRead>` |
| `StopReadingAsync` | Stop scheduling rounds and drain the current round |
| `DisconnectAsync`, `DisposeAsync` | Stop inventory/recovery and close the serial handle |
| `GetFastTidAsync` | `0x8E`, mode `0x00` or `0x8D` |
| `SetFastTidAsync` | Temporary `0x8C`; persistent `0x8D` only with `persist: true` |
| `InventoryBufferedAsync` | `0x80`, finite inventory into the device buffer |
| `ReadBufferAsync` | `0x90`; `clearAfterRead: true` selects `0x91` |
| `GetBufferCountAsync`, `ClearBufferAsync` | `0x92`, `0x93` |
| `SetBuzzerAsync` | `0x7A`: `Silent`, `AfterRound`, `AfterEveryTag` |
| `GetTemperatureAsync` | `0x7B`; signed integer Celsius |

```csharp
await reader.StopReadingAsync();
InventoryRound round = await reader.InventoryBufferedAsync();
ushort buffered = await reader.GetBufferCountAsync();
IReadOnlyList<TagRead> tags = await reader.ReadBufferAsync(clearAfterRead: true);
await reader.ClearBufferAsync();

await reader.SetBuzzerAsync(BuzzerMode.Silent);
int celsius = await reader.GetTemperatureAsync();
```

Buffer commands require stopped live inventory. Completion of buffer retrieval uses
the declared tag count, with validation of each record. The device owns the buffer;
other Gen2 operations or a power cycle can invalidate it. Retrieval and clearing are
not transactional across a connection loss, and failed commands are not replayed.
The generic protocol has no buzzer getter, so the client cannot restore an unknown
previous buzzer mode. Treat buzzer changes as potentially retained by firmware.

## FastTID

FastTID depends on reader firmware and compatible tag ICs. Its protocol can return a
modified PC followed by EPC+CRC+TID. It does not provide a universal boundary marker.

`InitialFastTidEnabled` defaults to null (unknown). A successful `GetFastTidAsync` or
`SetFastTidAsync` establishes the mode for the current connection. When enabled or
unknown, the library preserves `IdentifierHex` and reports `Epc = null` and
`FastTidAmbiguous = true` unless a valid `FastTidEpcLengthBytes` was supplied.
Provide that length only for a population whose EPC length is known. Do not infer it
from the total modified PC length. An ordinary fallback record of exactly the
configured EPC length remains an EPC with no TID. TID length is not assumed to be 96 bits.

On reconnect, a previously queried mode becomes unknown again unless a startup mode
was supplied or the caller explicitly set a mode, which is reapplied. For the tested
COM4 unit, `--standard-epc` supplies the verified ordinary-mode assumption.

```csharp
await using var fastReader = new D100Reader(new ReaderOptions
{
    FastTidEpcLengthBytes = 12 // Only if every relevant EPC is known to be 12 bytes.
});
await fastReader.ConnectAsync();
await fastReader.SetFastTidAsync(true); // Temporary, firmware/tag dependent.
```

## Cancellation, recovery and errors

One serialized transaction owns the serial stream. Controls wait between inventory
rounds. A stop drains the in-progress finite round, with the configured `RoundTimeout`
(default 5 seconds); the protocol has no separate real-time stop frame in this client.
If the summary never arrives, the session is closed at the deadline. Closing a lost
connection cannot confirm that RF activity has stopped; a previously issued finite
round may still finish on the device. Driver reads
are bounded to 100 ms and writes to 1000 ms; these bounds can extend elapsed time
slightly beyond a protocol timeout. A caller-cancelled wait for stop/disconnect does
not abandon the underlying cleanup. Cancellation of a command before it is sent has
no device side effect; cancellation after sending may leave a device-side change,
so the incomplete session is discarded before any subsequent command.

Discovery filters present FTDI ports by VID `0403` / PID `6001` by default, then checks
the reader's firmware reply. These IDs are not unique to Rodinbell; the protocol handshake
is an additional compatibility check, not a cryptographic identity. An explicit port
bypasses VID/PID filtering. More than one responding reader requires `PortName` or
`DeviceId`. Probes close their handles. Automatic reconnection follows the saved FTDI
PnP identity even if the COM number changes, and rejects a different device identity.
Registry registrations are matched against currently present Windows PnP devices;
retained entries for disconnected devices cannot supply the identity of a reused COM.
Missing or conflicting identity evidence excludes automatic discovery/reconnection;
an explicitly selected COM port remains usable without an identity. The WMI service
must be available to establish identity. See Microsoft's
[Win32_PnPEntity documentation](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-pnpentity).
PnP enumeration has a 2-second wait limit and catalog callers wait at most 3 seconds
for that query. A stalled Windows WMI call cannot be forcibly cancelled by this client;
only one query may remain outstanding so discovery does not accumulate workers. Its
results are discarded after timeout; a fresh query is required when that worker finishes.
The catalog wait can therefore add up to 3 seconds to discovery/recovery cancellation.

Recovery occurs when a running stream or next command needs the connection, not in an
idle polling thread. Each recovery has `ReconnectAttempts` attempts, separated by
`ReconnectDelay`. Consecutive failed inventory rounds are also bounded even when
handshakes succeed; a successful round resets that failure count. Only caller-set
power, FastTID and buzzer settings are reapplied. A failed arbitrary command is returned
to the caller, never silently replayed. `State`, `Info`, `LastRound`, `LastError` and
`ReconnectionCount` expose diagnostics. `LastError` retains the last failure after recovery.

`ReaderCommandException` preserves a firmware status byte. `ReaderProtocolException`
reports invalid payloads; `TimeoutException` means no complete matching reply arrived.
A timeout alone does not prove a command is unsupported. Discovery/recovery failures
use `ReaderDiscoveryException`/`IOException`. Framing validates length, address, command
and checksum, and handles fragmented/multiple packets. Noise that looks like an
unfinished long frame can require the transaction deadline and a fresh connection.

## Further documentation

See [hardware validation](HARDWARE.md), [protocol notes](PROTOCOL.md), and
[third-party notices](../THIRD_PARTY_NOTICES.md).
