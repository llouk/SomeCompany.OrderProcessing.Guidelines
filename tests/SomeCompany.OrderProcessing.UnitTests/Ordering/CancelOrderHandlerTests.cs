using Microsoft.Extensions.Logging.Abstractions;
using SomeCompany.OrderProcessing.Ordering.Application;
using SomeCompany.OrderProcessing.Ordering.Domain;
using SomeCompany.OrderProcessing.Ordering.Infrastructure;

namespace SomeCompany.OrderProcessing.UnitTests.Ordering;

public class CancelOrderHandlerTests
{
    private readonly InMemoryOrderRepository _orders = new();
    private readonly StubInventory _inventory = new();
    private readonly StubPayments _payments = new();
    private readonly StubShipping _shipping = new();

    [Fact]
    public async Task Cancelling_a_confirmed_order_undoes_every_side_effect()
    {
        var order = await PlaceAsync();

        var result = await CancelHandler().HandleAsync(new CancelOrderCommand(order.Id, "customer-1", null), CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, result.Value.Status);
        Assert.True(_shipping.ShipmentCancelled);
        Assert.True(_payments.Voided);
        Assert.True(_inventory.Released);
    }

    [Fact]
    public async Task Another_customers_order_is_not_found()
    {
        var order = await PlaceAsync();

        var result = await CancelHandler().HandleAsync(new CancelOrderCommand(order.Id, "customer-2", null), CancellationToken.None);

        Assert.Equal("order.not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Cancelling_twice_is_a_conflict()
    {
        var order = await PlaceAsync();
        await CancelHandler().HandleAsync(new CancelOrderCommand(order.Id, "customer-1", null), CancellationToken.None);

        var second = await CancelHandler().HandleAsync(new CancelOrderCommand(order.Id, "customer-1", null), CancellationToken.None);

        Assert.Equal("order.invalid_transition", second.Error!.Code);
    }

    [Fact]
    public async Task Cancel_during_placement_wins_and_the_payment_is_voided()
    {
        // The customer cancels the Pending order while its payment is being authorized.
        _payments.DuringAuthorization = orderId =>
            CancelHandler().HandleAsync(new CancelOrderCommand(orderId, "customer-1", null), CancellationToken.None);

        var order = await PlaceAsync();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.True(_payments.Voided);
        Assert.Null(order.ShipmentId);
    }

    private async Task<Order> PlaceAsync()
    {
        var handler = new PlaceOrderHandler(
            _orders, new StubIdempotencyStore(), new StubPricing(), _inventory, _payments, _shipping,
            TimeProvider.System, NullLogger<PlaceOrderHandler>.Instance);

        var result = await handler.HandleAsync(
            new PlaceOrderCommand("customer-1", null, new Address("1 Main St", "Athens", "10552", "GR"), [new OrderLine("SKU-1", 1)], "tok"),
            CancellationToken.None);

        return result.Value;
    }

    private CancelOrderHandler CancelHandler() =>
        new(_orders, _inventory, _payments, _shipping, TimeProvider.System, NullLogger<CancelOrderHandler>.Instance);
}
