using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UptimeMonitor.Data;
using UptimeMonitor.DTO;
using UptimeMonitor.Models;
using UptimeMonitor.Services;
using BC = BCrypt.Net.BCrypt;

namespace UptimeMonitor;

class Program
{
    private static readonly string DummyHash = BC.EnhancedHashPassword(
        "not-a-real-password",
        workFactor: 12
    );

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

        var app = builder.Build();

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapGet("/", () => "Hello World!");
        app.MapGet(
            "/monitors",
            async (MonitoringContext db) =>
            {
                return await db
                    .Monitors.OrderBy(m => m.Id)
                    .Select(m => new GetMonitorDto(m.Id, m.Name, m.TargetUrl))
                    .ToListAsync();
            }
        );
        //   app.MapPost(
        //"/monitors",
        //  async (MonitoringContext db, SetMonitorDto dto) =>
        //    {
        //          var monitor = db.Monitors.Add(
        //                new MonitorTarget { Name = dto.Name, TargetUrl = dto.TargetUrl }
        //              );
        //
        //var entity = monitor.Entity;
        //  await db.SaveChangesAsync();
        //    return TypedResults.Ok(new GetMonitorDto(entity.Id, entity.Name, entity.TargetUrl));
        //  }
        //);
        app.MapPut(
            "/monitors/{id}",
            async Task<Results<NotFound, Ok<GetMonitorDto>>> (
                MonitoringContext db,
                SetMonitorDto dto,
                int id
            ) =>
            {
                var monitor = await db.Monitors.FindAsync(id);
                if (monitor == null)
                {
                    return TypedResults.NotFound();
                }

                monitor.Name = dto.Name;
                monitor.TargetUrl = dto.TargetUrl;

                await db.SaveChangesAsync();
                return TypedResults.Ok(
                    new GetMonitorDto(monitor.Id, monitor.Name, monitor.TargetUrl)
                );
            }
        );
        app.MapDelete(
            "/monitors/{id}",
            async Task<Results<NotFound, NoContent>> (MonitoringContext db, int id) =>
            {
                var monitor = await db.Monitors.FindAsync(id);
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
            async Task<Results<Ok<UserResponse>, ProblemHttpResult>> (
                MonitoringContext db,
                RegisterRequest req
            ) =>
            {
                var email = req.Email.Trim().ToLowerInvariant();

                if (await db.Users.AnyAsync(u => u.Email == email))
                {
                    return TypedResults.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Email is already registered."
                    );
                }
                var user = new User
                {
                    Email = email,
                    PasswordHash = BC.EnhancedHashPassword(req.Password, workFactor: 12),
                };

                db.Users.Add(user);

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                    when (ex.InnerException
                            is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
                    )
                {
                    return TypedResults.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Email is already registered."
                    );
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
