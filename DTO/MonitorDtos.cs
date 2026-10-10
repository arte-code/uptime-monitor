using System.ComponentModel.DataAnnotations;

namespace UptimeMonitor.DTO;

public record SetMonitorDto([Required] string Name, [Required, Url] string TargetUrl);

public record GetMonitorDto(int Id, [Required] string Name, [Required, Url] string TargetUrl);
