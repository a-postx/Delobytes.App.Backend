using MediatR;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles;

/// <summary>
/// Запрос списка версий налогового профиля тенанта.
/// </summary>
public class GetTenantTaxProfilesQuery : IRequest<GetTenantTaxProfilesResponse>
{
    /// <summary>
    /// Gets or sets идентификатор тенанта.
    /// </summary>
    public Guid TenantId { get; set; }
}

/// <summary>
/// Ответ на запрос списка налоговых профилей тенанта.
/// </summary>
public class GetTenantTaxProfilesResponse
{
    /// <summary>
    /// Gets or sets версии профиля, отсортированные по дате начала по убыванию.
    /// </summary>
    public List<TenantTaxProfileItem> Items { get; set; } = new List<TenantTaxProfileItem>();
}

/// <summary>
/// Версия налогового профиля в списке.
/// </summary>
public class TenantTaxProfileItem
{
    /// <summary>
    /// Gets or sets идентификатор версии профиля.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets налоговый режим.
    /// </summary>
    public TaxRegime Regime { get; set; }

    /// <summary>
    /// Gets or sets ставка налога в процентах.
    /// </summary>
    public decimal RatePercent { get; set; }

    /// <summary>
    /// Gets or sets режим НДС.
    /// </summary>
    public VatType Vat { get; set; }

    /// <summary>
    /// Gets or sets дата начала действия версии.
    /// </summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>
    /// Gets or sets момент создания записи.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets идентификатор создателя записи.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// Handler for GetTenantTaxProfilesQuery.
/// </summary>
public class GetTenantTaxProfilesQueryHandler : IRequestHandler<GetTenantTaxProfilesQuery, GetTenantTaxProfilesResponse>
{
    private readonly ITenantTaxProfileRepository _taxProfileRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTenantTaxProfilesQueryHandler"/> class.
    /// </summary>
    public GetTenantTaxProfilesQueryHandler(ITenantTaxProfileRepository taxProfileRepository)
    {
        _taxProfileRepository = taxProfileRepository;
    }

    /// <inheritdoc/>
    public async Task<GetTenantTaxProfilesResponse> Handle(
        GetTenantTaxProfilesQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantTaxProfile> profiles = await _taxProfileRepository.GetByTenantAsync(request.TenantId, cancellationToken);

        return new GetTenantTaxProfilesResponse
        {
            Items = profiles.Select(p => new TenantTaxProfileItem
            {
                Id = p.Id,
                Regime = p.Regime,
                RatePercent = p.RatePercent,
                Vat = p.Vat,
                ValidFrom = p.ValidFrom,
                CreatedAt = p.CreatedAt,
                CreatedByUserId = p.CreatedByUserId,
            }).ToList(),
        };
    }
}
