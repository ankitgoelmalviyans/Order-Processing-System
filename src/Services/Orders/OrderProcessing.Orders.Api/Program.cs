using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using OrderProcessing.BuildingBlocks;
using OrderProcessing.Orders.Api;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Infrastructure;
using Serilog;
using Serilog.Filters;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceLogging("orders-api", logging => logging.Filter.ByExcluding(e =>
    // Drop the framework's duplicate Error log for expected 4xx outcomes; real faults still get logged.
    Matching.FromSource("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware")(e)
    && GlobalExceptionHandler.IsExpected(e.Exception)));

var connectionString = builder.Configuration.GetConnectionString("OrdersDb")
    ?? throw new InvalidOperationException("Connection string 'OrdersDb' is not configured.");

builder.Services
    .AddOrdersApplication()
    .AddOrdersInfrastructure(connectionString)
    .Configure<OrderProcessingOptions>(builder.Configuration.GetSection(OrderProcessingOptions.SectionName));

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        // Statuses travel as "PENDING", "PROCESSING", ...; numbers are rejected so undefined values cannot sneak in.
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false)));

// FluentValidation is the single source of request validation rules, so turn off MVC's implicit
// [Required] for non-nullable reference types (it would produce a second, differently worded error set).
builder.Services.Configure<MvcOptions>(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Order Processing API",
        Version = "v1",
        Description = "Place, track, list, update and cancel e-commerce orders. "
            + "PENDING orders are promoted to PROCESSING by a background job every 5 minutes.",
    });
    c.SupportNonNullableReferenceTypes();
});

builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres");

// Behind the gateway the Host header is "orders-api:8080"; honour X-Forwarded-Host/Proto so generated
// links (e.g. the Location header on 201) point at the public gateway address. The service publishes no
// port and is reachable only from the private compose network, so headers from any proxy there are trusted.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseCorrelationId();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

// Swagger stays on in every environment: this is a demo system and the gateway exposes it as the UI.
app.UseSwagger();
app.UseSwaggerUI(c => c.DocumentTitle = "Order Processing API");

app.MapControllers();
app.MapHealthChecks("/health");

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await app.Services.MigrateOrdersDatabaseAsync(app.Logger);
}

app.Run();

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;
