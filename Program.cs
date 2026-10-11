using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using UptimeMonitor.Auth;
using UptimeMonitor.Data;
using UptimeMonitor.DTO;
using UptimeMonitor.Models;
using UptimeMonitor.Services;
using UptimeMonitor.Workers;
using BC = BCrypt.Net.BCrypt;

namespace UptimeMonitor;

class Program
{
    private static readonly string DummyHash = BC.EnhancedHashPassword("not-a-real-password", workFactor: 12);

    static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var cs =
            builder.Configuration.GetConnectionString("default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");

        builder.Services.AddDbContext<MonitoringContext>(o => o.UseNpgsql(cs));
        builder.Services.AddValidation();
        builder.Services.AddProblemDetails(o =>
            o.CustomizeProblemDetails = ctx =>
            {
                if (ctx.ProblemDetails.Status is < 500)
                    ctx.ProblemDetails.Extensions.Remove("traceId");
            }
        );

        builder.Services.AddSingleton<TokenService>();

        var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");

        builder
            .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddHttpClient(
            "monitor",
            client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            }
        );
        builder.Services.AddHostedService<CheckWorker>();

        var app = builder.Build();

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapGet("/", () => "Hello World!");

        var monitors = app.MapGroup("/monitors").RequireAuthorization();

        monitors.MapGet(
            "/",
            async (MonitoringContext db, ClaimsPrincipal user) =>
            {
                var userId = user.GetUserId();
                return await db
                    .Monitors.Where(m => m.UserId == userId)
                    .OrderBy(m => m.Id)
                    .Select(m => new GetMonitorDto(m.Id, m.Name, m.TargetUrl))
                    .ToListAsync();
            }
        );

        monitors.MapGet(
            "/{monitorId}/checks",
            async Task<Results<NotFound, Ok<List<CheckOutcomeDto>>>> (
                MonitoringContext db,
                ClaimsPrincipal user,
                Guid monitorId
            ) =>
            {
                var userId = user.GetUserId();
                var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.UserId == userId && m.Id == monitorId);

                if (monitor == null)
                {
                    return TypedResults.NotFound();
                }

                var checks = await db
                    .CheckResults.AsNoTracking()
                    .Where(c => c.MonitorTargetId == monitor.Id)
                    .Select(c => new CheckOutcomeDto(
                        c.CheckedAt,
                        c.IsUp,
                        c.StatusCode,
                        c.ResponseTimeMs,
                        c.FailureReason,
                        c.MonitorTargetId
                    ))
                    .ToListAsync();

                return TypedResults.Ok(checks);
            }
        );

        monitors.MapPost(
            "/",
            async (MonitoringContext db, SetMonitorDto dto, ClaimsPrincipal user) =>
            {
                var userId = user.GetUserId();
                var monitor = db.Monitors.Add(
                    new MonitorTarget
                    {
                        Name = dto.Name,
                        TargetUrl = dto.TargetUrl,
                        UserId = userId,
                    }
                );

                var entity = monitor.Entity;
                await db.SaveChangesAsync();
                return TypedResults.Ok(new GetMonitorDto(entity.Id, entity.Name, entity.TargetUrl));
            }
        );
        monitors.MapPut(
            "/{monitorId}",
            async Task<Results<NotFound, Ok<GetMonitorDto>>> (
                MonitoringContext db,
                ClaimsPrincipal user,
                SetMonitorDto dto,
                Guid monitorId
            ) =>
            {
                var userId = user.GetUserId();
                var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == monitorId && m.UserId == userId);

                if (monitor == null)
                {
                    return TypedResults.NotFound();
                }

                monitor.Name = dto.Name;
                monitor.TargetUrl = dto.TargetUrl;

                await db.SaveChangesAsync();
                return TypedResults.Ok(new GetMonitorDto(monitor.Id, monitor.Name, monitor.TargetUrl));
            }
        );
        monitors.MapDelete(
            "/{monitorId}",
            async Task<Results<NotFound, NoContent>> (MonitoringContext db, ClaimsPrincipal user, Guid monitorId) =>
            {
                var userId = user.GetUserId();

                var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == monitorId && m.UserId == userId);
                if (monitor == null)
                {
                    return TypedResults.NotFound();
                }

                db.Monitors.Remove(monitor);
                await db.SaveChangesAsync();
                return TypedResults.NoContent();
            }
        );

        var auth = app.MapGroup("/auth");

        auth.MapPost(
            "/register",
            async Task<Results<Ok<UserResponse>, ProblemHttpResult>> (MonitoringContext db, RegisterRequest req) =>
            {
                var email = req.Email.Trim().ToLowerInvariant();

                if (await db.Users.AnyAsync(u => u.Email == email))
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email is already registered.");
                }
                var user = new User { Email = email, PasswordHash = BC.EnhancedHashPassword(req.Password, workFactor: 12) };

                db.Users.Add(user);

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                    when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email is already registered.");
                }

                return TypedResults.Ok(new UserResponse(user.Id, user.Email));
            }
        );

        auth.MapPost(
            "/login",
            async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> (
                MonitoringContext db,
                TokenService tokens,
                LoginRequest req
            ) =>
            {
                var email = req.Email.Trim().ToLowerInvariant();

                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);

                var passwordOk = BC.EnhancedVerify(req.Password, user?.PasswordHash ?? DummyHash);

                if (user is null || !passwordOk)
                {
                    return TypedResults.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Invalid email or password."
                    );
                }

                return TypedResults.Ok(tokens.CreateAccessToken(user));
            }
        );

        app.Run();
    }
}
