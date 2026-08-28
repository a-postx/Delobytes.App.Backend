using System.Globalization;
using Delobytes.AspNetCore.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Delobytes.App.Backend.Infrastructure.Health;

/// <summary>
/// Публикатор для регулярной засылки метрик в ЕЛК.
/// </summary>
public class MetricsPublisher : IHealthCheckPublisher
{
    public MetricsPublisher(ILogger<MetricsPublisher> logger)
    {
        _log = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private readonly ILogger<MetricsPublisher> _log;

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        Dictionary<string, object> metrics = new Dictionary<string, object>();

        DateTimeOffset currentDateTime = DateTimeOffset.UtcNow;

        metrics.Add("TDay", currentDateTime.ToString("MM-dd", CultureInfo.InvariantCulture));
        metrics.Add("THour", currentDateTime.ToString("HH", CultureInfo.InvariantCulture));
        metrics.Add("TMin", currentDateTime.ToString("mm", CultureInfo.InvariantCulture));

        foreach (KeyValuePair<string, HealthReportEntry> healthEntry in report.Entries)
        {
            if (healthEntry.Value.Tags.Contains("metric"))
            {
                metrics.Add(healthEntry.Key + "MSec", healthEntry.Value.Duration.TotalMilliseconds);
            }
        }

        if (report.Entries.TryGetValue(HealthCheckNames.Memory, out HealthReportEntry memoryEntry))
        {
            foreach (KeyValuePair<string, object> item in memoryEntry.Data)
            {
                metrics.Add(item.Key, item.Value);
            }
        }

        (string, object?)[] metricsArray = new (string, object?)[metrics.Count + 2];

        int i = 0;

        foreach (KeyValuePair<string, object> metric in metrics)
        {
            metricsArray.SetValue((metric.Key, metric.Value), i);
            i++;
        }
#pragma warning disable IDE0004 // Приведение необходимо
        metricsArray.SetValue(("HealthCheckResult", (object)report.Status), i++);
        metricsArray.SetValue(("HealthCheckTotalDurationMSec", (object)report.TotalDuration.TotalMilliseconds), i++);
#pragma warning restore IDE0004

        using (_log.BeginScopeWith(metricsArray))
        {
            _log.LogInformation("Metrics published.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}
