using System.Globalization;
using Rodinbell.D100;

// Sweeps transmit power and reports, per level, whether EPCs were read and how many.
//
// Separate from D100.Console, which is Windows-only because it discovers the reader through WMI.
// This one names its port and goes through SerialPortTransportFactory, so it runs anywhere
// System.IO.Ports does - COM4 on Windows, /dev/ttyUSB0 on Linux - and exercises the same portable
// path a host that publishes Native AOT would use.
//
// The D100 accepts 18-26 dBm; outside that it refuses the command with status 0x48. See
// docs/HARDWARE.md.

if (args.Contains("--help") || args.Length == 0)
{
    Console.WriteLine("D100.PowerSweep <port> [low=18] [high=26] [dwellSeconds=1] [--fast-tid-query]");
    Console.WriteLine();
    Console.WriteLine("  port    COM4, /dev/ttyUSB0 - named, not discovered");
    Console.WriteLine("  low     lowest dBm to try, 18-26");
    Console.WriteLine("  high    highest dBm to try, 18-26");
    Console.WriteLine("  dwell   seconds of inventory per level. Parsed as invariant: use 1.5, not 1,5");
    Console.WriteLine();
    Console.WriteLine("  --fast-tid-query  ask the reader its FastTID mode first. Firmware 1.9 does");
    Console.WriteLine("                    not answer, and the timeout costs two seconds per run.");
    return args.Contains("--help") ? 0 : 1;
}

var port = args[0];
var low = args.Length > 1 ? byte.Parse(args[1], CultureInfo.InvariantCulture) : (byte)18;
var high = args.Length > 2 ? byte.Parse(args[2], CultureInfo.InvariantCulture) : (byte)26;

// Invariant on purpose. On a host whose culture uses a comma for decimals, "1.0" parses as ten and
// the sweep silently runs ten times longer than asked.
var dwell = TimeSpan.FromSeconds(args.Length > 3
    ? double.Parse(args[3], CultureInfo.InvariantCulture)
    : 1.0);

if (low > high)
{
    Console.Error.WriteLine($"low ({low}) is above high ({high}).");
    return 1;
}

await using var reader = new D100Reader(new SerialPortTransportFactory(port), new ReaderOptions
{
    PortName = port,

    // Asserts standard EPC records rather than asking. In FastTID mode the reader returns a combined
    // identifier and the library reports Epc = null when it cannot split it safely, which would read
    // here as "no tags" and look like a range problem.
    InitialFastTidEnabled = false,
});

var info = await reader.ConnectAsync();
Console.WriteLine($"connected: firmware {info.FirmwareVersion}, {info.Port.PortName} @ {info.BaudRate}");

if (args.Contains("--fast-tid-query"))
{
    try
    {
        Console.WriteLine($"FastTID reported as {await reader.GetFastTidAsync()}");
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or ReaderCommandException)
    {
        Console.WriteLine($"FastTID query unanswered ({ex.GetType().Name}); assuming standard EPC records");
    }
}

Console.WriteLine($"dwell {dwell.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s per power level");
Console.WriteLine();

var perPower = new List<(int Dbm, int Distinct, long Reads, List<string> Epcs)>();

for (int dbm = low; dbm <= high; dbm++)
{
    await reader.SetPowerAsync((byte)dbm);

    var seen = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    long reads = 0;

    using var window = new CancellationTokenSource(dwell);
    try
    {
        await foreach (var tag in reader.ReadTagsAsync(cancellationToken: window.Token))
        {
            reads++;

            // Counted as a read either way; only a decoded EPC counts as an EPC. Keeping the two
            // apart is what distinguishes "nothing in the field" from "read but not decodable".
            if (!string.IsNullOrWhiteSpace(tag.Epc))
            {
                seen[tag.Epc] = seen.TryGetValue(tag.Epc, out var n) ? n + 1 : 1;
            }
        }
    }
    catch (OperationCanceledException)
    {
        // The dwell elapsed, which is how each level ends.
    }

    try
    {
        await reader.StopReadingAsync();
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or ReaderCommandException)
    {
        Console.WriteLine($"  (stop after {dbm} dBm: {ex.GetType().Name})");
    }

    perPower.Add((dbm, seen.Count, reads, [.. seen.Keys.OrderBy(static e => e, StringComparer.Ordinal)]));
    Console.WriteLine($"  {dbm,2} dBm -> {seen.Count} distinct EPC(s), {reads} read(s)");
}

Console.WriteLine();
Console.WriteLine("dBm  distinct EPCs  total reads  read?");
Console.WriteLine("---  -------------  -----------  -----");
foreach (var row in perPower)
{
    Console.WriteLine($"{row.Dbm,3}  {row.Distinct,13}  {row.Reads,11}  {(row.Distinct > 0 ? "yes" : "no")}");
}

var everything = perPower.SelectMany(static r => r.Epcs)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .OrderBy(static e => e, StringComparer.Ordinal)
    .ToList();

Console.WriteLine();
Console.WriteLine($"distinct EPCs across the sweep: {everything.Count}");
foreach (var epc in everything)
{
    var powers = perPower
        .Where(r => r.Epcs.Contains(epc, StringComparer.OrdinalIgnoreCase))
        .Select(static r => r.Dbm)
        .ToList();

    Console.WriteLine($"  {epc}   seen at {string.Join(",", powers)} dBm");
}

return 0;
