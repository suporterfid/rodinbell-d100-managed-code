using Xunit;

namespace Rodinbell.D100.Tests;

public sealed class BoundedDeviceQueryTests
{
    [Fact]
    public async Task Expired_query_cannot_supply_late_identity_to_a_new_lookup()
    {
        using var releaseOldQuery = new ManualResetEventSlim();
        int calls = 0;
        var lookup = new BoundedDeviceQuery(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                releaseOldQuery.Wait();
                return new HashSet<string> { "removed-reader" };
            }
            return new HashSet<string> { "current-reader" };
        }, TimeSpan.FromMilliseconds(200));

        try
        {
            Assert.Empty(lookup.Read()); // First query has expired but its worker is still blocked.
            var next = Task.Run(lookup.Read);
            // Expired generations must fail closed immediately, without waiting for the old worker.
            Assert.Empty(await next.WaitAsync(TimeSpan.FromMilliseconds(100)));
            Assert.Equal(1, Volatile.Read(ref calls));
            releaseOldQuery.Set();

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            IReadOnlySet<string> result;
            do
            {
                await Task.Delay(5, deadline.Token);
                result = lookup.Read();
                Assert.DoesNotContain("removed-reader", result);
            } while (result.Count == 0);
            Assert.Equal("current-reader", Assert.Single(result));
            Assert.Equal(2, Volatile.Read(ref calls));
        }
        finally { releaseOldQuery.Set(); }
    }
}
