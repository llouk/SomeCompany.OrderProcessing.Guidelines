namespace SomeCompany.OrderProcessing.Ordering.Application;

/// <summary>
/// Remembers which order an Idempotency-Key produced, so a retried POST returns the original order instead of
/// creating a duplicate. Shared by all API instances (Redis, ADR-005). Keys are scoped per customer.
/// </summary>
internal interface IIdempotencyStore
{
    /// <summary>Atomically claims the key, or reports what an earlier request did with it.</summary>
    Task<IdempotencyClaim> TryClaimAsync(string customerId, string key, CancellationToken ct);

    /// <summary>Links the key to the created order. From now on, retries return that order.</summary>
    Task CompleteAsync(string customerId, string key, Guid orderId, CancellationToken ct);

    /// <summary>Frees the key when the request failed before an order was created.</summary>
    Task ReleaseAsync(string customerId, string key, CancellationToken ct);
}

internal enum IdempotencyClaimStatus
{
    /// <summary>This request owns the key and must do the work.</summary>
    Claimed,

    /// <summary>Another request with the same key is still running.</summary>
    InProgress,

    /// <summary>An earlier request already created <see cref="IdempotencyClaim.OrderId"/>.</summary>
    Completed
}

internal sealed record IdempotencyClaim(IdempotencyClaimStatus Status, Guid? OrderId = null);
