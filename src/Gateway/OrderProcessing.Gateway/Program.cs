using OrderProcessing.BuildingBlocks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceLogging("gateway");

// Routes live in configuration ("ReverseProxy" section). Only public paths are routed:
// /api/orders/** and /swagger/**. Anything else, including /internal/**, gets a 404 here.
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCorrelationId();
app.UseSerilogRequestLogging();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();
