using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Models;

namespace UptimeMonitor.Data;

public class MonitoringContext : DbContext
{
    public MonitoringContext(DbContextOptions<MonitoringContext> options)
        : base(options) { }

    public DbSet<MonitorTarget> Monitors => Set<MonitorTarget>();
    public DbSet<User> Users => Set<User>();
    public DbSet<CheckResult> CheckResults => Set<CheckResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<CheckResult>().Property(c => c.FailureReason).HasConversion<string>();
    }
}
