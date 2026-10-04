using Microsoft.Extensions.Logging;
using SomeCompany.OrderProcessing.Inventory.Contracts;
using SomeCompany.OrderProcessing.Ordering.Domain;
using SomeCompany.OrderProcessing.Payments.Contracts;
using SomeCompany.OrderProcessing.Pricing.Contracts;
using SomeCompany.OrderProcessing.SharedKernel;
using SomeCompany.OrderProcessing.Shipping.Contracts;

namespace SomeCompany.OrderProcessing.Ordering.Application;

internal sealed record PlaceOrderCommand(
    string CustomerId,
    string? IdempotencyKey,
    Address ShippingAddress,
    IReadOnlyList<OrderLine> Lines,
    string PaymentToken);

/// <summary>
/// Orchestrates order placement: claim idempotency key → price → persist (Pending) → reserve stock → authorize payment
/// → confirm → request shipment. A failed step compensates the earlier ones and the order ends as Rejected with the
/// reason in its history. Synchronous by design for v1. Known gaps and the path to an outbox are in ADR-004.
/// </summary>
internal sealed class PlaceOrderHandler(
    IOrderRepository orders,
    IIdempotencyStore idempotency,
    IPricingService pricing,
    IInventoryService inventory,
    IPaymentService payments,
    IShippingService shipping,
    TimeProvider clock,
    ILogger<PlaceOrderHandler> logger)
{
    public async Task<Result<Order>> HandleAsync(PlaceOrderCommand command, CancellationToken ct)
    {
        var idempotencyKey = command.IdempotencyKey;

        // Idempotency (ADR-005): only one request per key does the work; retries get the original order.
        if (idempotencyKey is not null)
        {
            var claim = await idempotency.TryClaimAsync(command.CustomerId, idempotencyKey, ct);

            if (claim.Status == IdempotencyClaimStatus.InProgress)
                return OrderErrors.RequestInProgress;

            if (claim.Status == IdempotencyClaimStatus.Completed)
                return await orders.GetAsync(claim.OrderId!.Value, ct) is { } existing
                    ? existing
                    : OrderErrors.NotFound(claim.OrderId.Value);
        }

        var priced = await CreatePricedOrderAsync(command, ct);
        if (!priced.IsSuccess)
        {
            // Nothing was created, so free the key: the client may fix the request and retry with it.
            if (idempotencyKey is not null)
                await idempotency.ReleaseAsync(command.CustomerId, idempotencyKey, ct);

            return priced.Error!;
        }

        var order = priced.Value;
        await orders.AddAsync(order, ct);

        // From here on the order exists, so we finish the workflow even if the client disconnects.
        var noCancel = CancellationToken.None;

        if (idempotencyKey is not null)
            await idempotency.CompleteAsync(command.CustomerId, idempotencyKey, order.Id, noCancel);

        var reserved = await inventory.ReserveAsync(order.Id, [.. order.Lines.Select(l => new StockItem(l.Sku, l.Quantity))], noCancel);
        if (!reserved.IsSuccess)
        {
            // The outcome may be unknown (e.g. timeout after the provider reserved), so always release. Release is idempotent.
            await inventory.ReleaseAsync(order.Id, noCancel);
            return await RejectAsync(order, reserved.Error!);
        }

        var authorization = await payments.AuthorizeAsync(order.Id, order.Totals!.Total, command.PaymentToken, noCancel);
        if (!authorization.IsSuccess)
        {
            await inventory.ReleaseAsync(order.Id, noCancel);
            return await RejectAsync(order, authorization.Error!);
        }

        if (!order.Confirm(authorization.Value, clock.GetUtcNow()).IsSuccess)
        {
            // The order changed meanwhile (e.g. the customer cancelled it): undo what this request did.
            await payments.VoidAsync(authorization.Value, noCancel);
            await inventory.ReleaseAsync(order.Id, noCancel);
            return order;
        }

        await orders.UpdateAsync(order, noCancel);
        logger.LogInformation("Order {OrderId} confirmed for {Total}", order.Id, order.Totals.Total);

        await RequestShipmentAsync(order);
        return order;
    }

    private async Task<Result<Order>> CreatePricedOrderAsync(PlaceOrderCommand command, CancellationToken ct)
    {
        var created = Order.Create(command.CustomerId, command.ShippingAddress, command.Lines, clock.GetUtcNow());
        if (!created.IsSuccess)
            return created.Error!;

        var order = created.Value;

        var quote = await pricing.QuoteAsync(
            new PriceQuoteRequest(order.ShippingAddress.CountryCode, [.. order.Lines.Select(l => new PriceQuoteLine(l.Sku, l.Quantity))]),
            ct);
        if (!quote.IsSuccess)
            return quote.Error!;

        order.ApplyPricing(
            quote.Value.Lines.ToDictionary(l => l.Sku, l => l.UnitPrice),
            new OrderTotals(
                quote.Value.Subtotal,
                [.. quote.Value.Charges.Select(c => new OrderCharge(c.Code, c.Amount))],
                quote.Value.Tax,
                quote.Value.Total));

        return order;
    }

    private async Task<Result<Order>> RejectAsync(Order order, Error reason)
    {
        order.Reject(reason.Message, clock.GetUtcNow());
        await orders.UpdateAsync(order, CancellationToken.None);
        logger.LogWarning("Order {OrderId} rejected: {ErrorCode}", order.Id, reason.Code);

        // A rejected order is a valid outcome, not an API error: the client reads the status and reason.
        return order;
    }

    private async Task RequestShipmentAsync(Order order)
    {
        var address = order.ShippingAddress;
        var shipment = await shipping.CreateShipmentAsync(
            new ShipmentRequest(
                order.Id,
                new ShippingAddress(address.Line1, address.City, address.PostalCode, address.CountryCode),
                [.. order.Lines.Select(l => new ShipmentItem(l.Sku, l.Quantity))]),
            CancellationToken.None);

        if (!shipment.IsSuccess)
        {
            // Known gap (ADR-004): no automatic retry yet. Phase 2 moves this call behind the outbox.
            logger.LogError("Shipment request failed for order {OrderId}: {ErrorCode}", order.Id, shipment.Error!.Code);
            return;
        }

        order.AttachShipment(shipment.Value);
        await orders.UpdateAsync(order, CancellationToken.None);
    }
}
