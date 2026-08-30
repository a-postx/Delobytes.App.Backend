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

    /// <summary>
    /// Initializes a new instance of the <see cref="GetCurrentUserQueryHandler"/> class.
    /// </summary>
    public GetCurrentUserQueryHandler(
        IUserRepository userRepository,
        ITenantRepository tenantRepository)
    {
        _userRepository = userRepository;
        _tenantRepository = tenantRepository;
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

        return new GetCurrentUserResponse
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            TenantId = tenant.Id,
            TenantName = tenant.Name,
        };
    }
}
