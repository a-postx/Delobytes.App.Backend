using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Period diagnostics.</summary>
public sealed class GetPnlDiagnosticsQuery : IRequest<PnlDiagnosticsResult>
{
    /// <summary>
    /// Gets or sets the optional channel filter. Without it the diagnostics cover the whole tenant,
    /// which is not the population a per-channel summary describes.
    /// </summary>
    public Guid? ChannelId { get; set; }

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
        // Reported as a field error instead of an argument error: an impossible period is a client
        // mistake and must answer 400, while an unhandled ArgumentException became a 500.
        if (request.From > request.To)
        {
            throw new ValidationException(
                new[]
                {
                    new ValidationFailure("from", "Начало периода не может быть позже его конца."),
                });
        }

        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        // The reporting currency is the tenant's accounting currency, the same one the summary
        // reports: the two results are compared with each other.
        TenantAccountingSettings settings =
            await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);

        return await _store.GetDiagnosticsAsync(
            settings.Currency,
            request.ChannelId,
            request.From,
            request.To,
            cancellationToken);
    }
}
