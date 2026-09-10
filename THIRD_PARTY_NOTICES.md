# Third-party notices

The repository's [MIT license](LICENSE) applies to this project's code and documentation.
Third-party dependencies and reference projects keep their respective licenses.

## Dependencies

The library uses Microsoft's `System.IO.Ports` and `System.Management` 8.0.0 packages,
distributed under MIT. Development dependencies include Microsoft.NET.Test.Sdk, xUnit
and the xUnit Visual Studio adapter. Exact direct and transitive dependency versions
and content hashes are recorded in each project's `packages.lock.json`; package license
metadata and notices are supplied by NuGet. Dependencies are restored, not vendored.

## Protocol references

- Rodinbell SDK C# sources and API manuals were consulted during protocol analysis.
  Vendor source archives, manuals, drivers and DLLs are not included in this repository.
- [rust-invelion](https://github.com/russss/rust-invelion/tree/7786bbd9613e7e8564a34fd73ae16e8fe633f954),
  reviewed at commit `7786bbd9613e7e8564a34fd73ae16e8fe633f954`, declares
  **LGPL-3.0-or-later**. It was used as a supporting reference for command identifiers,
  framing and field layouts. This repository contains independently written C# code;
  no Rust source or compiled Rust library is bundled. The reference project's license
  is not replaced by this project's MIT license.

Rodinbell, D100, Microsoft, Windows, FTDI and other names identify their respective
products or owners. This project does not claim endorsement by those organizations.
