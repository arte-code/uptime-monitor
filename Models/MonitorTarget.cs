namespace UptimeMonitor.Models;

public class MonitorTarget
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { set; get; }
    public required string TargetUrl { set; get; }

    public required Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
