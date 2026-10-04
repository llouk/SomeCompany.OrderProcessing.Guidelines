using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Shipping.Contracts;

// Events the Shipping module publishes after translating a verified carrier webhook (Phase 2, ADR-008).
// Contracts only: not published yet. Ordering subscribes and moves the order to Shipped / Delivered.
// Shipping owns these events, so the dependency stays one-way: Ordering → Shipping.Contracts.

public sealed record ShipmentDispatched(
    Guid MessageId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string ShipmentId,
    string? TrackingUrl) : IIntegrationEvent;

public sealed record ShipmentDelivered(
    Guid MessageId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string ShipmentId) : IIntegrationEvent;
