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
    .AddStandardResilienceHandler(resilience =>
    {
        // The defaults (10 s per attempt, 30 s total) suit interactive calls. A big PENDING backlog can take
        // longer, and a timed-out attempt aborts the request, which cancels and rolls back the UPDATE.
        resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
        resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
        resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2); // must be >= 2x attempt timeout
    });

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<PendingOrderPromotionWorker>();

builder.Build().Run();
