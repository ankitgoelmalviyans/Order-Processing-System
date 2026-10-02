using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Application;

public interface IOrderService
{
    Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken);

    Task<OrderResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResponse<OrderResponse>> ListAsync(ListOrdersQuery query, CancellationToken cancellationToken);

    Task<OrderResponse> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken);

    Task<OrderResponse> CancelAsync(Guid id, CancellationToken cancellationToken);

    Task<PromotePendingResponse> PromotePendingAsync(CancellationToken cancellationToken);
}

/// <summary>Application use cases for orders. Validates input, applies domain rules, persists.</summary>
public sealed class OrderService(
    IOrderRepository repository,
    IValidator<CreateOrderRequest> createValidator,
    IValidator<UpdateOrderStatusRequest> updateStatusValidator,
    IValidator<ListOrdersQuery> listValidator,
    IOptions<OrderProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var items = request.Items
            .Select(i => new NewOrderItem(i.ProductId, i.ProductName, i.Quantity, i.UnitPrice))
            .ToList();
        var order = Order.Create(request.CustomerId, items, timeProvider.GetUtcNow());

        await repository.AddAsync(order, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Order {OrderId} created for customer {CustomerId} with {ItemCount} items, total {TotalAmount}",
            order.Id, order.CustomerId, order.Items.Count, order.TotalAmount);

        return OrderResponse.From(order);
    }

    public async Task<OrderResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(id, cancellationToken) ?? throw new OrderNotFoundException(id);
        return OrderResponse.From(order);
    }

    public async Task<PagedResponse<OrderResponse>> ListAsync(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        await listValidator.ValidateAndThrowAsync(query, cancellationToken);

        var (items, total) = await repository.ListAsync(
            query.Status, query.CustomerId, query.Page, query.PageSize, cancellationToken);

        return new PagedResponse<OrderResponse>(
            items.Select(OrderResponse.From).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<OrderResponse> UpdateStatusAsync(
        Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        await updateStatusValidator.ValidateAndThrowAsync(request, cancellationToken);

        var order = await repository.GetByIdAsync(id, cancellationToken) ?? throw new OrderNotFoundException(id);
        var previous = order.Status;

        order.ChangeStatus(request.Status!.Value, timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderId} status changed {From} -> {To}", id, previous, order.Status);
        return OrderResponse.From(order);
    }

    public async Task<OrderResponse> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(id, cancellationToken) ?? throw new OrderNotFoundException(id);

        order.Cancel(timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderId} cancelled", id);
        return OrderResponse.From(order);
    }

    public async Task<PromotePendingResponse> PromotePendingAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cutoff = now.AddSeconds(-Math.Max(0, options.Value.MinPendingAgeSeconds));

        var promoted = await repository.PromotePendingAsync(cutoff, now, cancellationToken);

        logger.LogInformation("Promoted {PromotedCount} PENDING orders to PROCESSING", promoted);
        return new PromotePendingResponse(promoted);
    }
}
