namespace OrderProcessing.Orders.Domain;

/// <summary>
/// Order aggregate root. All status changes go through <see cref="ChangeStatus"/> or <see cref="Cancel"/>
/// so the rules in <see cref="OrderStatusTransitions"/> cannot be bypassed.
/// </summary>
public sealed class Order
{
    private readonly List<OrderItem> _items = [];

    // Required by EF Core.
    private Order()
    {
    }

    public Guid Id { get; private set; }

    public string CustomerId { get; private set; } = default!;

    public OrderStatus Status { get; private set; }

    /// <summary>Calculated from the items on the server; never accepted from the client.</summary>
    public decimal TotalAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token. Rotated on every change so concurrent writers are detected.</summary>
    public Guid Version { get; private set; }

    /// <summary>Lines in the order they were placed (EF does not guarantee load order, so sort by line number).</summary>
    public IReadOnlyCollection<OrderItem> Items => _items.OrderBy(i => i.LineNumber).ToList().AsReadOnly();

    public static Order Create(string customerId, IReadOnlyCollection<NewOrderItem> items, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new InvalidOrderException("Customer id is required.");
        }

        if (items.Count == 0)
        {
            throw new InvalidOrderException("An order must contain at least one item.");
        }

        if (items.Any(i => string.IsNullOrWhiteSpace(i.ProductId) || string.IsNullOrWhiteSpace(i.ProductName)))
        {
            throw new InvalidOrderException("Every item needs a product id and a product name.");
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new InvalidOrderException("Item quantity must be greater than zero.");
        }

        if (items.Any(i => i.UnitPrice <= 0))
        {
            throw new InvalidOrderException("Item unit price must be greater than zero.");
        }

        // Compare trimmed ids: they are stored trimmed, so "SKU-1" and "SKU-1 " are the same product.
        if (items.GroupBy(i => i.ProductId.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            throw new InvalidOrderException("Each product may appear only once per order; combine the quantities instead.");
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId.Trim(),
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            Version = Guid.NewGuid(),
        };

        var lineNumber = 0;
        foreach (var item in items)
        {
            order._items.Add(new OrderItem(
                ++lineNumber, item.ProductId.Trim(), item.ProductName.Trim(), item.Quantity, item.UnitPrice));
        }

        order.TotalAmount = order._items.Sum(i => i.LineTotal);
        return order;
    }

    public void ChangeStatus(OrderStatus newStatus, DateTimeOffset now)
    {
        if (!OrderStatusTransitions.CanTransition(Status, newStatus))
        {
            throw new InvalidOrderStateTransitionException(Id, Status, newStatus);
        }

        Status = newStatus;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    /// <summary>Customers may cancel only while the order is still PENDING.</summary>
    public void Cancel(DateTimeOffset now) => ChangeStatus(OrderStatus.Cancelled, now);
}
