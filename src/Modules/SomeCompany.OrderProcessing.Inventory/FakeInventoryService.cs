using SomeCompany.OrderProcessing.Inventory.Contracts;
using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Inventory;

/// <summary>In-process stand-in for local development and demos. Used when Inventory:BaseUrl is not configured.</summary>
internal sealed class FakeInventoryService : IInventoryService
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, int> _stock = new() { ["SKU-KEYBOARD"] = 100, ["SKU-MOUSE"] = 100, ["SKU-MONITOR"] = 2 };
    private readonly Dictionary<Guid, IReadOnlyList<StockItem>> _reservations = [];

    public Task<Result> ReserveAsync(Guid orderId, IReadOnlyList<StockItem> items, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_reservations.ContainsKey(orderId))
                return Task.FromResult(Result.Success()); // idempotent retry

            if (items.Any(i => _stock.GetValueOrDefault(i.Sku) < i.Quantity))
                return Task.FromResult<Result>(InventoryErrors.InsufficientStock);

            foreach (var item in items)
                _stock[item.Sku] -= item.Quantity;

            _reservations[orderId] = items;
            return Task.FromResult(Result.Success());
        }
    }

    public Task<Result> ReleaseAsync(Guid orderId, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_reservations.Remove(orderId, out var items))
                foreach (var item in items)
                    _stock[item.Sku] += item.Quantity;

            return Task.FromResult(Result.Success());
        }
    }
}
