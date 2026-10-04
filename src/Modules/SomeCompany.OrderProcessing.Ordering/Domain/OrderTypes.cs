using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Ordering.Domain;

internal enum OrderStatus
{
    Pending,
    Confirmed,
    Rejected,
    Cancelled,
    Shipped,
    Delivered
}

internal sealed record Address(string Line1, string City, string PostalCode, string CountryCode);

internal sealed record OrderLine(string Sku, int Quantity, Money? UnitPrice = null)
{
    public Money? LineTotal => UnitPrice?.Multiply(Quantity);
}

internal sealed record OrderCharge(string Code, Money Amount);

internal sealed record OrderTotals(Money Subtotal, IReadOnlyList<OrderCharge> Charges, Money Tax, Money Total);

internal sealed record StatusChange(OrderStatus Status, DateTimeOffset At, string Reason);

internal static class OrderErrors
{
    public static Error NotFound(Guid id) =>
        new(ErrorType.NotFound, "order.not_found", $"Order {id} was not found.");

    public static Error InvalidTransition(OrderStatus from, OrderStatus to) =>
        new(ErrorType.Conflict, "order.invalid_transition", $"An order cannot move from {from} to {to}.");

    public static readonly Error RequestInProgress =
        new(ErrorType.Conflict, "order.request_in_progress", "A request with this Idempotency-Key is still being processed. Retry shortly.");

    public static Error Invalid(string code, string message) => new(ErrorType.Validation, code, message);
}
