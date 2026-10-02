using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace OrderProcessing.BuildingBlocks;

/// <summary>
/// Reuses the caller's <c>X-Correlation-Id</c> (or creates one), writes it back on the request so a reverse
/// proxy forwards it downstream, echoes it on the response and adds it to every log line for the request.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > MaxLength || !IsSafe(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Request.Headers[HeaderName] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    // Header values end up in logs; only accept simple tokens to avoid log forging.
    private static bool IsSafe(string value) => value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
