using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OrderProcessing.Orders.Application;
using Testcontainers.PostgreSql;

namespace OrderProcessing.Orders.IntegrationTests;

/// <summary>
/// Boots the real Orders API in memory against a throwaway PostgreSQL container, so EF Core mappings,
/// migrations, the concurrency token and the bulk UPDATE are exercised against the same engine as production.
/// </summary>
public sealed class OrdersApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };

    public Task InitializeAsync() => _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:OrdersDb", _postgres.GetConnectionString());
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<OrdersApiFactory>
{
    // Tests share one database and run sequentially; each test creates its own orders under a unique customer id.
    public const string Name = "integration";
}

internal static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(OrdersApiFactory.Json))!;

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, OrdersApiFactory.Json);

    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PatchAsJsonAsync(url, body, OrdersApiFactory.Json);

    public static async Task<OrderResponse> CreateOrderAsync(this HttpClient client, string customerId, int itemCount = 1)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(i => new CreateOrderItemRequest($"SKU-{i}", $"Product {i}", i, 10m))
            .ToList();
        var response = await client.PostJsonAsync("/api/orders", new CreateOrderRequest(customerId, items));
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<OrderResponse>();
    }

    public static string NewCustomerId() => $"cust-{Guid.NewGuid():N}";
}
