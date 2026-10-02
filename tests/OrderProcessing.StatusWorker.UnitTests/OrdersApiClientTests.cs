using System.Net;
using System.Text;

namespace OrderProcessing.StatusWorker.UnitTests;

public sealed class OrdersApiClientTests
{
    [Fact]
    public async Task Posts_to_the_internal_endpoint_with_correlation_id_and_parses_count()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"promotedCount":7}""");
        var client = new OrdersApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://orders-api:8080") });

        var promoted = await client.PromotePendingAsync("corr-1", CancellationToken.None);

        promoted.Should().Be(7);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri.Should().Be(new Uri("http://orders-api:8080/internal/jobs/promote-pending"));
        handler.LastRequest.Headers.GetValues("X-Correlation-Id").Should().Equal("corr-1");
    }

    [Fact]
    public async Task Non_success_status_is_surfaced_as_an_exception()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "{}");
        var client = new OrdersApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://orders-api:8080") });

        var act = () => client.PromotePendingAsync("corr-1", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
