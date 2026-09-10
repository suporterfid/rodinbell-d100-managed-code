using Rodinbell.D100;

if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("This sample requires Windows."); return 1; }
string? Value(string name)
{
    int index = Array.IndexOf(args, name);
    if (index < 0) return null;
    if (index + 1 >= args.Length) throw new ArgumentException($"Missing value for {name}.");
    return args[index + 1];
}
try
{
    if (args.Contains("--help"))
    {
        Console.WriteLine("D100.Console [--list] [--port COM4] [--seconds 5] [--standard-epc] [--power 20] [--temperature] [--fast-tid --epc-bytes 12] [--buffered] [--buzzer silent|round|tag]");
        return 0;
    }
    var options = new ReaderOptions
    {
        PortName = Value("--port"),
        InitialFastTidEnabled = args.Contains("--standard-epc") ? false : null,
        FastTidEpcLengthBytes = Value("--epc-bytes") is string length ? int.Parse(length) : null
    };
    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
    if (args.Contains("--list"))
    {
        foreach (var found in await D100Reader.DiscoverAsync(options, shutdown.Token))
            Console.WriteLine($"{found.Port.PortName} {found.BaudRate} firmware={found.FirmwareVersion} address={found.Address:X2} device={found.Port.DeviceId}");
        return 0;
    }
    double seconds = Value("--seconds") is string duration ? double.Parse(duration, System.Globalization.CultureInfo.InvariantCulture) : 5;
    if (seconds is <= 0 or > 3600) throw new ArgumentOutOfRangeException("seconds", "Use 0 < seconds <= 3600.");
    await using var reader = new D100Reader(options);
    var info = await reader.ConnectAsync(shutdown.Token);
    Console.WriteLine($"Connected {info.Port.PortName}, {info.BaudRate} baud, firmware {info.FirmwareVersion}, address {info.Address:X2}, device {info.Port.DeviceId}");
    byte originalPower = await reader.GetPowerAsync(shutdown.Token);
    Console.WriteLine($"Power={originalPower} dBm");
    bool? originalFastTid = null;
    bool changedPower = false, changedFastTid = false;
    try
    {
        if (Value("--power") is string power)
        {
            await reader.SetPowerAsync(byte.Parse(power), shutdown.Token);
            changedPower = true;
            Console.WriteLine($"Temporary power={await reader.GetPowerAsync(shutdown.Token)} dBm");
        }
        try
        {
            originalFastTid = await reader.GetFastTidAsync(shutdown.Token);
            Console.WriteLine($"FastTID={originalFastTid}");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ReaderCommandException)
        { Console.WriteLine($"FastTID query unavailable: {ex.Message}"); }
        if (args.Contains("--fast-tid"))
        {
            if (originalFastTid is null) Console.WriteLine("FastTID enable skipped: original mode is unknown and cannot be restored.");
            else
            {
                await reader.SetFastTidAsync(true, cancellationToken: shutdown.Token);
                changedFastTid = true;
                Console.WriteLine($"Temporary FastTID={await reader.GetFastTidAsync(shutdown.Token)}");
            }
        }
        if (args.Contains("--temperature"))
        {
            try { Console.WriteLine($"Temperature={await reader.GetTemperatureAsync(shutdown.Token)} C"); }
            catch (Exception ex) when (ex is IOException or TimeoutException or ReaderCommandException)
            { Console.WriteLine($"Temperature unavailable: {ex.Message}"); }
        }
        if (Value("--buzzer") is string buzzer)
        {
            var mode = buzzer switch { "silent" => BuzzerMode.Silent, "round" => BuzzerMode.AfterRound, "tag" => BuzzerMode.AfterEveryTag, _ => throw new ArgumentException("Unknown buzzer mode.") };
            await reader.SetBuzzerAsync(mode, shutdown.Token);
            Console.WriteLine($"Buzzer={mode}; no getter exists to restore its previous mode.");
        }
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        reading.CancelAfter(TimeSpan.FromSeconds(seconds));
        int count = 0;
        try
        {
            await foreach (var tag in reader.ReadTagsAsync(new ReadingOptions { DeduplicationWindow = TimeSpan.FromMilliseconds(500), Interval = TimeSpan.FromMilliseconds(10) }, reading.Token))
            {
                count++;
                Console.WriteLine($"{tag.Timestamp:O} EPC={tag.Epc ?? "<ambiguous>"} TID={tag.Tid ?? "-"} identifier={tag.IdentifierHex} PC={tag.Pc:X4} antenna={tag.Antenna} frequencyIndex={tag.FrequencyIndex} RSSIraw={tag.RssiRaw} count={tag.ReadCount} source={tag.Source}");
            }
        }
        catch (OperationCanceledException) when (reading.IsCancellationRequested) { }
        await reader.StopReadingAsync();
        Console.WriteLine($"Stopped. Delivered={count}, suppressed={reader.SuppressedDuplicates}, dropped={reader.DroppedTags}, reconnects={reader.ReconnectionCount}, state={reader.State}, lastRound={reader.LastRound}");
        if (args.Contains("--buffered") && !shutdown.IsCancellationRequested)
        {
            var round = await reader.InventoryBufferedAsync(cancellationToken: shutdown.Token);
            Console.WriteLine($"Buffered inventory={round}; bufferCount={await reader.GetBufferCountAsync(shutdown.Token)}");
            foreach (var tag in await reader.ReadBufferAsync(cancellationToken: shutdown.Token))
                Console.WriteLine($"Buffer EPC={tag.Epc ?? "<ambiguous>"} TID={tag.Tid ?? "-"} CRC={tag.Crc} count={tag.ReadCount}");
        }
    }
    finally
    {
        await reader.StopReadingAsync();
        if (changedFastTid && originalFastTid.HasValue)
        {
            await reader.SetFastTidAsync(originalFastTid.Value);
            Console.WriteLine($"Restored FastTID={await reader.GetFastTidAsync()}");
        }
        if (changedPower)
        {
            await reader.SetPowerAsync(originalPower);
            Console.WriteLine($"Restored power={await reader.GetPowerAsync()} dBm");
        }
        await reader.DisconnectAsync();
        Console.WriteLine($"Disconnected: {reader.State}");
    }
    return 0;
}
catch (OperationCanceledException) { Console.WriteLine("Cancelled."); return 0; }
catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 1; }
