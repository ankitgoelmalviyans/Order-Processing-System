using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OrderProcessing.Orders.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>(ServiceLifetime.Singleton);
        services.AddScoped<IOrderService, OrderService>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
