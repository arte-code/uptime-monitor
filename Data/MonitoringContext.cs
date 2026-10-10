using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Models;

namespace UptimeMonitor.Data;

public class MonitoringContext : DbContext
{
    public MonitoringContext(DbContextOptions<MonitoringContext> options)
        : base(options) { }

    public DbSet<MonitorTarget> Monitors { get; set; }
}
