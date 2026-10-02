using Microsoft.Extensions.DependencyInjection;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Domain;
using static OrderProcessing.Orders.IntegrationTests.HttpExtensions;

namespace OrderProcessing.Orders.IntegrationTests;

/// <summary>
/// Reproduces the races the API has to survive, deterministically, by interleaving two units of work by hand.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ConcurrencyTests(OrdersApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Cancel_loses_if_background_job_promoted_the_order_after_it_was_read()
    {
        var created = await _client.CreateOrderAsync(NewCustomerId());

        await using var cancelScope = factory.Services.CreateAsyncScope();
        var cancelRepository = cancelScope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var order = await cancelRepository.GetByIdAsync(created.Id, CancellationToken.None);
        order!.Status.Should().Be(OrderStatus.Pending); // customer's cancel request has read the order...

        await using (var jobScope = factory.Services.CreateAsyncScope())
        {
            // ...then the background job promotes it before the cancel is saved.
            await jobScope.ServiceProvider.GetRequiredService<IOrderService>().PromotePendingAsync(CancellationToken.None);
        }

        order.Cancel(DateTimeOffset.UtcNow); // still looks PENDING in memory
        var save = () => cancelRepository.SaveChangesAsync(CancellationToken.None);

        await save.Should().ThrowAsync<ConcurrencyConflictException>();
        var stored = await _client.GetAsync($"/api/orders/{created.Id}");
        (await stored.ReadAsync<OrderResponse>()).Status.Should().Be(OrderStatus.Processing);
    }

    [Fact]
    public async Task Two_writers_on_the_same_order_only_one_wins()
    {
        var created = await _client.CreateOrderAsync(NewCustomerId());

        await using var scopeA = factory.Services.CreateAsyncScope();
        await using var scopeB = factory.Services.CreateAsyncScope();
        var repoA = scopeA.ServiceProvider.GetRequiredService<IOrderRepository>();
        var repoB = scopeB.ServiceProvider.GetRequiredService<IOrderRepository>();
        var orderA = (await repoA.GetByIdAsync(created.Id, CancellationToken.None))!;
        var orderB = (await repoB.GetByIdAsync(created.Id, CancellationToken.None))!;

        orderA.Cancel(DateTimeOffset.UtcNow);
        orderB.ChangeStatus(OrderStatus.Processing, DateTimeOffset.UtcNow);

        await repoA.SaveChangesAsync(CancellationToken.None);
        var saveB = () => repoB.SaveChangesAsync(CancellationToken.None);

        await saveB.Should().ThrowAsync<ConcurrencyConflictException>().Where(e => e.OrderId == created.Id);
    }

    [Fact]
    public async Task Concurrent_cancel_requests_produce_exactly_one_success()
    {
        var created = await _client.CreateOrderAsync(NewCustomerId());

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => factory.CreateClient().PostAsync($"/api/orders/{created.Id}/cancel", null)));

        responses.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        responses.Where(r => !r.IsSuccessStatusCode)
            .Should().OnlyContain(r => r.StatusCode == System.Net.HttpStatusCode.Conflict);
    }
}
