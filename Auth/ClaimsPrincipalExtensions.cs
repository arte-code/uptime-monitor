using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace UptimeMonitor.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(
            user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? throw new InvalidOperationException("Authenticated user has no 'sub' claim.")
        );
}
