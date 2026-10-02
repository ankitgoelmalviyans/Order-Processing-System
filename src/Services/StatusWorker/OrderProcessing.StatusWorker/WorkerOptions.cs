using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.StatusWorker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>How often PENDING orders are promoted. The requirement is every 5 minutes (300 s).</summary>
    [Range(1, 86_400)]
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>Also run once immediately at start-up instead of waiting for the first interval.</summary>
    public bool RunOnStartup { get; set; }

    [Required]
    [Url]
    public string OrdersApiBaseUrl { get; set; } = "http://localhost:5001";
}
