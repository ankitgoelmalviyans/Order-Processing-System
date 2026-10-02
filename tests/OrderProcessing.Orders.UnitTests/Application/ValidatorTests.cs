using FluentValidation.TestHelper;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.UnitTests.Application;

public class ValidatorTests
{
    private readonly CreateOrderRequestValidator _create = new();
    private readonly ListOrdersQueryValidator _list = new();
    private readonly UpdateOrderStatusRequestValidator _update = new();

    private static CreateOrderItemRequest Item(string productId = "SKU-1", int quantity = 1, decimal unitPrice = 9.99m) =>
        new(productId, "Product", quantity, unitPrice);

    [Fact]
    public void Valid_order_passes()
    {
        _create.TestValidate(new CreateOrderRequest("cust-1", [Item("A"), Item("B", 3, 0.01m)]))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Too_many_items_fails()
    {
        var items = Enumerable.Range(0, CreateOrderRequestValidator.MaxItemsPerOrder + 1).Select(i => Item($"SKU-{i}")).ToList();

        _create.TestValidate(new CreateOrderRequest("cust-1", items)).ShouldHaveValidationErrorFor(r => r.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CreateOrderRequestValidator.MaxQuantityPerItem + 1)]
    public void Quantity_out_of_range_fails(int quantity)
    {
        _create.TestValidate(new CreateOrderRequest("cust-1", [Item(quantity: quantity)]))
            .ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("10.555")]
    public void Price_with_more_than_two_decimals_fails(string price)
    {
        _create.TestValidate(new CreateOrderRequest("cust-1", [Item(unitPrice: decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture))]))
            .ShouldHaveValidationErrorFor("Items[0].UnitPrice");
    }

    [Fact]
    public void Duplicate_products_ignoring_case_and_whitespace_fail()
    {
        _create.TestValidate(new CreateOrderRequest("cust-1", [Item("sku-1"), Item(" SKU-1 ")]))
            .ShouldHaveValidationErrorFor(r => r.Items);
    }

    [Fact]
    public void Missing_status_on_update_fails()
    {
        _update.TestValidate(new UpdateOrderStatusRequest(null)).ShouldHaveValidationErrorFor(r => r.Status);
    }

    [Fact]
    public void Undefined_status_value_on_list_fails()
    {
        _list.TestValidate(new ListOrdersQuery(Status: (OrderStatus)42)).ShouldHaveValidationErrorFor(q => q.Status);
    }
}
