using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Ordering.Domain;

/// <summary>
/// Aggregate root. Owns the order lifecycle: every status change goes through <see cref="TransitionTo"/>,
/// which enforces <see cref="AllowedTransitions"/> and appends to <see cref="History"/>.
/// </summary>
internal sealed class Order
{
    // The whole state machine in one place. See docs/architecture.md for the diagram.
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Rejected, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Rejected] = [],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Delivered] = []
    };

    private readonly List<OrderLine> _lines;
    private readonly List<StatusChange> _history = [];

    private Order(string customerId, Address shippingAddress, List<OrderLine> lines, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CustomerId = customerId;
        ShippingAddress = shippingAddress;
        _lines = lines;
        Status = OrderStatus.Pending;
        _history.Add(new StatusChange(OrderStatus.Pending, now, "Order received"));
    }

    public Guid Id { get; }
    public string CustomerId { get; }
    public Address ShippingAddress { get; }
    public OrderStatus Status { get; private set; }
    public OrderTotals? Totals { get; private set; }
    public string? PaymentAuthorizationId { get; private set; }
    public string? ShipmentId { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;
    public IReadOnlyList<StatusChange> History => _history;

    /// <summary>Cancellation is allowed only before shipment. Derived from the transition table, never duplicated.</summary>
    public bool CanBeCancelled => AllowedTransitions[Status].Contains(OrderStatus.Cancelled);

    public static Result<Order> Create(
        string customerId,
        Address shippingAddress,
        IReadOnlyList<OrderLine> lines,
        DateTimeOffset now)
    {
        if (lines.Count == 0)
            return OrderErrors.Invalid("order.no_lines", "An order needs at least one product.");

        if (lines.Any(l => string.IsNullOrWhiteSpace(l.Sku) || l.Quantity <= 0))
            return OrderErrors.Invalid("order.invalid_line", "Every line needs a SKU and a positive quantity.");

        if (lines.DistinctBy(l => l.Sku).Count() != lines.Count)
            return OrderErrors.Invalid("order.duplicate_sku", "Each product may appear only once.");

        return new Order(customerId, shippingAddress, [.. lines], now);
    }

    /// <summary>Prices are a snapshot: later catalogue or tax changes do not alter an accepted order.</summary>
    public void ApplyPricing(IReadOnlyDictionary<string, Money> unitPrices, OrderTotals totals)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Only a pending order can be priced.");

        for (var i = 0; i < _lines.Count; i++)
            _lines[i] = _lines[i] with { UnitPrice = unitPrices[_lines[i].Sku] };

        Totals = totals;
    }

    public Result Confirm(string paymentAuthorizationId, DateTimeOffset now)
    {
        if (Totals is null)
            return new Error(ErrorType.Conflict, "order.not_priced", "An order must be priced before it is confirmed.");

        var result = TransitionTo(OrderStatus.Confirmed, "Stock reserved and payment authorized", now);
        if (result.IsSuccess)
            PaymentAuthorizationId = paymentAuthorizationId;

        return result;
    }

    public Result Reject(string reason, DateTimeOffset now) => TransitionTo(OrderStatus.Rejected, reason, now);

    /// <summary>Permitted only before the order has shipped (enforced by the transition table).</summary>
    public Result Cancel(string reason, DateTimeOffset now) => TransitionTo(OrderStatus.Cancelled, reason, now);

    public void AttachShipment(string shipmentId) => ShipmentId = shipmentId;

    // Driven by carrier webhooks (Phase 2).
    public Result MarkShipped(DateTimeOffset now) => TransitionTo(OrderStatus.Shipped, "Handed to carrier", now);

    public Result MarkDelivered(DateTimeOffset now) => TransitionTo(OrderStatus.Delivered, "Delivered to customer", now);

    private Result TransitionTo(OrderStatus next, string reason, DateTimeOffset now)
    {
        if (!AllowedTransitions[Status].Contains(next))
            return OrderErrors.InvalidTransition(Status, next);

        Status = next;
        _history.Add(new StatusChange(next, now, reason));
        return Result.Success();
    }
}
