using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace SomeCompany.OrderProcessing.Ordering.Infrastructure;

/// <summary>Readiness: an instance that cannot reach Redis cannot guarantee idempotency, so it should get no traffic.</summary>
internal sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is unreachable.", ex);
        }
    }
}
