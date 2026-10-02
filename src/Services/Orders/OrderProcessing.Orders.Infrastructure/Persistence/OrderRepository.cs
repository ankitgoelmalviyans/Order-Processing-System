using Microsoft.EntityFrameworkCore;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Infrastructure.Persistence;

internal sealed class OrderRepository(OrdersDbContext db) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken cancellationToken) =>
        await db.Orders.AddAsync(order, cancellationToken);

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> ListAsync(
        OrderStatus? status,
        string? customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = db.Orders.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            var trimmed = customerId.Trim();
            query = query.Where(o => o.CustomerId == trimmed);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id) // stable ordering so pages never overlap
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<int> PromotePendingAsync(
        DateTimeOffset createdAtOrBefore, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // One set-based UPDATE ... WHERE status = 'Pending': atomic and idempotent, so overlapping job runs
        // (or several worker replicas) cannot double-process. Rotating the version makes any request that
        // loaded the order before this ran (e.g. a concurrent cancel) fail with a concurrency conflict.
        var newVersion = Guid.NewGuid();
        return db.Orders
            .Where(o => o.Status == OrderStatus.Pending && o.CreatedAt <= createdAtOrBefore)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.Status, OrderStatus.Processing)
                    .SetProperty(o => o.UpdatedAt, now)
                    .SetProperty(o => o.Version, newVersion),
                cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var orderId = ex.Entries.Select(e => e.Entity).OfType<Order>().FirstOrDefault()?.Id ?? Guid.Empty;
            throw new ConcurrencyConflictException(orderId, ex);
        }
    }
}
