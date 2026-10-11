using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Data;

namespace UptimeMonitor.Workers;

using UptimeMonitor.Models;

class CheckWorker : BackgroundService
{
    private readonly ILogger<CheckWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _sharedClient = new HttpClient(
        new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }
    )
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public CheckWorker(ILogger<CheckWorker> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    private async Task<CheckResult> CheckMonitorAsync(string targetUrl, Guid monitorId, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var checkedAt = DateTime.UtcNow;
        FailureReason? failureReason = null;
        bool isUp = false;
        int? statusCode = null;

        try
        {
            using var response = await _sharedClient.GetAsync(targetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            statusCode = (int)response.StatusCode;
            isUp = response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            var exceptionReason = ex.HttpRequestError;
            failureReason = exceptionReason switch
            {
                HttpRequestError.NameResolutionError => FailureReason.DnsFailure,
                HttpRequestError.InvalidResponse => FailureReason.InvalidResponse,
                HttpRequestError.ConnectionError => FailureReason.ConnectionFailed,
                HttpRequestError.SecureConnectionError => FailureReason.TlsFailure,
                _ => FailureReason.Unknown,
            };
        }
        catch (OperationCanceledException)
        {
            failureReason = FailureReason.Timeout;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error checking {Url}", targetUrl);
            failureReason = FailureReason.Unknown;
        }
        finally
        {
            sw.Stop();
        }
        return new CheckResult
        {
            CheckedAt = checkedAt,
            IsUp = isUp,
            StatusCode = statusCode,
            ResponseTimeMs = sw.ElapsedMilliseconds,
            MonitorTargetId = monitorId,
            FailureReason = failureReason,
        };
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(300));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await RunCkecksAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Check cycle failed");
            }
        }
    }

    private async Task RunCkecksAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MonitoringContext>();

        var monitors = await db.Monitors.AsNoTracking().Select(m => new { m.Id, m.TargetUrl }).ToListAsync();

        var tasks = monitors.Select(m => CheckMonitorAsync(m.TargetUrl, m.Id, ct)).ToList();
        var results = await Task.WhenAll(tasks);

        db.CheckResults.AddRange(results);
        await db.SaveChangesAsync();
    }
}
