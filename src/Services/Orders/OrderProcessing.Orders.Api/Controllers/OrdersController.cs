using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public sealed class OrdersController(IOrderService orders) : ControllerBase
{
    /// <summary>Place a new order with one or more items. The order starts as PENDING.</summary>
    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponse>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await orders.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    /// <summary>Get an order and its items by id.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<OrderResponse> GetById(Guid id, CancellationToken cancellationToken) =>
        orders.GetByIdAsync(id, cancellationToken);

    /// <summary>List orders, newest first, optionally filtered by status and/or customer.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<OrderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResponse<OrderResponse>> List(
        [FromQuery] OrderStatus? status,
        [FromQuery] string? customerId,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        orders.ListAsync(new ListOrdersQuery(status, customerId, page, pageSize), cancellationToken);

    /// <summary>Move an order to its next status (PENDING → PROCESSING → SHIPPED → DELIVERED).</summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<OrderResponse> UpdateStatus(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken) =>
        orders.UpdateStatusAsync(id, request, cancellationToken);

    /// <summary>Cancel an order. Only allowed while it is still PENDING.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<OrderResponse> Cancel(Guid id, CancellationToken cancellationToken) =>
        orders.CancelAsync(id, cancellationToken);
}
