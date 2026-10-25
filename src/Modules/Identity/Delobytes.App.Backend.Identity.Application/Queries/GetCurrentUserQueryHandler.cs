using MediatR;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Queries;

/// <summary>
/// Query to retrieve the current authenticated user with their active tenant.
/// </summary>
public class GetCurrentUserQuery : IRequest<GetCurrentUserResponse>
{
    /// <summary>
    /// Gets or sets the user identifier extracted from JWT claims.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier extracted from JWT claims.
    /// </summary>
    public Guid TenantId { get; set; }
}

/// <summary>
/// Response returned by GetCurrentUserQuery.
/// </summary>
public class GetCurrentUserResponse
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the user display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the user email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the active tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the active tenant name.
    /// </summary>
    public string TenantName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the active tenant accounting currency (ISO 4217).
    /// </summary>
    public string Currency { get; set; } = default!;

    /// <summary>
    /// Gets or sets the active tenant time zone identifier (IANA).
    /// </summary>
    public string TimeZone { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user role in the active tenant.
    /// </summary>
    public string Role { get; set; } = default!;

    /// <summary>
    /// Gets or sets all tenants the user belongs to.
    /// </summary>
    public List<UserTenantInfo> Tenants { get; set; } = new List<UserTenantInfo>();
}

/// <summary>
/// Information about a tenant the user belongs to.
/// </summary>
public class UserTenantInfo
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the tenant name.
    /// </summary>
    public string TenantName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user role within this tenant.
    /// </summary>
    public string Role { get; set; } = default!;
}

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
            Currency = tenant.Currency,
            TimeZone = tenant.TimeZone,
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
