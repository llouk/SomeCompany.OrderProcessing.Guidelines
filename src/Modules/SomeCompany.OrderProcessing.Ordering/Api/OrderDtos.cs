using SomeCompany.OrderProcessing.Ordering.Domain;
using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Ordering.Api;

// HTTP contract. Kept separate from the domain so the API can stay stable while the model evolves.
// Note: the request carries no prices. Prices always come from the Pricing module.

internal sealed record PlaceOrderRequest(AddressDto? ShippingAddress, List<OrderLineDto>? Lines, string? PaymentToken);

internal sealed record AddressDto(string Line1, string City, string PostalCode, string CountryCode);

internal sealed record OrderLineDto(string Sku, int Quantity);

internal sealed record CancelOrderRequest(string? Reason);

internal sealed record OrderResponse(
    Guid Id,
    string Status,
    bool CanCancel,
    IReadOnlyList<OrderLineResponse> Lines,
    TotalsResponse? Totals,
    IReadOnlyList<StatusChangeResponse> History)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.CanBeCancelled,
        [.. order.Lines.Select(l => new OrderLineResponse(l.Sku, l.Quantity, l.UnitPrice, l.LineTotal))],
        order.Totals is { } t
            ? new TotalsResponse(t.Subtotal, [.. t.Charges.Select(c => new ChargeResponse(c.Code, c.Amount))], t.Tax, t.Total)
            : null,
        [.. order.History.Select(h => new StatusChangeResponse(h.Status.ToString(), h.At, h.Reason))]);
}

internal sealed record OrderLineResponse(string Sku, int Quantity, Money? UnitPrice, Money? LineTotal);

internal sealed record TotalsResponse(Money Subtotal, IReadOnlyList<ChargeResponse> Charges, Money Tax, Money Total);

internal sealed record ChargeResponse(string Code, Money Amount);

internal sealed record StatusChangeResponse(string Status, DateTimeOffset At, string Reason);
