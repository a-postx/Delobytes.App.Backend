using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Delobytes.App.Backend.Infrastructure.Health.System;

/// <summary>
/// Проверяет пробег приложения.
/// </summary>
public class UptimeHealthCheck : IHealthCheck
{
    public string Name => "uptime_check";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken())
    {
        TimeSpan runtime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
        int upTimeValue = (runtime.Days * 3600) + (runtime.Minutes * 60) + runtime.Seconds;

        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { "Uptime, sec", upTimeValue }
        };

        HealthStatus status = HealthStatus.Healthy;

        return Task.FromResult(new HealthCheckResult(status, AppDomain.CurrentDomain.FriendlyName + " is running.", null, data));
    }
}
