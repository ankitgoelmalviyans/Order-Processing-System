using Microsoft.Extensions.Options;
using Serilog.Context;

namespace OrderProcessing.StatusWorker;

/// <summary>
/// Every <see cref="WorkerOptions.IntervalSeconds"/> asks the Orders service to move PENDING orders to PROCESSING.
/// A failed run is logged and retried on the next tick; it never stops the loop.
/// </summary>
internal sealed class PendingOrderPromotionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<PendingOrderPromotionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.IntervalSeconds);
        logger.LogInformation("Pending order promotion scheduled every {Interval}", interval);

        // PeriodicTimer does not drift or overlap: a slow run delays the next tick instead of stacking runs.
        using var timer = new PeriodicTimer(interval, timeProvider);

        if (options.Value.RunOnStartup)
        {
            await RunOnceAsync(stoppingToken);
        }

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    internal async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        using var _ = LogContext.PushProperty("CorrelationId", correlationId);

        try
        {
            // A fresh scope per run so the typed HttpClient (and its pooled handler) is never held forever.
            await using var scope = scopeFactory.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<IOrdersApiClient>();

            var promoted = await client.PromotePendingAsync(correlationId, stoppingToken);
            logger.LogInformation("Promotion run finished: {PromotedCount} orders moved PENDING -> PROCESSING", promoted);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Promotion run failed; will retry on the next tick");
        }
    }
}
