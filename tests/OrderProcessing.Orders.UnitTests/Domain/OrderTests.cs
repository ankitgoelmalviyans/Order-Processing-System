using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.UnitTests.Domain;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static NewOrderItem Item(string productId = "SKU-1", int quantity = 1, decimal unitPrice = 10m) =>
        new(productId, $"Product {productId}", quantity, unitPrice);

    [Fact]
    public void Create_with_multiple_items_starts_pending_and_computes_total()
    {
        var order = Order.Create("cust-1", [Item("SKU-1", 2, 10.50m), Item("SKU-2", 3, 1.25m)], Now);

        order.Status.Should().Be(OrderStatus.Pending);
        order.Items.Should().HaveCount(2);
        order.TotalAmount.Should().Be(24.75m);
        order.CreatedAt.Should().Be(Now);
        order.UpdatedAt.Should().Be(Now);
        order.Id.Should().NotBeEmpty();
        order.Version.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_without_items_is_rejected()
    {
        var act = () => Order.Create("cust-1", [], Now);

        act.Should().Throw<InvalidOrderException>().WithMessage("*at least one item*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_without_customer_is_rejected(string customerId)
    {
        var act = () => Order.Create(customerId, [Item()], Now);

        act.Should().Throw<InvalidOrderException>();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    public void Create_with_non_positive_quantity_or_price_is_rejected(int quantity, decimal unitPrice)
    {
        var act = () => Order.Create("cust-1", [Item(quantity: quantity, unitPrice: unitPrice)], Now);

        act.Should().Throw<InvalidOrderException>();
    }

    [Fact]
    public void Create_with_duplicate_product_ids_is_rejected_case_insensitively()
    {
        var act = () => Order.Create("cust-1", [Item("sku-1"), Item("SKU-1")], Now);

        act.Should().Throw<InvalidOrderException>().WithMessage("*only once*");
    }

    [Fact]
    public void Create_with_duplicate_product_ids_differing_only_by_whitespace_is_rejected()
    {
        // Review finding F8: the domain grouped untrimmed ids but stored trimmed ones.
        var act = () => Order.Create("cust-1", [Item("SKU-1"), Item("SKU-1 ")], Now);

        act.Should().Throw<InvalidOrderException>();
    }

    [Fact]
    public void Create_numbers_lines_in_submission_order()
    {
        var order = Order.Create("cust-1", [Item("C"), Item("A"), Item("B")], Now);

        order.Items.Select(i => (i.LineNumber, i.ProductId)).Should().Equal((1, "C"), (2, "A"), (3, "B"));
    }

    [Fact]
    public void Cancel_pending_order_succeeds_and_rotates_version()
    {
        var order = Order.Create("cust-1", [Item()], Now);
        var originalVersion = order.Version;
        var later = Now.AddMinutes(1);

        order.Cancel(later);

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.UpdatedAt.Should().Be(later);
        order.Version.Should().NotBe(originalVersion);
    }

    [Theory]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void Cancel_is_rejected_once_order_has_left_pending(OrderStatus reachedStatus)
    {
        var order = OrderIn(reachedStatus);

        var act = () => order.Cancel(Now);

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.From.Should().Be(reachedStatus);
        order.Status.Should().Be(reachedStatus);
    }

    [Fact]
    public void Full_happy_path_reaches_delivered()
    {
        var order = Order.Create("cust-1", [Item()], Now);

        order.ChangeStatus(OrderStatus.Processing, Now);
        order.ChangeStatus(OrderStatus.Shipped, Now);
        order.ChangeStatus(OrderStatus.Delivered, Now);

        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public void Skipping_a_status_is_rejected_and_leaves_order_unchanged()
    {
        var order = Order.Create("cust-1", [Item()], Now);
        var version = order.Version;

        var act = () => order.ChangeStatus(OrderStatus.Delivered, Now.AddMinutes(1));

        act.Should().Throw<InvalidOrderStateTransitionException>();
        order.Status.Should().Be(OrderStatus.Pending);
        order.Version.Should().Be(version);
        order.UpdatedAt.Should().Be(Now);
    }

    /// <summary>Builds an order and walks it through legal transitions to reach <paramref name="target"/>.</summary>
    internal static Order OrderIn(OrderStatus target)
    {
        var order = Order.Create("cust-1", [Item()], Now);
        OrderStatus[] path = target switch
        {
            OrderStatus.Pending => [],
            OrderStatus.Processing => [OrderStatus.Processing],
            OrderStatus.Shipped => [OrderStatus.Processing, OrderStatus.Shipped],
            OrderStatus.Delivered => [OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Delivered],
            OrderStatus.Cancelled => [OrderStatus.Cancelled],
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

        foreach (var status in path)
        {
            order.ChangeStatus(status, Now);
        }

        return order;
    }
}
