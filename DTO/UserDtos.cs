using System.ComponentModel.DataAnnotations;

namespace UptimeMonitor.DTO;

public record RegisterRequest(
    [EmailAddress, Required, MaxLength(254)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password
);

public record UserResponse(Guid Id, string Email);

public record LoginRequest(
    [Required, MaxLength(254)] string Email,
    [Required, MaxLength(128)] string Password
);

public record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);
