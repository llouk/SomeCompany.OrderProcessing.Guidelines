using System.Collections.Concurrent;
using SomeCompany.OrderProcessing.Ordering.Application;
using SomeCompany.OrderProcessing.Ordering.Domain;

namespace SomeCompany.OrderProcessing.Ordering.Infrastructure;

/// <summary>
/// Ordering's in-memory database for the PoC. Data is lost on restart and not shared between instances.
/// During development it is replaced by EF Core on PostgreSQL or SQL Server, in Ordering's own schema (ADR-006).
/// </summary>
internal sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task<Order?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(_orders.GetValueOrDefault(id));

    public Task AddAsync(Order order, CancellationToken ct)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    // Same instance is held in memory, so nothing to do. With EF Core this is SaveChangesAsync.
    public Task UpdateAsync(Order order, CancellationToken ct) => Task.CompletedTask;
}
