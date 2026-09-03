using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries.GetCurrentUser;

/// <summary>
/// Handler for GetCurrentUserQuery.
/// </summary>
public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, GetCurrentUserResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantMembershipRepository _membershipRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetCurrentUserQueryHandler"/> class.
    /// </summary>
    public GetCurrentUserQueryHandler(
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        ITenantMembershipRepository membershipRepository)
    {
        _userRepository = userRepository;
        _tenantRepository = tenantRepository;
        _membershipRepository = membershipRepository;
    }

    /// <inheritdoc/>
    public async Task<GetCurrentUserResponse> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        User user = await _userRepository.FindByIdAsync(request.UserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Пользователь не найден.");

        Tenant tenant = await _tenantRepository.FindByIdAsync(request.TenantId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Тенант не найден.");

        TenantMembership? currentMembership = await _membershipRepository
            .FindActiveByUserAndTenantAsync(request.UserId, request.TenantId, cancellationToken);

        string role = currentMembership?.Role.ToString() ?? "Unknown";

        IReadOnlyList<TenantMembership> allMemberships = await _membershipRepository
            .GetActiveByUserAsync(request.UserId, cancellationToken);

        return new GetCurrentUserResponse
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            TenantId = tenant.Id,
            TenantName = tenant.Name,
            Role = role,
            Tenants = allMemberships.Select(m => new UserTenantInfo
            {
                TenantId = m.TenantId,
                TenantName = m.Tenant.Name,
                Role = m.Role.ToString(),
            }).ToList(),
        };
    }
}
