using SomeCompany.OrderProcessing.Ordering.Domain;
using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.UnitTests.Ordering;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Address Address = new("1 Main St", "Athens", "10552", "GR");

    [Fact]
    public void New_order_is_pending_with_history()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Single(order.History);
    }

    [Fact]
    public void Order_without_lines_is_rejected_at_creation()
    {
        var result = Order.Create("customer-1", Address, [], Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("order.no_lines", result.Error!.Code);
    }

    [Fact]
    public void Order_cannot_be_confirmed_before_pricing()
    {
        var order = NewOrder();

        var result = order.Confirm("auth-1", Now);

        Assert.Equal("order.not_priced", result.Error!.Code);
    }

    [Fact]
    public void Confirmed_order_can_be_cancelled()
    {
        var order = ConfirmedOrder();

        var result = order.Cancel("Changed my mind", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal([OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Cancelled], order.History.Select(h => h.Status));
    }

    [Fact]
    public void Shipped_order_cannot_be_cancelled()
    {
        var order = ConfirmedOrder();
        order.MarkShipped(Now);

        var result = order.Cancel("Too late", Now);

        Assert.Equal("order.invalid_transition", result.Error!.Code);
        Assert.Equal(OrderStatus.Shipped, order.Status);
    }

    [Theory]
    [InlineData(nameof(OrderStatus.Pending), true)]
    [InlineData(nameof(OrderStatus.Confirmed), true)]
    [InlineData(nameof(OrderStatus.Shipped), false)]
    [InlineData(nameof(OrderStatus.Rejected), false)]
    public void Cancellation_is_allowed_only_before_shipment(string status, bool expected)
    {
        var order = Enum.Parse<OrderStatus>(status) switch
        {
            OrderStatus.Pending => NewOrder(),
            OrderStatus.Confirmed => ConfirmedOrder(),
            OrderStatus.Shipped => Shipped(ConfirmedOrder()),
            _ => Rejected(NewOrder())
        };

        Assert.Equal(expected, order.CanBeCancelled);
    }

    [Fact]
    public void Rejected_order_is_final()
    {
        var order = NewOrder();
        order.Reject("Out of stock", Now);

        Assert.False(order.Confirm("auth-1", Now).IsSuccess);
        Assert.False(order.Cancel("x", Now).IsSuccess);
    }

    private static Order NewOrder() =>
        Order.Create("customer-1", Address, [new OrderLine("SKU-1", 2)], Now).Value;

    private static Order Shipped(Order order)
    {
        order.MarkShipped(Now);
        return order;
    }

    private static Order Rejected(Order order)
    {
        order.Reject("Out of stock", Now);
        return order;
    }

    private static Order ConfirmedOrder()
    {
        var order = NewOrder();
        var price = new Money(10m, "EUR");
        order.ApplyPricing(new Dictionary<string, Money> { ["SKU-1"] = price }, new OrderTotals(price, [], price, price));
        order.Confirm("auth-1", Now);
        return order;
    }
}
