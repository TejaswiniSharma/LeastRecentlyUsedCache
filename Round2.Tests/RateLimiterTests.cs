using System.Threading.RateLimiting;
using NUnit.Framework;

namespace Round2.Tests;

/// <summary>
/// Unit tests for token-bucket rate limiter behaviour.
///
/// All tests set <c>AutoReplenishment = false</c> and call
/// <c>TryReplenish()</c> manually so timing is fully deterministic —
/// no real-time waits needed.
///
/// The tests verify four things:
///   1. Requests within the token budget are granted immediately.
///   2. Requests that exceed available tokens are QUEUED (not rejected),
///      provided queue space is available.
///   3. Requests that exceed both the token budget AND the queue are
///      rejected immediately (IsAcquired = false).
///   4. Queued requests are served in FIFO (OldestFirst) order.
/// </summary>
[TestFixture]
public class RateLimiterTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="TokenBucketRateLimiter"/> with manual replenishment.
    /// Returning the concrete type gives access to <c>TryReplenish()</c>.
    /// </summary>
    private static TokenBucketRateLimiter MakeLimiter(
        int tokenLimit      = 3,
        int queueLimit      = 2,
        int tokensPerPeriod = 1) =>
        new(new TokenBucketRateLimiterOptions
        {
            TokenLimit           = tokenLimit,
            QueueLimit           = queueLimit,
            TokensPerPeriod      = tokensPerPeriod,
            ReplenishmentPeriod  = TimeSpan.FromSeconds(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment    = false     // tests call TryReplenish() explicitly
        });

    // ═══════════════════════════════════════════════════════════════════════════
    // Within-limit — immediate grant
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Request_WithinTokenLimit_IsGrantedImmediately()
    {
        using var limiter = MakeLimiter(tokenLimit: 3, queueLimit: 0);

        using var lease = await limiter.AcquireAsync();

        Assert.That(lease.IsAcquired, Is.True,
            "A request within the token budget must be granted immediately.");
    }

    [Test]
    public async Task MultipleRequests_WithinTokenLimit_AllGrantedImmediately()
    {
        using var limiter = MakeLimiter(tokenLimit: 3, queueLimit: 0);
        var leases = new List<RateLimitLease>();

        try
        {
            for (int i = 0; i < 3; i++)
                leases.Add(await limiter.AcquireAsync());

            Assert.That(leases.All(l => l.IsAcquired), Is.True,
                "Every request within the token budget must be granted immediately.");
        }
        finally
        {
            foreach (var l in leases) l.Dispose();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Queuing behaviour — over budget but queue has space
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Request_WhenTokensExhausted_IsQueuedNotRejected()
    {
        using var limiter = MakeLimiter(tokenLimit: 1, queueLimit: 1);

        // Exhaust the single token
        using var firstLease = await limiter.AcquireAsync();
        Assert.That(firstLease.IsAcquired, Is.True);

        // Second request: no tokens left, but queue has space → must be queued
        var queuedTask = limiter.AcquireAsync().AsTask();

        // AutoReplenishment is off, so the task stays pending indefinitely
        Assert.That(queuedTask.IsCompleted, Is.False,
            "Request should sit in the queue, not be resolved or rejected immediately.");

        // Replenish to drain the pending task cleanly before the limiter is disposed
        limiter.TryReplenish();
        (await queuedTask).Dispose();
    }

    [Test]
    public async Task QueuedRequest_IsGrantedAfterTokenReplenishment()
    {
        using var limiter = MakeLimiter(tokenLimit: 1, queueLimit: 1);

        using var firstLease = await limiter.AcquireAsync(); // exhaust token

        var queuedTask = limiter.AcquireAsync().AsTask();
        Assert.That(queuedTask.IsCompleted, Is.False, "Precondition: request is queued.");

        // Replenish tokens — the queued request must now be served
        limiter.TryReplenish();

        using var queuedLease = await queuedTask;

        Assert.That(queuedLease.IsAcquired, Is.True,
            "Queued request must be granted once tokens are replenished.");
    }

    [Test]
    public async Task MultipleQueuedRequests_AllGrantedAfterReplenishments()
    {
        // tokenLimit=1, queueLimit=2, tokensPerPeriod=1
        // Three extra requests are queued; three TryReplenish() calls serve them one-by-one
        using var limiter = MakeLimiter(tokenLimit: 1, queueLimit: 3, tokensPerPeriod: 1);

        using var firstLease = await limiter.AcquireAsync();

        var tasks = Enumerable.Range(0, 3)
                              .Select(_ => limiter.AcquireAsync().AsTask())
                              .ToList();

        // All three must be queued, none completed yet
        Assert.That(tasks.All(t => !t.IsCompleted), Is.True,
            "All queued requests must be pending before replenishment.");

        // One replenishment → one request served at a time
        foreach (var task in tasks)
        {
            limiter.TryReplenish();
            using var lease = await task;
            Assert.That(lease.IsAcquired, Is.True,
                "Each queued request must be granted after its replenishment tick.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Queue full — immediate rejection
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Request_WhenQueueFull_IsRejectedImmediately()
    {
        using var limiter = MakeLimiter(tokenLimit: 1, queueLimit: 1);

        using var firstLease = await limiter.AcquireAsync();  // exhaust token
        var queuedTask = limiter.AcquireAsync().AsTask();     // fill the single queue slot

        // Queue is now full — this request must be rejected immediately
        using var rejectedLease = await limiter.AcquireAsync();

        Assert.That(rejectedLease.IsAcquired, Is.False,
            "A request arriving when both the token bucket and queue are full must be rejected.");

        // Clean up the queued task
        limiter.TryReplenish();
        await queuedTask;
    }

    [Test]
    public async Task MultipleRequests_BeyondQueueLimit_AllRejected()
    {
        using var limiter = MakeLimiter(tokenLimit: 1, queueLimit: 1);

        using var firstLease = await limiter.AcquireAsync();
        var queuedTask = limiter.AcquireAsync().AsTask(); // fills the queue

        // Any further requests must be rejected
        var overflowLeases = new List<RateLimitLease>();
        try
        {
            for (int i = 0; i < 3; i++)
                overflowLeases.Add(await limiter.AcquireAsync());

            Assert.That(overflowLeases.All(l => !l.IsAcquired), Is.True,
                "All requests beyond the queue limit must be rejected.");
        }
        finally
        {
            foreach (var l in overflowLeases) l.Dispose();
            limiter.TryReplenish();
            await queuedTask;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FIFO ordering
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task QueuedRequests_AreProcessedInOldestFirstOrder()
    {
        // tokenLimit=1, tokensPerPeriod=1 → each TryReplenish() adds exactly 1 token
        using var limiter = MakeLimiter(tokenLimit: 1, queueLimit: 2, tokensPerPeriod: 1);
        var completionOrder = new List<int>();

        using var firstLease = await limiter.AcquireAsync(); // exhaust token

        // Enqueue A then B — A is older
        var taskA = limiter.AcquireAsync().AsTask()
                           .ContinueWith(t => { completionOrder.Add(1); t.Result.Dispose(); },
                                         TaskContinuationOptions.ExecuteSynchronously);
        var taskB = limiter.AcquireAsync().AsTask()
                           .ContinueWith(t => { completionOrder.Add(2); t.Result.Dispose(); },
                                         TaskContinuationOptions.ExecuteSynchronously);

        // First replenishment → 1 token → request A (oldest) is served
        limiter.TryReplenish();
        await taskA;

        Assert.That(completionOrder,     Has.Count.EqualTo(1));
        Assert.That(completionOrder[0],  Is.EqualTo(1),
            "OldestFirst: the first queued request must be served first.");

        // Second replenishment → 1 token → request B is served
        limiter.TryReplenish();
        await taskB;

        Assert.That(completionOrder,     Has.Count.EqualTo(2));
        Assert.That(completionOrder[1],  Is.EqualTo(2),
            "OldestFirst: the second queued request must be served second.");
    }
}
