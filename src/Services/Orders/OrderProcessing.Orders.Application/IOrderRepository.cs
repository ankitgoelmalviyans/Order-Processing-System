using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Application;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Order> Items, int TotalCount)> ListAsync(
        OrderStatus? status,
        string? customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves every PENDING order created at or before <paramref name="createdAtOrBefore"/> to PROCESSING
    /// in a single atomic statement. Returns the number of orders promoted.
    /// </summary>
    Task<int> PromotePendingAsync(DateTimeOffset createdAtOrBefore, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Persists tracked changes. Throws <see cref="ConcurrencyConflictException"/> on a lost update.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
