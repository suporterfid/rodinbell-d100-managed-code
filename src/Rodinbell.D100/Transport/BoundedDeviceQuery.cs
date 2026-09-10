namespace Rodinbell.D100;

/// <summary>Bounds callers without accumulating workers when a Windows metadata call stalls.</summary>
internal sealed class BoundedDeviceQuery(Func<IReadOnlySet<string>> query, TimeSpan timeout)
{
    private sealed class Attempt(Task<IReadOnlySet<string>> task)
    {
        public Task<IReadOnlySet<string>> Task { get; } = task;
        public bool Expired;
    }
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();
    private Attempt? pending;

    public IReadOnlySet<string> Read()
    {
        Attempt attempt;
        lock (sync)
        {
            if (pending is null || pending.Task.IsCompleted) pending = new Attempt(Task.Run(query));
            if (pending.Expired) return Empty;
            attempt = pending;
        }
        try
        {
            if (!attempt.Task.Wait(timeout))
            {
                lock (sync) attempt.Expired = true;
                return Empty;
            }
            var result = attempt.Task.GetAwaiter().GetResult();
            lock (sync) return attempt.Expired ? Empty : result;
        }
        catch (Exception) { return Empty; }
    }
}
