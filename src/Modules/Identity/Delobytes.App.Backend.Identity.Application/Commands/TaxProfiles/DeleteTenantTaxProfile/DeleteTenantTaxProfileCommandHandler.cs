using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.DeleteTenantTaxProfile;

/// <summary>
/// Handler for DeleteTenantTaxProfileCommand.
/// </summary>
public class DeleteTenantTaxProfileCommandHandler : IRequestHandler<DeleteTenantTaxProfileCommand, DeleteTenantTaxProfileResponse>
{
    private readonly ITenantTaxProfileRepository _taxProfileRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteTenantTaxProfileCommandHandler"/> class.
    /// </summary>
    public DeleteTenantTaxProfileCommandHandler(ITenantTaxProfileRepository taxProfileRepository)
    {
        _taxProfileRepository = taxProfileRepository;
    }

    /// <inheritdoc/>
    public async Task<DeleteTenantTaxProfileResponse> Handle(
        DeleteTenantTaxProfileCommand request,
        CancellationToken cancellationToken)
    {
        TenantTaxProfile? profile = await _taxProfileRepository.FindByIdAsync(request.TenantId, request.Id, cancellationToken);

        if (profile == null)
        {
            return new DeleteTenantTaxProfileResponse { Found = false };
        }

        TenantTaxProfile? latest = await _taxProfileRepository.GetLatestAsync(request.TenantId, cancellationToken);

        // Удаление не последней версии оставило бы дыру в истории: более ранний профиль
        // начал бы применяться к периоду, который уже посчитан по удалённому.
        if (latest != null && latest.Id != profile.Id)
        {
            return new DeleteTenantTaxProfileResponse { Found = true, NotLatest = true };
        }

        _taxProfileRepository.Remove(profile);
        await _taxProfileRepository.SaveChangesAsync(cancellationToken);

        return new DeleteTenantTaxProfileResponse { Found = true };
    }
}
