using System.ComponentModel.DataAnnotations;

namespace UptimeMonitor.Models;

public class MonitorTarget
{
    public int Id { set; get; }

    [Required]
    public required string Name { set; get; }

    [Url]
    [Required]
    public required string TargetUrl { set; get; }
}
