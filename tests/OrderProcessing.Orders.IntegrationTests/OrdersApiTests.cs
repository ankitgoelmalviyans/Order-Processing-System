using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Domain;
using static OrderProcessing.Orders.IntegrationTests.HttpExtensions;

namespace OrderProcessing.Orders.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class OrdersApiTests(OrdersApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // ---------- Create & get ----------

    [Fact]
    public async Task Create_returns_201_with_location_and_server_computed_total()
    {
        var request = new CreateOrderRequest("cust-42",
        [
            new CreateOrderItemRequest("SKU-1", "Keyboard", 2, 49.99m),
            new CreateOrderItemRequest("SKU-2", "Mouse", 1, 19.50m),
        ]);

        var response = await _client.PostJsonAsync("/api/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await response.ReadAsync<OrderResponse>();
        response.Headers.Location!.ToString().Should().EndWith($"/api/orders/{order.Id}");
        order.Status.Should().Be(OrderStatus.Pending);
        order.TotalAmount.Should().Be(119.48m);
        order.Items.Should().HaveCount(2);
        order.Items.Should().ContainSingle(i => i.ProductId == "SKU-1").Which.LineTotal.Should().Be(99.98m);
    }

    [Fact]
    public async Task Get_returns_the_persisted_order_with_items()
    {
        var created = await _client.CreateOrderAsync(NewCustomerId(), itemCount: 3);

        var response = await _client.GetAsync($"/api/orders/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await response.ReadAsync<OrderResponse>();
        fetched.Should().BeEquivalentTo(created, o => o
            .Using<DateTimeOffset>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromMilliseconds(1)))
            .WhenTypeIs<DateTimeOffset>());
    }

    [Fact]
    public async Task Get_unknown_order_returns_404_problem_details()
    {
        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.ReadAsync<ProblemDetails>()).Title.Should().Be("Order not found");
    }

    [Theory]
    [InlineData("""{"customerId":"c1","items":[]}""", "Items")]
    [InlineData("""{"customerId":"c1"}""", "Items")]
    [InlineData("""{"customerId":"","items":[{"productId":"A","productName":"A","quantity":1,"unitPrice":1}]}""", "CustomerId")]
    [InlineData("""{"items":[{"productId":"A","productName":"A","quantity":1,"unitPrice":1}]}""", "CustomerId")]
    [InlineData("""{"customerId":"c1","items":[{"productId":"A","productName":"A","quantity":0,"unitPrice":1}]}""", "Items[0].Quantity")]
    [InlineData("""{"customerId":"c1","items":[{"productId":"A","productName":"A","quantity":1,"unitPrice":-1}]}""", "Items[0].UnitPrice")]
    [InlineData("""{"customerId":"c1","items":[{"productId":"A","productName":"A","quantity":1,"unitPrice":1.999}]}""", "Items[0].UnitPrice")]
    [InlineData("""{"customerId":"c1","items":[{"productId":"","productName":"A","quantity":1,"unitPrice":1}]}""", "Items[0].ProductId")]
    [InlineData("""{"customerId":"c1","items":[{"productId":"A","productName":"A","quantity":1,"unitPrice":1},{"productId":"a","productName":"B","quantity":1,"unitPrice":1}]}""", "Items")]
    [InlineData("""{"customerId":"c1","items":[null]}""", "Items[0]")]
    public async Task Create_with_invalid_payload_returns_400_with_field_errors(string json, string expectedErrorKey)
    {
        var response = await _client.PostAsync("/api/orders", new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadAsync<ValidationProblemDetails>();
        problem.Errors.Keys.Should().Contain(expectedErrorKey);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"customerId":"c1","items":[{"productId":"A","productName":"A","quantity":"lots","unitPrice":1}]}""")]
    public async Task Create_with_malformed_json_returns_400(string json)
    {
        var response = await _client.PostAsync("/api/orders", new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- List ----------

    [Fact]
    public async Task List_filters_by_status_and_customer()
    {
        var customer = NewCustomerId();
        var pending = await _client.CreateOrderAsync(customer);
        var toCancel = await _client.CreateOrderAsync(customer);
        (await _client.PostAsync($"/api/orders/{toCancel.Id}/cancel", null)).EnsureSuccessStatusCode();

        var pendingPage = await (await _client.GetAsync($"/api/orders?customerId={customer}&status=PENDING"))
            .ReadAsync<PagedResponse<OrderResponse>>();
        var cancelledPage = await (await _client.GetAsync($"/api/orders?customerId={customer}&status=CANCELLED"))
            .ReadAsync<PagedResponse<OrderResponse>>();
        var allPage = await (await _client.GetAsync($"/api/orders?customerId={customer}"))
            .ReadAsync<PagedResponse<OrderResponse>>();

        pendingPage.Items.Select(o => o.Id).Should().Equal(pending.Id);
        cancelledPage.Items.Select(o => o.Id).Should().Equal(toCancel.Id);
        allPage.TotalCount.Should().Be(2);
        allPage.Items.Select(o => o.Id).Should().Equal(toCancel.Id, pending.Id); // newest first
    }

    [Fact]
    public async Task List_paginates_without_overlap()
    {
        var customer = NewCustomerId();
        for (var i = 0; i < 5; i++)
        {
            await _client.CreateOrderAsync(customer);
        }

        var page1 = await (await _client.GetAsync($"/api/orders?customerId={customer}&page=1&pageSize=2")).ReadAsync<PagedResponse<OrderResponse>>();
        var page2 = await (await _client.GetAsync($"/api/orders?customerId={customer}&page=2&pageSize=2")).ReadAsync<PagedResponse<OrderResponse>>();
        var page3 = await (await _client.GetAsync($"/api/orders?customerId={customer}&page=3&pageSize=2")).ReadAsync<PagedResponse<OrderResponse>>();

        page1.TotalCount.Should().Be(5);
        page1.TotalPages.Should().Be(3);
        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(2);
        page3.Items.Should().HaveCount(1);
        page1.Items.Concat(page2.Items).Concat(page3.Items).Select(o => o.Id).Should().OnlyHaveUniqueItems().And.HaveCount(5);
    }

    [Theory]
    [InlineData("status=NOT_A_STATUS")]
    [InlineData("status=7")]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_with_invalid_query_returns_400(string query)
    {
        var response = await _client.GetAsync($"/api/orders?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- Update status ----------

    [Fact]
    public async Task Update_status_walks_the_full_lifecycle()
    {
        var order = await _client.CreateOrderAsync(NewCustomerId());

        foreach (var next in new[] { OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Delivered })
        {
            var response = await _client.PatchJsonAsync($"/api/orders/{order.Id}/status", new UpdateOrderStatusRequest(next));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.ReadAsync<OrderResponse>()).Status.Should().Be(next);
        }
    }

    [Fact]
    public async Task Update_status_rejects_skipping_a_step_with_409()
    {
        var order = await _client.CreateOrderAsync(NewCustomerId());

        var response = await _client.PatchJsonAsync($"/api/orders/{order.Id}/status", new UpdateOrderStatusRequest(OrderStatus.Delivered));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadAsync<ProblemDetails>()).Title.Should().Be("Invalid status transition");
        (await (await _client.GetAsync($"/api/orders/{order.Id}")).ReadAsync<OrderResponse>()).Status.Should().Be(OrderStatus.Pending);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"status":"UNKNOWN"}""")]
    [InlineData("""{"status":1}""")]
    public async Task Update_status_with_missing_or_invalid_status_returns_400(string json)
    {
        var order = await _client.CreateOrderAsync(NewCustomerId());

        var response = await _client.PatchAsync($"/api/orders/{order.Id}/status", new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_status_of_unknown_order_returns_404()
    {
        var response = await _client.PatchJsonAsync($"/api/orders/{Guid.NewGuid()}/status", new UpdateOrderStatusRequest(OrderStatus.Processing));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- Cancel ----------

    [Fact]
    public async Task Cancel_pending_order_succeeds_once_then_conflicts()
    {
        var order = await _client.CreateOrderAsync(NewCustomerId());

        var first = await _client.PostAsync($"/api/orders/{order.Id}/cancel", null);
        var second = await _client.PostAsync($"/api/orders/{order.Id}/cancel", null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.ReadAsync<OrderResponse>()).Status.Should().Be(OrderStatus.Cancelled);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancel_is_rejected_once_order_is_processing()
    {
        var order = await _client.CreateOrderAsync(NewCustomerId());
        (await _client.PatchJsonAsync($"/api/orders/{order.Id}/status", new UpdateOrderStatusRequest(OrderStatus.Processing)))
            .EnsureSuccessStatusCode();

        var response = await _client.PostAsync($"/api/orders/{order.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await (await _client.GetAsync($"/api/orders/{order.Id}")).ReadAsync<OrderResponse>()).Status.Should().Be(OrderStatus.Processing);
    }

    [Fact]
    public async Task Cancel_unknown_order_returns_404()
    {
        var response = await _client.PostAsync($"/api/orders/{Guid.NewGuid()}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- Background job endpoint ----------

    [Fact]
    public async Task Promote_pending_moves_only_pending_orders_and_is_idempotent()
    {
        var customer = NewCustomerId();
        var pending = await _client.CreateOrderAsync(customer);
        var cancelled = await _client.CreateOrderAsync(customer);
        (await _client.PostAsync($"/api/orders/{cancelled.Id}/cancel", null)).EnsureSuccessStatusCode();

        var first = await _client.PostAsync("/internal/jobs/promote-pending", null);
        var second = await _client.PostAsync("/internal/jobs/promote-pending", null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.ReadAsync<PromotePendingResponse>()).PromotedCount.Should().BeGreaterThanOrEqualTo(1);
        (await second.ReadAsync<PromotePendingResponse>()).PromotedCount.Should().Be(0);

        var promoted = await (await _client.GetAsync($"/api/orders/{pending.Id}")).ReadAsync<OrderResponse>();
        promoted.Status.Should().Be(OrderStatus.Processing);
        promoted.UpdatedAt.Should().BeAfter(promoted.CreatedAt);
        (await (await _client.GetAsync($"/api/orders/{cancelled.Id}")).ReadAsync<OrderResponse>()).Status.Should().Be(OrderStatus.Cancelled);
    }

    // ---------- Cross-cutting ----------

    [Fact]
    public async Task Correlation_id_is_echoed_or_generated()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{Guid.NewGuid()}");
        request.Headers.Add("X-Correlation-Id", "test-correlation-123");

        var echoed = await _client.SendAsync(request);
        var generated = await _client.GetAsync("/health");

        echoed.Headers.GetValues("X-Correlation-Id").Should().Equal("test-correlation-123");
        generated.Headers.GetValues("X-Correlation-Id").Single().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Health_reports_healthy_when_database_is_reachable()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
