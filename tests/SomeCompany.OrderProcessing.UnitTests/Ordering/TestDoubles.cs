using SomeCompany.OrderProcessing.Inventory.Contracts;
using SomeCompany.OrderProcessing.Ordering.Application;
using SomeCompany.OrderProcessing.Payments.Contracts;
using SomeCompany.OrderProcessing.Pricing.Contracts;
using SomeCompany.OrderProcessing.SharedKernel;
using SomeCompany.OrderProcessing.Shipping.Contracts;

namespace SomeCompany.OrderProcessing.UnitTests.Ordering;

// Hand-written stubs of the other modules' contracts. No mocking library needed: the contracts are small on purpose.

internal sealed class StubPricing : IPricingService
{
    public Task<Result<PriceQuote>> QuoteAsync(PriceQuoteRequest request, CancellationToken ct)
    {
        var lines = request.Lines.Select(l => new PricedLine(l.Sku, l.Quantity, Eur(10m), Eur(10m * l.Quantity))).ToList();
        var subtotal = Eur(lines.Sum(l => l.LineTotal.Amount));
        return Task.FromResult<Result<PriceQuote>>(new PriceQuote(lines, subtotal, [], Eur(0m), subtotal));
    }

    private static Money Eur(decimal amount) => new(amount, "EUR");
}

internal sealed class StubInventory : IInventoryService
{
    public bool HasStock { get; set; } = true;
    public bool Released { get; private set; }

    public Task<Result> ReserveAsync(Guid orderId, IReadOnlyList<StockItem> items, CancellationToken ct) =>
        Task.FromResult(HasStock
            ? Result.Success()
            : Result.Failure(new Error(ErrorType.Conflict, "inventory.insufficient_stock", "Out of stock.")));

    public Task<Result> ReleaseAsync(Guid orderId, CancellationToken ct)
    {
        Released = true;
        return Task.FromResult(Result.Success());
    }
}

internal sealed class StubPayments : IPaymentService
{
    public bool Approve { get; set; } = true;
    public bool WasCalled { get; private set; }
    public bool Voided { get; private set; }

    /// <summary>Lets a test act while authorization is "in flight", e.g. a concurrent cancellation.</summary>
    public Func<Guid, Task>? DuringAuthorization { get; set; }

    public async Task<Result<string>> AuthorizeAsync(Guid orderId, Money amount, string paymentToken, CancellationToken ct)
    {
        WasCalled = true;
        if (DuringAuthorization is not null)
            await DuringAuthorization(orderId);

        return Approve ? "auth-1" : new Error(ErrorType.Conflict, "payments.declined", "Declined.");
    }

    public Task<Result> CaptureAsync(string authorizationId, Money amount, CancellationToken ct) => Task.FromResult(Result.Success());

    public Task<Result> VoidAsync(string authorizationId, CancellationToken ct)
    {
        Voided = true;
        return Task.FromResult(Result.Success());
    }
}

internal sealed class StubShipping : IShippingService
{
    public bool ShipmentCancelled { get; private set; }

    public Task<Result<string>> CreateShipmentAsync(ShipmentRequest request, CancellationToken ct) =>
        Task.FromResult<Result<string>>("shp-1");

    public Task<Result> CancelShipmentAsync(string shipmentId, CancellationToken ct)
    {
        ShipmentCancelled = true;
        return Task.FromResult(Result.Success());
    }
}

/// <summary>Same semantics as the Redis store, without Redis.</summary>
internal sealed class StubIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<string, Guid?> _keys = [];

    public Task<IdempotencyClaim> TryClaimAsync(string customerId, string key, CancellationToken ct)
    {
        if (_keys.TryAdd($"{customerId}:{key}", null))
            return Task.FromResult(new IdempotencyClaim(IdempotencyClaimStatus.Claimed));

        var orderId = _keys[$"{customerId}:{key}"];
        return Task.FromResult(orderId is null
            ? new IdempotencyClaim(IdempotencyClaimStatus.InProgress)
            : new IdempotencyClaim(IdempotencyClaimStatus.Completed, orderId));
    }

    public Task CompleteAsync(string customerId, string key, Guid orderId, CancellationToken ct)
    {
        _keys[$"{customerId}:{key}"] = orderId;
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(string customerId, string key, CancellationToken ct)
    {
        _keys.Remove($"{customerId}:{key}");
        return Task.CompletedTask;
    }
}
