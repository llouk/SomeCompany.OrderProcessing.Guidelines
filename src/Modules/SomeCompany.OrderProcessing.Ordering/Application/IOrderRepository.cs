using SomeCompany.OrderProcessing.Ordering.Domain;

namespace SomeCompany.OrderProcessing.Ordering.Application;

/// <summary>Ordering's persistence port. In-memory today; PostgreSQL or SQL Server via EF Core next (ADR-006).</summary>
internal interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken ct);

    Task AddAsync(Order order, CancellationToken ct);

    Task UpdateAsync(Order order, CancellationToken ct);
}
