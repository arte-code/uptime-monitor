namespace UptimeMonitor.Models;

public class MonitorTarget
{
    public int Id { set; get; }
    public required string Name { set; get; }
    public required string TargetUrl { set; get; }

    public required Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
