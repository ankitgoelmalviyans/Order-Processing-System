namespace OrderProcessing.Orders.Application;

public sealed class OrderProcessingOptions
{
    public const string SectionName = "OrderProcessing";

    /// <summary>
    /// Minimum age a PENDING order must reach before the background job promotes it to PROCESSING.
    /// Defaults to 0: every PENDING order is promoted on each run, which is the literal reading of the
    /// requirement. Raise it to give customers a guaranteed cancellation window.
    /// </summary>
    public int MinPendingAgeSeconds { get; set; }
}
