namespace OrderProcessing.Orders.Domain;

/// <summary>Base type for business-rule violations raised by the domain model.</summary>
public abstract class OrderDomainException(string message) : Exception(message);

/// <summary>An order could not be built because its data breaks an invariant (maps to HTTP 400).</summary>
public sealed class InvalidOrderException(string message) : OrderDomainException(message);

/// <summary>A status change is not allowed by <see cref="OrderStatusTransitions"/> (maps to HTTP 409).</summary>
public sealed class InvalidOrderStateTransitionException(Guid orderId, OrderStatus from, OrderStatus to)
    : OrderDomainException($"Order '{orderId}' cannot move from {from} to {to}.")
{
    public Guid OrderId { get; } = orderId;
    public OrderStatus From { get; } = from;
    public OrderStatus To { get; } = to;
}
