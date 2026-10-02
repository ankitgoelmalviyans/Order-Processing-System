using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Infrastructure.Persistence;

namespace OrderProcessing.Orders.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<OrdersDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IOrderRepository, OrderRepository>();
        return services;
    }

    /// <summary>
    /// Applies pending EF Core migrations, retrying while the database is still starting.
    /// Compose already waits for Postgres to be healthy; the retry covers running outside compose.
    /// </summary>
    public static async Task MigrateOrdersDatabaseAsync(
        this IServiceProvider services, ILogger logger, int maxAttempts = 10, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                await db.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Orders database is up to date");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Database migration attempt {Attempt}/{MaxAttempts} failed; retrying", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * attempt, 10)), cancellationToken);
            }
        }
    }
}
