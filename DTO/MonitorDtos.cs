using System.ComponentModel.DataAnnotations;
using UptimeMonitor.Models;

namespace UptimeMonitor.DTO;

public record SetMonitorDto([Required] string Name, [Required, Url] string TargetUrl);

public record GetMonitorDto(Guid Id, [Required] string Name, [Required, Url] string TargetUrl);

public record CheckOutcomeDto(
    DateTime CheckedAt,
    bool IsUp,
    int? StatusCode,
    long? ResponseTime,
    FailureReason? FailureReason,
    Guid MonitorId
);
