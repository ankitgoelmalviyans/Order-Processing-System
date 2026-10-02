using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using OrderProcessing.BuildingBlocks;
using OrderProcessing.Orders.Api;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceLogging("orders-api");

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

var app = builder.Build();

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
