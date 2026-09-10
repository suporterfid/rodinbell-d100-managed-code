# Protocol notes

Serial framing used by the managed library:

```text
A0 LEN ADDR CMD DATA... CHECKSUM
LEN = DATA.Length + 3
Total frame bytes = LEN + 2
Sum of every frame byte modulo 256 = 0
```

For example, firmware request `A0 03 FF 72 EC` elicited response
`A0 05 01 72 01 09 DE` from the tested unit: address `01`, firmware `1.9`.
Serial defaults are 115200 baud, 8 data bits, no parity, one stop bit, no handshake,
with DTR and RTS disabled. Discovery can also probe 38400 baud.

| Operation | Command |
| --- | --- |
| Firmware | `72` |
| Read / temporary set power | `77` / `66` |
| Real-time finite inventory | `89` |
| Buffered inventory | `80` |
| Read / read-and-clear buffer | `90` / `91` |
| Buffer count / clear | `92` / `93` |
| Query / temporary set FastTID | `8E` / `8C` |
| Persistent FastTID, explicitly requested | `8D` |
| Buzzer / temperature | `7A` / `7B` |

### Status bytes seen on hardware

| Status | Meaning |
|---|---|
| `0x10` | the command changed something and was acknowledged |
| `0x48` | a parameter was outside the range the reader accepts |

`0x48` is what `0x66` answers for any transmit power outside 18–26 dBm, measured by sweeping 0–30 on
firmware 1.9 — see [hardware validation](HARDWARE.md). A caller that sends a bad value gets this and
nothing else to go on, so it is worth naming.

One transaction owns the byte stream. Responses must match address and command.
The decoder handles fragmentation, bounds and checksums before interpreting payloads.
Single-byte status responses are classified before exact seven-byte real-time summaries;
short, valid EPC records are not misclassified as summaries. Buffered completion is
determined from the declared tag count. Multibyte counts and rates are big-endian.

Real-time observations contain a packed frequency/antenna byte, two PC bytes, a variable
identifier and one raw RSSI byte. Antennas are normalized to one-based values. PC length
must match the identifier length. FastTID can replace the normal identifier with
EPC+CRC+TID; a known EPC length is required to split it safely. Otherwise the raw
identifier remains available with `Epc = null`. No universal TID length or RSSI-to-dBm
conversion is assumed. See [usage](USAGE.md) for exact API semantics.

## Reference review

The supplied Rodinbell SDK and captured D100 frames were cross-checked against
[rust-invelion at 7786bbd](https://github.com/russss/rust-invelion/tree/7786bbd9613e7e8564a34fd73ae16e8fe633f954).
The reviewed Rust version has parser issues that were not copied: out-of-bounds access
while reporting a bad checksum, assertions on malformed frames, misclassification of
short inventory payloads, and an unchecked partial-write result. The C# code uses
bounded parsing and the managed serial write contract.

The comparison matched 34 captured frames and five checksum vectors. Rust was inspected
as source, not compiled or executed in the original environment. The managed project
has its own 42-test offline suite. Consult [third-party notices](../THIRD_PARTY_NOTICES.md)
for reference provenance and licensing, and [hardware validation](HARDWARE.md) for the
distinction between protocol support and confirmed firmware behavior.
