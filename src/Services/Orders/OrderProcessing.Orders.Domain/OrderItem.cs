namespace OrderProcessing.Orders.Domain;

/// <summary>A line on an order. Owned by <see cref="Order"/>; never changed after the order is placed.</summary>
public sealed class OrderItem
{
    // Required by EF Core.
    private OrderItem()
    {
    }

    internal OrderItem(int lineNumber, string productId, string productName, int quantity, decimal unitPrice)
    {
        Id = Guid.NewGuid();
        LineNumber = lineNumber;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; private set; }

    /// <summary>1-based position as submitted, so items are always returned in the order they were placed.</summary>
    public int LineNumber { get; private set; }

    public string ProductId { get; private set; } = default!;

    public string ProductName { get; private set; } = default!;

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;
}

/// <summary>Input for creating an order line; keeps the domain free of API DTOs.</summary>
public sealed record NewOrderItem(string ProductId, string ProductName, int Quantity, decimal UnitPrice);
