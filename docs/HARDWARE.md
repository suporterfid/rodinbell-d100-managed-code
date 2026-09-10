# Hardware validation

The implementation was exercised on a Rodinbell D100 connected through FTDI USB serial
on Windows on 2026-09-09. The unit replied on COM4 at 115200 baud, 8N1, address `01`,
firmware `1.9`. Device-specific serial numbers and raw session logs remain in the
original development workspace; they are not required to build this repository.

| Feature | Evidence from that unit |
| --- | --- |
| Discovery, connection, EPC stream, stop, disconnect | Passed |
| Temporary transmit power | 18 → 20 → restored 18 dBm; readback verified |
| Buffered inventory, count, retrieval (`80/92/90`) | Passed; EPC, CRC and read count decoded |
| Recovery after command timeout | Reopened the same device and resumed inventory |
| FastTID query (`8E`) | No reply within 2 seconds; live enable/TID acquisition unverified |
| Temperature query (`7B`) | No reply within 2 seconds; live temperature unverified |
| Buzzer, buffer read-and-clear/clear (`7A/91/93`) | Offline protocol/transport tests only |
| USB unplug and changed COM assignment | Simulated tests; physical unplug not performed |

The last bounded stream observed two EPCs, emitted five observations, suppressed seven
duplicates and dropped none. A follow-up discovery succeeded after the final query
freshness fix. The reader was disconnected with power at 18 dBm.

## Manual check

Close other applications using the reader. From the repository root:

```powershell
dotnet run --project examples/D100.Console -- --list
dotnet run --project examples/D100.Console -- --port COM4 --standard-epc --seconds 5
```

Use the port returned by discovery. `--standard-epc` is a caller assertion about the
record layout; leave it off when FastTID mode is unknown. The example can optionally
exercise `--power 20`, `--temperature`, `--fast-tid --epc-bytes 12`, `--buffered`, and
`--buzzer silent|round|tag`. Use a known EPC byte length only for a matching tag population.

Temporary power/FastTID changes acknowledged by the example are restored in `finally`.
Restoration can fail if the device disappears. Buzzer has no getter and cannot be
restored automatically. The default sample and automated tests do not change buzzer mode.
No tag-memory write, EPC rewrite or password modification is implemented.

Automated CI uses fake transports and never accesses COM4. A clean build or passing
protocol test is not evidence that a firmware-specific command works on hardware.

## Transmit power range, measured 2026-09-10

A D100 on firmware 1.9 was swept across the whole byte range the command accepts, 0–30 dBm in steps
of one, over USB serial on `COM4` at 115200. After each setting the power was read back with `0x77`
and an inventory (`0x89`) was run for at least a second with a tag fixed to the antenna, so a power
the module accepted but did not transmit at would show as reads of zero.

| dBm | `0x66` status | read back | tag frames in ~1.2 s |
|---|---|---|---|
| 0–17 | `0x48` | — | 0 |
| 18–26 | `0x10` | as set | 78–132 |
| 27–30 | `0x48` | — | 0 |

**18–26 dBm inclusive**, and every accepted level transmits — there is no level the reader accepts
and then stays silent on. Boundaries were re-run at 16–19 and 25–28 and reproduced exactly.

This confirms the range `SetPowerAsync` already enforced; it had been documented but not measured
here. `0x66` is the temporary set, so the sweep persisted nothing to the device.

Read counts are contact-range with the tag on the antenna and say nothing about range at distance.
They are recorded only because a level transmitting weakly would have shown a much lower count, and
none did.
