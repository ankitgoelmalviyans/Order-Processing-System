namespace OrderProcessing.Orders.Domain;

/// <summary>
/// Lifecycle of an order. Serialized as SCREAMING_SNAKE_CASE strings on the API (e.g. "PENDING").
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    Processing = 1,
    Shipped = 2,
    Delivered = 3,

    /// <summary>Not in the original list, but needed to represent a cancelled order (only reachable from Pending).</summary>
    Cancelled = 4,
}
