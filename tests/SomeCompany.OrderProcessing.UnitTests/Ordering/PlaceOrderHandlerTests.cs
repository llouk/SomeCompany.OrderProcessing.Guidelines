using Microsoft.Extensions.Logging.Abstractions;
using SomeCompany.OrderProcessing.Ordering.Application;
using SomeCompany.OrderProcessing.Ordering.Domain;
using SomeCompany.OrderProcessing.Ordering.Infrastructure;

namespace SomeCompany.OrderProcessing.UnitTests.Ordering;

/// <summary>Tests the placement orchestration against hand-written stubs (see TestDoubles.cs).</summary>
public class PlaceOrderHandlerTests
{
    private readonly StubInventory _inventory = new();
    private readonly StubPayments _payments = new();
    private readonly StubIdempotencyStore _idempotency = new();
    private readonly InMemoryOrderRepository _orders = new();

    [Fact]
    public async Task Happy_path_confirms_the_order()
    {
        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        var order = result.Value;
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(20m, order.Totals!.Total.Amount);
        Assert.NotNull(order.PaymentAuthorizationId);
        Assert.NotNull(order.ShipmentId);
    }

    [Fact]
    public async Task Insufficient_stock_rejects_without_charging()
    {
        _inventory.HasStock = false;

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, result.Value.Status);
        Assert.False(_payments.WasCalled);
        Assert.True(_inventory.Released); // outcome may be unknown after a timeout, so release is always called
    }

    [Fact]
    public async Task Declined_payment_rejects_and_releases_stock()
    {
        _payments.Approve = false;

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, result.Value.Status);
        Assert.True(_inventory.Released);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_same_order()
    {
        var handler = Handler();

        var first = await handler.HandleAsync(Command("key-1"), CancellationToken.None);
        var second = await handler.HandleAsync(Command("key-1"), CancellationToken.None);

        Assert.Equal(first.Value.Id, second.Value.Id);
    }

    [Fact]
    public async Task Same_key_while_first_request_runs_is_a_conflict()
    {
        await _idempotency.TryClaimAsync("customer-1", "key-1", CancellationToken.None); // first request still running

        var result = await Handler().HandleAsync(Command("key-1"), CancellationToken.None);

        Assert.Equal("order.request_in_progress", result.Error!.Code);
    }

    [Fact]
    public async Task Invalid_request_releases_the_key_so_the_client_can_retry()
    {
        var invalid = Command("key-1") with { Lines = [] };

        await Handler().HandleAsync(invalid, CancellationToken.None);
        var retry = await Handler().HandleAsync(Command("key-1"), CancellationToken.None);

        Assert.Equal(OrderStatus.Confirmed, retry.Value.Status);
    }

    private PlaceOrderHandler Handler() => new(
        _orders,
        _idempotency,
        new StubPricing(),
        _inventory,
        _payments,
        new StubShipping(),
        TimeProvider.System,
        NullLogger<PlaceOrderHandler>.Instance);

    private static PlaceOrderCommand Command(string? idempotencyKey = null) => new(
        "customer-1",
        idempotencyKey,
        new Address("1 Main St", "Athens", "10552", "GR"),
        [new OrderLine("SKU-1", 2)],
        "tok_test");
}
