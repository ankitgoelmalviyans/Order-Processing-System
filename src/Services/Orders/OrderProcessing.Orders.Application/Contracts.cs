using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Application;

// Request / response contracts exposed by the Orders API. Kept separate from the domain model
// so the aggregate can evolve without breaking clients.

public sealed record CreateOrderRequest(string CustomerId, IReadOnlyList<CreateOrderItemRequest> Items);

public sealed record CreateOrderItemRequest(string ProductId, string ProductName, int Quantity, decimal UnitPrice);

// Nullable so a missing "status" is a validation error instead of silently binding to the enum default (Pending).
public sealed record UpdateOrderStatusRequest(OrderStatus? Status);

public sealed record ListOrdersQuery(OrderStatus? Status = null, string? CustomerId = null, int Page = 1, int PageSize = 20);

public sealed record OrderItemResponse(
    Guid Id,
    int LineNumber,
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    OrderStatus Status,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderItemResponse> Items)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status,
        order.TotalAmount,
        order.CreatedAt,
        order.UpdatedAt,
        order.Items
            .Select(i => new OrderItemResponse(i.Id, i.LineNumber, i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.LineTotal))
            .ToList());
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record PromotePendingResponse(int PromotedCount);
