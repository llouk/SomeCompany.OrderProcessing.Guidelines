using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Ordering.Contracts;

// Events Ordering will publish through the outbox (Phase 2, ADR-008). Contracts only: not published yet.
// They carry everything a consumer needs, so a consumer never has to call back into Ordering.

public sealed record OrderConfirmed(
    Guid MessageId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string CustomerId,
    Money Total,
    OrderShippingAddress ShippingAddress,
    IReadOnlyList<OrderItem> Items) : IIntegrationEvent;

public sealed record OrderRejected(
    Guid MessageId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string ReasonCode) : IIntegrationEvent;

public sealed record OrderCancelled(
    Guid MessageId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string? PaymentAuthorizationId,
    string? ShipmentId) : IIntegrationEvent;

public sealed record OrderShippingAddress(string Line1, string City, string PostalCode, string CountryCode);

public sealed record OrderItem(string Sku, int Quantity);
