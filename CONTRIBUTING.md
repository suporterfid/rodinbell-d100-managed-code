# Contributing

Use the .NET 8 SDK selected by `global.json`. Open `ManagedD100.sln` in Visual Studio
or work from the command line. The supported physical transport is Windows USB serial.

Run `pwsh ./scripts/verify.ps1` before submitting a change. It restores locked NuGet
dependencies, builds Release, runs the offline tests and creates a local package.
If intentionally changing dependencies, run `dotnet restore ManagedD100.sln --force-evaluate`
and include the updated `packages.lock.json` files.

Keep reader communication in `src/Rodinbell.D100`, runnable hardware examples in
`examples`, and offline regressions in `tests`. Preserve the serialized transaction
model, bounded queues, cancellation cleanup and device-identity checks. Do not label
an ambiguous FastTID payload as an EPC. Keep vendor binaries out of this repository.

For protocol or lifecycle changes, add a regression that demonstrates the failure
using a captured frame or controlled fake transport. Hardware tests are manual and
must state the reader model, firmware and commands tested. An unanswered command is
not proof that all firmware versions lack the feature. Avoid placing access passwords,
USB serial numbers or private tag inventories in issue reports.

Describe the problem, resulting behavior and validation in the pull request. New
project contributions are under the [MIT license](LICENSE); retain applicable notices
for third-party material.
