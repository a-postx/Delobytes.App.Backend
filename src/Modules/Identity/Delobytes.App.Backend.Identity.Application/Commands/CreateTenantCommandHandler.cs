using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Options;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to create a new tenant for a user.
/// </summary>
public class CreateTenantCommand : IRequest<CreateTenantResponse>
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the tenant name.
    /// </summary>
    public string TenantName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the current active tenant identifier.
    /// Required when creating additional tenant (must be Administrator).
    /// Null for first-time tenant creation.
    /// </summary>
    public Guid? CurrentTenantId { get; set; }
}

/// <summary>
/// Response from CreateTenant command.
/// </summary>
public class CreateTenantResponse
{
    /// <summary>
    /// Gets or sets the created tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the JWT access token with tenant context.
    /// </summary>
    public string AccessToken { get; set; } = default!;
}

/// <summary>
/// Handler for CreateTenantCommand.
/// </summary>
public class CreateTenantCommandHandler : IRequestHandler<CreateTenantCommand, CreateTenantResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly MultitenancyOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateTenantCommandHandler"/> class.
    /// </summary>
    public CreateTenantCommandHandler(
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        ITenantMembershipRepository membershipRepository,
        IJwtTokenService jwtTokenService,
        IOptions<MultitenancyOptions> options)
    {
        _userRepository = userRepository;
        _tenantRepository = tenantRepository;
        _membershipRepository = membershipRepository;
        _jwtTokenService = jwtTokenService;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public async Task<CreateTenantResponse> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.FindByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            throw new InvalidOperationException($"Пользователь с ID {request.UserId} не найден.");
        }

        int currentMembershipsCount = await _membershipRepository.CountActiveByUserAsync(request.UserId, cancellationToken);

        if (currentMembershipsCount >= _options.MaxTenantsPerUser)
        {
            throw new InvalidOperationException(
                $"Достигнут максимальный лимит пространств ({_options.MaxTenantsPerUser}).");
        }

        // If user already has memberships, check they are Administrator in current tenant
        if (currentMembershipsCount > 0)
        {
            if (request.CurrentTenantId == null)
            {
                throw new InvalidOperationException("Для создания дополнительного пространства необходимо указать текущий активный тенант.");
            }

            TenantMembership? currentMembership = await _membershipRepository
                .FindActiveByUserAndTenantAsync(request.UserId, request.CurrentTenantId.Value, cancellationToken);

            if (currentMembership == null)
            {
                throw new InvalidOperationException("Пользователь не состоит в указанном текущем пространстве.");
            }

            if (currentMembership.Role != Role.Administrator)
            {
                throw new InvalidOperationException(
                    "Только Администратор текущего пространства может создавать дополнительные пространства.");
            }
        }

        Tenant tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.TenantName,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            MaxBackgroundJobsParallelism = _options.DefaultMaxBackgroundJobsParallelism,
        };

        _tenantRepository.Add(tenant);

        TenantMembership membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            TenantId = tenant.Id,
            Role = Role.Administrator,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _membershipRepository.Add(membership);

        // Both repositories share the same DbContext unit of work,
        // so one SaveChanges call persists both the tenant and the membership.
        await _tenantRepository.SaveChangesAsync(cancellationToken);

        string token = _jwtTokenService.GenerateToken(request.UserId, tenant.Id, Role.Administrator);

        return new CreateTenantResponse
        {
            TenantId = tenant.Id,
            AccessToken = token,
        };
    }
}
