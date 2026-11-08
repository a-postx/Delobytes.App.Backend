using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ChannelParameterSetRepository : IChannelParameterSetRepository
{
    private readonly CatalogDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly TimeProvider _timeProvider;

    public ChannelParameterSetRepository(
        CatalogDbContext context,
        ITenantContext tenantContext,
        ITenantSettingsProvider settingsProvider,
        TimeProvider timeProvider)
    {
        _context = context;
        _tenantContext = tenantContext;
        _settingsProvider = settingsProvider;
        _timeProvider = timeProvider;
    }

    public Task<ChannelParameterSet?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.ChannelParameterSets
            .FirstOrDefaultAsync(cps => cps.Id == id, ct);
    }

    public Task<List<ChannelParameterSet>> GetByChannelIdAsync(Guid channelId, CancellationToken ct)
    {
        return _context.ChannelParameterSets
            .Where(cps => cps.ChannelId == channelId)
            .OrderByDescending(cps => cps.ValidFrom)
            .ToListAsync(ct);
    }

    public async Task<ChannelParameterSet?> GetActiveByChannelIdAsync(Guid channelId, CancellationToken ct)
    {
        DateOnly today = await GetTodayInTenantTimeZoneAsync(ct);

        return await _context.ChannelParameterSets
            .Where(cps => cps.ChannelId == channelId)
            .Where(cps => cps.ValidFrom <= today)
            .OrderByDescending(cps => cps.ValidFrom)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(ChannelParameterSet parameterSet)
    {
        _context.ChannelParameterSets.Add(parameterSet);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesWithConflictTranslationAsync(ct);
    }

    // ValidFrom is a tenant-local calendar date, so "today" must be read in the tenant's zone:
    // a UTC date lags by hours for tenants east of UTC and would keep the previous parameter
    // set active for part of the day.
    private async Task<DateOnly> GetTodayInTenantTimeZoneAsync(CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        TenantAccountingSettings settings = await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);
        TimeZoneInfo timeZone = ResolveTimeZone(settings.TimeZoneId);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), timeZone).DateTime);
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException($"Часовой пояс '{timeZoneId}' не найден в системе.", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new InvalidOperationException($"Часовой пояс '{timeZoneId}' содержит некорректные данные.", ex);
        }
    }
}
