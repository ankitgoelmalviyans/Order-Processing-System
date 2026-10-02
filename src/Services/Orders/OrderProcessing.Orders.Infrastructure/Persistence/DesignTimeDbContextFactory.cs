using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderProcessing.Orders.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef migrations add</c> run without booting the API. Not used at runtime.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql("Host=localhost;Database=orders;Username=orders;Password=design-time-only")
            .Options;
        return new OrdersDbContext(options);
    }
}
