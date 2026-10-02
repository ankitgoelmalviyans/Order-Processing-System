namespace OrderProcessing.Orders.Application;

/// <summary>The requested order does not exist (maps to HTTP 404).</summary>
public sealed class OrderNotFoundException(Guid orderId) : Exception($"Order '{orderId}' was not found.")
{
    public Guid OrderId { get; } = orderId;
}

/// <summary>
/// The order was changed by someone else (e.g. the background job) between read and write
/// (maps to HTTP 409). Raised by the persistence layer when the optimistic concurrency token does not match.
/// </summary>
public sealed class ConcurrencyConflictException(Guid orderId, Exception? inner = null)
    : Exception($"Order '{orderId}' was modified by another request. Reload it and try again.", inner)
{
    public Guid OrderId { get; } = orderId;
}
