using SomeCompany.OrderProcessing.Ordering.Domain;
using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Ordering.Application;

internal sealed class GetOrderHandler(IOrderRepository orders)
{
    public async Task<Result<Order>> HandleAsync(Guid orderId, string customerId, CancellationToken ct)
    {
        var order = await orders.GetAsync(orderId, ct);

        // Another customer's order is reported as "not found" so order ids cannot be probed.
        if (order is null || order.CustomerId != customerId)
            return OrderErrors.NotFound(orderId);

        return order;
    }
}
