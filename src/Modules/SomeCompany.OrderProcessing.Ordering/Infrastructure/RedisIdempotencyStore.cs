using SomeCompany.OrderProcessing.Ordering.Application;
using StackExchange.Redis;

namespace SomeCompany.OrderProcessing.Ordering.Infrastructure;

/// <summary>
/// One Redis string per key: "in-progress" while the first request runs, then the order id.
/// SET NX makes the claim atomic across all API instances.
/// </summary>
internal sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    private const string InProgress = "in-progress";

    // A claim that never completes (process crash) frees itself quickly; a completed key covers client retries for a day.
    private static readonly TimeSpan InProgressTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CompletedTtl = TimeSpan.FromHours(24);

    public async Task<IdempotencyClaim> TryClaimAsync(string customerId, string key, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var redisKey = ToRedisKey(customerId, key);

        if (await db.StringSetAsync(redisKey, InProgress, InProgressTtl, When.NotExists))
            return new IdempotencyClaim(IdempotencyClaimStatus.Claimed);

        var value = await db.StringGetAsync(redisKey);
        return Guid.TryParse(value.ToString(), out var orderId)
            ? new IdempotencyClaim(IdempotencyClaimStatus.Completed, orderId)
            : new IdempotencyClaim(IdempotencyClaimStatus.InProgress);
    }

    public Task CompleteAsync(string customerId, string key, Guid orderId, CancellationToken ct) =>
        redis.GetDatabase().StringSetAsync(ToRedisKey(customerId, key), orderId.ToString(), CompletedTtl);

    public Task ReleaseAsync(string customerId, string key, CancellationToken ct) =>
        redis.GetDatabase().KeyDeleteAsync(ToRedisKey(customerId, key));

    private static RedisKey ToRedisKey(string customerId, string key) => $"ordering:idempotency:{customerId}:{key}";
}
