using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace OrderProcessing.StatusWorker.UnitTests;

public sealed class PendingOrderPromotionWorkerTests : IAsyncDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeOrdersApiClient _client = new();
    private PendingOrderPromotionWorker? _worker;

    private PendingOrderPromotionWorker CreateWorker(int intervalSeconds = 300, bool runOnStartup = false)
    {
        var services = new ServiceCollection().AddSingleton<IOrdersApiClient>(_client).BuildServiceProvider();
        _worker = new PendingOrderPromotionWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WorkerOptions { IntervalSeconds = intervalSeconds, RunOnStartup = runOnStartup }),
            _time,
            NullLogger<PendingOrderPromotionWorker>.Instance);
        return _worker;
    }

    [Fact]
    public async Task Does_not_call_the_api_before_the_first_interval_elapses()
    {
        await CreateWorker().StartAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));

        _client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Calls_the_api_once_every_five_minutes()
    {
        await CreateWorker().StartAsync(CancellationToken.None);

        for (var tick = 1; tick <= 3; tick++)
        {
            _time.Advance(TimeSpan.FromMinutes(5));
            await _client.WaitForCallAsync(WaitLimit);
        }

        _client.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task Runs_immediately_when_RunOnStartup_is_enabled()
    {
        await CreateWorker(runOnStartup: true).StartAsync(CancellationToken.None);

        await _client.WaitForCallAsync(WaitLimit);

        _client.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_run_does_not_stop_later_runs()
    {
        _client.FailNextCall = true;
        await CreateWorker().StartAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(5));
        await _client.WaitForCallAsync(WaitLimit); // throws inside the worker
        _time.Advance(TimeSpan.FromMinutes(5));
        await _client.WaitForCallAsync(WaitLimit);

        _client.CallCount.Should().Be(2);
        _worker!.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task Each_run_sends_a_new_correlation_id()
    {
        var worker = CreateWorker();

        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);

        _client.CorrelationIds.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        _client.CorrelationIds.Should().OnlyContain(id => !string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public async Task Stops_cleanly_when_the_host_shuts_down()
    {
        var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        await worker.StopAsync(CancellationToken.None);

        worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    public async ValueTask DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker.Dispose();
        }
    }

    private sealed class FakeOrdersApiClient : IOrdersApiClient
    {
        private readonly Channel<int> _calls = Channel.CreateUnbounded<int>();
        private int _callCount;

        public bool FailNextCall { get; set; }

        public int CallCount => Volatile.Read(ref _callCount);

        public List<string> CorrelationIds { get; } = [];

        public Task<int> PromotePendingAsync(string correlationId, CancellationToken cancellationToken)
        {
            CorrelationIds.Add(correlationId);
            var count = Interlocked.Increment(ref _callCount);
            _calls.Writer.TryWrite(count);

            if (FailNextCall)
            {
                FailNextCall = false;
                throw new HttpRequestException("orders-api unavailable");
            }

            return Task.FromResult(1);
        }

        public async Task WaitForCallAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await _calls.Reader.ReadAsync(cts.Token);
        }
    }
}
