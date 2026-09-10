# Rodinbell D100 Managed Code

A managed C# client for the Rodinbell D100 UHF RFID reader on Windows, targeting
.NET 8. Communicates directly over USB serial using Microsoft's `System.IO.Ports`
and identifies connected devices through `System.Management`. No vendor DLL,
application P/Invoke, or x86 restriction.

## Features

- Async connection and controls with cancellation.
- `IAsyncEnumerable<TagRead>` and rich tag events: EPC, TID when available, PC,
  antenna, raw RSSI, timestamps, read counts, and raw payloads.
- Automatic port discovery and reconnection pinned to the Windows device identity.
- Bounded queues, overflow reporting and configurable deduplication.
- Temporary transmit power, real-time and buffered inventory.
- FastTID, buzzer and temperature APIs, subject to firmware support.

## Quick start

Install the .NET 8 SDK. Hardware access also requires the reader's Windows FTDI USB
serial driver. From this repository root:

```powershell
dotnet restore ManagedD100.sln --locked-mode
dotnet build ManagedD100.sln -c Release --no-restore
dotnet test ManagedD100.sln -c Release --no-build --no-restore
dotnet run --project examples/D100.Console -- --list
dotnet run --project examples/D100.Console -- --port COM4 --standard-epc --seconds 5
```

`--standard-epc` declares that the reader returns ordinary EPC records. Use it only
for a verified standard-mode reader. Unknown FastTID mode preserves the raw identifier
instead of labeling a combined identifier as an EPC.

Reference [src/Rodinbell.D100/Rodinbell.D100.csproj](src/Rodinbell.D100/Rodinbell.D100.csproj)
from your application. See [usage and API examples](docs/USAGE.md) for async streaming,
power, buffer operations, cancellation and reconnection behavior.

## Repository layout

```text
.github/              Windows CI, dependency updates, pull-request template
docs/                 Usage, protocol, hardware validation and GitHub setup
examples/D100.Console/ Bounded console sample; hardware access is explicit
scripts/verify.ps1    Restore, build, offline tests and local NuGet packaging
src/Rodinbell.D100/   Managed reader library
tests/Rodinbell.D100.Tests/  Protocol and simulated transport tests
ManagedD100.sln       Visual Studio / dotnet solution
global.json          .NET 8 SDK selection
LICENSE              MIT license
```

## Verification and packaging

The script example uses PowerShell 7 (`pwsh`).

```powershell
pwsh ./scripts/verify.ps1
```

This runs the offline suite and creates the library package under `artifacts/packages/`.
It does not open a serial port or publish a package. The same script runs in GitHub
Actions on Windows for pushes to `main`, pull requests and manual runs.

The inherited implementation has 42 offline tests. Physical tests on a D100 with
firmware 1.9 confirmed discovery, EPC streaming, temporary power, buffered retrieval,
stop and disconnect. FastTID and temperature queries did not answer on that firmware;
buzzer and buffer-clear commands were tested offline only. See
[hardware validation and limitations](docs/HARDWARE.md).

## Documentation

- [Usage and API behavior](docs/USAGE.md)
- [Protocol notes and references](docs/PROTOCOL.md)
- [Hardware validation](docs/HARDWARE.md)
- [GitHub repository and CI](docs/GITHUB.md)
- [Contributing](CONTRIBUTING.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

## License

[MIT](LICENSE). Dependencies and referenced third-party projects retain their own
licenses. This is an independent project, not an official Rodinbell SDK.
