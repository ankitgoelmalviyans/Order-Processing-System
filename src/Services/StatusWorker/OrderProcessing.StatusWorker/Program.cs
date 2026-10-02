using Microsoft.Extensions.Options;
using OrderProcessing.StatusWorker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, logging) => logging
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "status-worker")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}"));

builder.Services
    .AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddHttpClient<IOrdersApiClient, OrdersApiClient>((services, http) =>
        http.BaseAddress = new Uri(services.GetRequiredService<IOptions<WorkerOptions>>().Value.OrdersApiBaseUrl))
    // Retries with exponential backoff, circuit breaker and timeouts. Retrying this POST is safe because
    // the promote endpoint is idempotent (UPDATE ... WHERE status = 'Pending').
    .AddStandardResilienceHandler();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<PendingOrderPromotionWorker>();

builder.Build().Run();
