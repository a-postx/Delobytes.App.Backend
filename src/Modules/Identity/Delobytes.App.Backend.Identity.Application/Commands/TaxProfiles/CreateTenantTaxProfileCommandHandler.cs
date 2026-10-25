using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles;

/// <summary>
/// Команда создания новой версии налогового профиля тенанта.
/// </summary>
public class CreateTenantTaxProfileCommand : IRequest<CreateTenantTaxProfileResponse>, IRequireRole
{
    /// <summary>
    /// Gets or sets идентификатор тенанта.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets налоговый режим.
    /// </summary>
    public TaxRegime Regime { get; set; }

    /// <summary>
    /// Gets or sets ставка налога в процентах (0–100).
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

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>
/// Ответ на команду создания налогового профиля.
/// </summary>
public class CreateTenantTaxProfileResponse
{
    /// <summary>
    /// Gets or sets идентификатор созданной версии профиля.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets признак конфликта: дата начала не позже последней существующей версии.
    /// </summary>
    public bool Conflict { get; set; }
}

/// <summary>
/// Handler for CreateTenantTaxProfileCommand.
/// </summary>
public class CreateTenantTaxProfileCommandHandler : IRequestHandler<CreateTenantTaxProfileCommand, CreateTenantTaxProfileResponse>
{
    private readonly ITenantTaxProfileRepository _taxProfileRepository;
    private readonly IUserContext _userContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateTenantTaxProfileCommandHandler"/> class.
    /// </summary>
    public CreateTenantTaxProfileCommandHandler(
        ITenantTaxProfileRepository taxProfileRepository,
        IUserContext userContext)
    {
        _taxProfileRepository = taxProfileRepository;
        _userContext = userContext;
    }

    /// <inheritdoc/>
    public async Task<CreateTenantTaxProfileResponse> Handle(
        CreateTenantTaxProfileCommand request,
        CancellationToken cancellationToken)
    {
        TenantTaxProfile? latest = await _taxProfileRepository.GetLatestAsync(request.TenantId, cancellationToken);

        // Профиль неизменяем, поэтому исправить можно только «в будущее». Дата не позже
        // последней версии означала бы переписывание уже закрытого периода.
        if (latest != null && request.ValidFrom <= latest.ValidFrom)
        {
            return new CreateTenantTaxProfileResponse { Conflict = true };
        }

        TenantTaxProfile profile = new TenantTaxProfile
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Regime = request.Regime,
            RatePercent = request.RatePercent,
            Vat = request.Vat,
            ValidFrom = request.ValidFrom,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userContext.UserId,
        };

        _taxProfileRepository.Add(profile);
        await _taxProfileRepository.SaveChangesAsync(cancellationToken);

        return new CreateTenantTaxProfileResponse
        {
            Id = profile.Id,
        };
    }
}
