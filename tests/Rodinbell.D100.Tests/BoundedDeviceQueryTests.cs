using Xunit;

namespace Rodinbell.D100.Tests;

public sealed class BoundedDeviceQueryTests
{
    [Fact]
    public async Task Expired_query_cannot_supply_late_identity_to_a_new_lookup()
    {
        using var releaseOldQuery = new ManualResetEventSlim();
        var oldQueryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var lookup = new BoundedDeviceQuery(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                oldQueryStarted.SetResult();
                releaseOldQuery.Wait();
                return new HashSet<string> { "removed-reader" };
            }
            return new HashSet<string> { "current-reader" };
        }, TimeSpan.FromMilliseconds(200));

        try
        {
            Assert.Empty(lookup.Read()); // First query has expired but its worker is still blocked.
            await oldQueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Isolate this caller from the thread pool occupied by the blocked query and other tests.
            var next = Task.Factory.StartNew(lookup.Read, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            // The expired generation must return empty while its old worker is still blocked.
            Assert.Empty(await next.WaitAsync(TimeSpan.FromSeconds(5)));
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
