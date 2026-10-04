using Microsoft.Extensions.Logging;
using SomeCompany.OrderProcessing.Inventory.Contracts;
using SomeCompany.OrderProcessing.Ordering.Domain;
using SomeCompany.OrderProcessing.Payments.Contracts;
using SomeCompany.OrderProcessing.SharedKernel;
using SomeCompany.OrderProcessing.Shipping.Contracts;

namespace SomeCompany.OrderProcessing.Ordering.Application;

internal sealed record CancelOrderCommand(Guid OrderId, string CustomerId, string? Reason);

internal sealed class CancelOrderHandler(
    IOrderRepository orders,
    IInventoryService inventory,
    IPaymentService payments,
    IShippingService shipping,
    TimeProvider clock,
    ILogger<CancelOrderHandler> logger)
{
    public async Task<Result<Order>> HandleAsync(CancelOrderCommand command, CancellationToken ct)
    {
        var order = await orders.GetAsync(command.OrderId, ct);
        if (order is null || order.CustomerId != command.CustomerId)
            return OrderErrors.NotFound(command.OrderId);

        // The aggregate decides whether cancellation is permitted.
        var cancelled = order.Cancel(command.Reason ?? "Cancelled by customer", clock.GetUtcNow());
        if (!cancelled.IsSuccess)
            return cancelled.Error!;

        await orders.UpdateAsync(order, ct);

        // Undo side effects. Best effort for now; Phase 2 moves these behind the outbox so failures are retried.
        var noCancel = CancellationToken.None;
        if (order.ShipmentId is not null)
            LogIfFailed(order, "shipping.cancel", await shipping.CancelShipmentAsync(order.ShipmentId, noCancel));
        if (order.PaymentAuthorizationId is not null)
            LogIfFailed(order, "payments.void", await payments.VoidAsync(order.PaymentAuthorizationId, noCancel));
        LogIfFailed(order, "inventory.release", await inventory.ReleaseAsync(order.Id, noCancel));

        return order;
    }

    private void LogIfFailed(Order order, string step, Result result)
    {
        if (!result.IsSuccess)
            logger.LogError("Compensation {Step} failed for cancelled order {OrderId}: {ErrorCode}", step, order.Id, result.Error!.Code);
    }
}
