namespace OrderProcessing.Orders.Domain;

/// <summary>
/// Single source of truth for which status changes are legal (a small state machine).
/// <code>
/// PENDING ──► PROCESSING ──► SHIPPED ──► DELIVERED
///    │
///    └──► CANCELLED
/// </code>
/// </summary>
public static class OrderStatusTransitions
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Allowed =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Pending] = [OrderStatus.Processing, OrderStatus.Cancelled],
            [OrderStatus.Processing] = [OrderStatus.Shipped],
            [OrderStatus.Shipped] = [OrderStatus.Delivered],
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = [],
        };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyCollection<OrderStatus> AllowedFrom(OrderStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];
}
