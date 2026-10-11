namespace UptimeMonitor.Models;

public class CheckResult
{
    public int Id { get; set; }

    public required DateTime CheckedAt { get; set; }
    public required bool IsUp { get; set; }
    public int? StatusCode { get; set; }
    public long? ResponseTimeMs { get; set; }

    public FailureReason? FailureReason { get; set; }

    public required Guid MonitorTargetId { get; set; }
    public MonitorTarget MonitorTarget { get; set; } = null!;
}
