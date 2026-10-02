using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Orders.Application;

namespace OrderProcessing.Orders.Api.Controllers;

/// <summary>
/// Endpoints for other services inside the cluster (called by the status worker).
/// The gateway does not route <c>/internal/**</c>, and this service publishes no host port.
/// </summary>
[ApiController]
[Route("internal/jobs")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalJobsController(IOrderService orders) : ControllerBase
{
    /// <summary>Promote every eligible PENDING order to PROCESSING. Idempotent; safe to retry.</summary>
    [HttpPost("promote-pending")]
    public Task<PromotePendingResponse> PromotePending(CancellationToken cancellationToken) =>
        orders.PromotePendingAsync(cancellationToken);
}
