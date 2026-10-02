using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Api;

/// <summary>Maps application/domain exceptions to RFC 7807 problem responses in one place.</summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Exceptions that are normal business outcomes (4xx), not faults. In .NET 8 the built-in
    /// ExceptionHandlerMiddleware logs every exception at Error before this handler runs; Program.cs filters
    /// that log line out for these types only, so real 5xx faults are still logged with their stack trace.
    /// </summary>
    public static bool IsExpected(Exception? exception) => exception is
        ValidationException or OrderDomainException or OrderNotFoundException or ConcurrencyConflictException
        or BadHttpRequestException;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing useful to send and not a server error.
            httpContext.Response.StatusCode = 499;
            return true;
        }

        ProblemDetails problem = exception switch
        {
            ValidationException ve => new HttpValidationProblemDetails(
                ve.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            InvalidOrderException e => Problem(StatusCodes.Status400BadRequest, "Invalid order", e.Message),
            OrderNotFoundException e => Problem(StatusCodes.Status404NotFound, "Order not found", e.Message),
            InvalidOrderStateTransitionException e => Problem(StatusCodes.Status409Conflict, "Invalid status transition", e.Message),
            ConcurrencyConflictException e => Problem(StatusCodes.Status409Conflict, "Concurrent update", e.Message),
            // Kestrel/MVC already know the right status (e.g. 413 body too large, 400 truncated body).
            BadHttpRequestException e => Problem(e.StatusCode, "Bad request", e.Message),
            _ => Problem(StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred."),
        };

        if (IsExpected(exception))
        {
            logger.LogInformation("Request rejected with {StatusCode}: {Reason}", problem.Status, exception.Message);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });

        if (!written)
        {
            // No registered writer accepts the client's Accept header (e.g. "text/plain"). Returning false here
            // would make the middleware rethrow and turn a 404/409 into a 500, so always answer with JSON.
            await httpContext.Response.WriteAsJsonAsync(
                problem, problem.GetType(), options: null, contentType: "application/problem+json", cancellationToken);
        }

        return true;
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
