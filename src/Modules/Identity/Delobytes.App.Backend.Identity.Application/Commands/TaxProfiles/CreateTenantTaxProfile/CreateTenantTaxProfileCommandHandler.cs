using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.CreateTenantTaxProfile;

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
