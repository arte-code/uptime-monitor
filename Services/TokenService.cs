using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using UptimeMonitor.DTO;
using UptimeMonitor.Models;

namespace UptimeMonitor.Services;

public class TokenService
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _lifetime;

    public TokenService(IConfiguration config)
    {
        var keyBase64 =
            config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var keyBytes = Convert.FromBase64String(keyBase64);

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256
        );
        _issuer =
            config["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        _audience =
            config["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

        _lifetime = TimeSpan.FromMinutes(config.GetValue("Jwt:AccessTokenMinutes", 15));
    }

    public LoginResponse CreateAccessToken(User user)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(_lifetime);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
                new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new Claim(JwtRegisteredClaimNames.Email, user.Email),
                }
            ),
            Issuer = _issuer,
            Audience = _audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = _credentials,
        };

        return new LoginResponse(_handler.CreateToken(descriptor), expiresAt);
    }
}
