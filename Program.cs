using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Data;
using UptimeMonitor.DTO;
using UptimeMonitor.Models;

namespace UptimeMonitor;

class Program
{
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
        app.MapPost(
            "/monitors",
            async (MonitoringContext db, SetMonitorDto dto) =>
            {
                var monitor = db.Monitors.Add(
                    new MonitorTarget { Name = dto.Name, TargetUrl = dto.TargetUrl }
                );

                var entity = monitor.Entity;
                await db.SaveChangesAsync();
                return TypedResults.Ok(new GetMonitorDto(entity.Id, entity.Name, entity.TargetUrl));
            }
        );
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

        app.Run();
    }
}
