using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Expectation;
using Delobytes.App.Backend.Finance.Application.Interfaces;

namespace Delobytes.App.Backend.BackgroundServices;

/// <summary>
/// Periodic sweep of the expected-document open items. Discovers the tenants that have a document
/// past its window, then ages each tenant's expectations in its own scope, because every write is
/// tenant-scoped and a single failing tenant must not stop the others.
/// </summary>
public class ExpectationAgingHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExpectationAgingHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpectationAgingHostedService"/> class.
    /// </summary>
    /// <param name="scopeFactory">Scope factory used to create one tenant scope per pass.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public ExpectationAgingHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ExpectationAgingHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, _timeProvider, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using PeriodicTimer timer = new PeriodicTimer(Interval, _timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expectation aging pass failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        IReadOnlyList<Guid> tenantIds;

        using (IServiceScope lookupScope = _scopeFactory.CreateScope())
        {
            IPnlExpectationStore store = lookupScope.ServiceProvider.GetRequiredService<IPnlExpectationStore>();
            tenantIds = await store.LoadTenantIdsWithDueAsync(now, cancellationToken);
        }

        if (tenantIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Expectation aging pass covers {TenantCount} tenant(s).", tenantIds.Count);

        foreach (Guid tenantId in tenantIds)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenantId(tenantId);

                ExpectationStateUpdater updater =
                    scope.ServiceProvider.GetRequiredService<ExpectationStateUpdater>();

                await updater.RunAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One tenant's failure is logged and skipped: the sweep is periodic, and stopping
                // the whole pass would starve every tenant queued behind the broken one.
                _logger.LogError(
                    ex,
                    "Expectation aging pass failed for tenant {TenantId}.",
                    tenantId);
            }
        }
    }
}
