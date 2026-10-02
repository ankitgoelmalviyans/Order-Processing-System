using System.Net.Http.Json;

namespace OrderProcessing.StatusWorker;

public interface IOrdersApiClient
{
    /// <summary>Asks the Orders service to promote PENDING orders; returns how many were promoted.</summary>
    Task<int> PromotePendingAsync(string correlationId, CancellationToken cancellationToken);
}

/// <summary>
/// Typed HttpClient for the Orders service. The worker owns no data: the Orders service stays the only
/// writer to its database, and this client just triggers the use case over HTTP.
/// </summary>
internal sealed class OrdersApiClient(HttpClient http) : IOrdersApiClient
{
    public const string PromotePendingPath = "internal/jobs/promote-pending";

    public async Task<int> PromotePendingAsync(string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PromotePendingPath);
        request.Headers.Add("X-Correlation-Id", correlationId);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<PromotePendingResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Orders API returned an empty body.");
        return body.PromotedCount;
    }

    private sealed record PromotePendingResponse(int PromotedCount);
}
