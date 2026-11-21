using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Period diagnostics.</summary>
public sealed class GetPnlDiagnosticsQuery : IRequest<PnlDiagnosticsResult>
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }
}

/// <summary>Handles <see cref="GetPnlDiagnosticsQuery"/>.</summary>
public sealed class GetPnlDiagnosticsQueryHandler : IRequestHandler<GetPnlDiagnosticsQuery, PnlDiagnosticsResult>
{
    private readonly IPnlReadStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _settingsProvider;

    public GetPnlDiagnosticsQueryHandler(
        IPnlReadStore store,
        ITenantContext tenantContext,
        ITenantSettingsProvider settingsProvider)
    {
        _store = store;
        _tenantContext = tenantContext;
        _settingsProvider = settingsProvider;
    }

    /// <inheritdoc/>
    public async Task<PnlDiagnosticsResult> Handle(
        GetPnlDiagnosticsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.From > request.To)
        {
            throw new ArgumentException("The period start must not be after its end.", nameof(request));
        }

        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        // The reporting currency is the tenant's accounting currency, the same one the summary
        // reports: the two results are compared with each other.
        TenantAccountingSettings settings =
            await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);

        return await _store.GetDiagnosticsAsync(
            settings.Currency,
            request.From,
            request.To,
            cancellationToken);
    }
}
