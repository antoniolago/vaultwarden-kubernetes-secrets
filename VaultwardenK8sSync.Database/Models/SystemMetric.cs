namespace VaultwardenK8sSync.Database.Models;

/// <summary>
/// Stores system metrics over time for dashboard visualization
/// </summary>
public class SystemMetric
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string MetricName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string? Labels { get; set; } // JSON string of labels
}
