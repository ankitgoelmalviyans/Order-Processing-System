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
            _ => Problem(StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred."),
        };

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request rejected with {StatusCode}: {Reason}", problem.Status, exception.Message);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
