using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.UnitTests.Application;

public class OrderServiceTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryOrderRepository _repository = new();

    private OrderService CreateService(int minPendingAgeSeconds = 0) => new(
        _repository,
        new CreateOrderRequestValidator(),
        new UpdateOrderStatusRequestValidator(),
        new ListOrdersQueryValidator(),
        Options.Create(new OrderProcessingOptions { MinPendingAgeSeconds = minPendingAgeSeconds }),
        _time,
        NullLogger<OrderService>.Instance);

    private static CreateOrderRequest ValidRequest() =>
        new("cust-1", [new CreateOrderItemRequest("SKU-1", "Widget", 2, 5m)]);

    [Fact]
    public async Task Create_persists_the_order_and_stamps_the_current_time()
    {
        var response = await CreateService().CreateAsync(ValidRequest(), CancellationToken.None);

        response.Status.Should().Be(OrderStatus.Pending);
        response.TotalAmount.Should().Be(10m);
        response.CreatedAt.Should().Be(_time.GetUtcNow());
        _repository.Orders.Should().ContainSingle(o => o.Id == response.Id);
        _repository.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_with_invalid_request_throws_validation_and_persists_nothing()
    {
        var act = () => CreateService().CreateAsync(new CreateOrderRequest("cust-1", []), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _repository.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_cancel_and_update_on_unknown_order_throw_not_found()
    {
        var service = CreateService();
        var id = Guid.NewGuid();

        await service.Invoking(s => s.GetByIdAsync(id, CancellationToken.None)).Should().ThrowAsync<OrderNotFoundException>();
        await service.Invoking(s => s.CancelAsync(id, CancellationToken.None)).Should().ThrowAsync<OrderNotFoundException>();
        await service.Invoking(s => s.UpdateStatusAsync(id, new UpdateOrderStatusRequest(OrderStatus.Processing), CancellationToken.None))
            .Should().ThrowAsync<OrderNotFoundException>();
    }

    [Fact]
    public async Task Promote_with_default_options_uses_now_as_cutoff()
    {
        await CreateService().PromotePendingAsync(CancellationToken.None);

        _repository.LastPromoteCutoff.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task Promote_respects_minimum_pending_age()
    {
        var service = CreateService(minPendingAgeSeconds: 60);
        var old = await service.CreateAsync(ValidRequest(), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(90));
        var fresh = await service.CreateAsync(ValidRequest() with { CustomerId = "cust-2" }, CancellationToken.None);

        var result = await service.PromotePendingAsync(CancellationToken.None);

        result.PromotedCount.Should().Be(1);
        _repository.Find(old.Id).Status.Should().Be(OrderStatus.Processing);
        _repository.Find(fresh.Id).Status.Should().Be(OrderStatus.Pending);
    }

    /// <summary>Minimal in-memory stand-in; the real EF repository is covered by the integration tests.</summary>
    private sealed class InMemoryOrderRepository : IOrderRepository
    {
        public List<Order> Orders { get; } = [];

        public int SaveCount { get; private set; }

        public DateTimeOffset? LastPromoteCutoff { get; private set; }

        public Order Find(Guid id) => Orders.Single(o => o.Id == id);

        public Task AddAsync(Order order, CancellationToken cancellationToken)
        {
            Orders.Add(order);
            return Task.CompletedTask;
        }

        public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.SingleOrDefault(o => o.Id == id));

        public Task<(IReadOnlyList<Order> Items, int TotalCount)> ListAsync(
            OrderStatus? status, string? customerId, int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> PromotePendingAsync(DateTimeOffset createdAtOrBefore, DateTimeOffset now, CancellationToken cancellationToken)
        {
            LastPromoteCutoff = createdAtOrBefore;
            var eligible = Orders.Where(o => o.Status == OrderStatus.Pending && o.CreatedAt <= createdAtOrBefore).ToList();
            eligible.ForEach(o => o.ChangeStatus(OrderStatus.Processing, now));
            return Task.FromResult(eligible.Count);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
