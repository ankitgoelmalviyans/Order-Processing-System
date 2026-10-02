using Microsoft.AspNetCore.Builder;
using Serilog;

namespace OrderProcessing.BuildingBlocks;

public static class LoggingExtensions
{
    public const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>Structured console logging, configurable through the "Serilog" configuration section.</summary>
    public static WebApplicationBuilder AddServiceLogging(
        this WebApplicationBuilder builder, string serviceName, Action<LoggerConfiguration>? configure = null)
    {
        builder.Host.UseSerilog((context, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName)
                .WriteTo.Console(outputTemplate: ConsoleTemplate);
            configure?.Invoke(configuration);
        });
        return builder;
    }
}
