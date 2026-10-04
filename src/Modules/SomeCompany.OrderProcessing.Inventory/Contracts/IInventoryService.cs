using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Inventory.Contracts;

/// <summary>
/// Public API of the Inventory module (an anti-corruption layer over the external inventory system).
/// Availability is validated by <em>reserving</em> stock, not by a separate check: a check followed by a
/// later reservation is a race. Calls are keyed by orderId so the external system can deduplicate retries.
/// </summary>
public interface IInventoryService
{
    Task<Result> ReserveAsync(Guid orderId, IReadOnlyList<StockItem> items, CancellationToken ct);

    Task<Result> ReleaseAsync(Guid orderId, CancellationToken ct);
}

public sealed record StockItem(string Sku, int Quantity);
