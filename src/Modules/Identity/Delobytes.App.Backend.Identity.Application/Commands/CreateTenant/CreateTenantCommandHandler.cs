using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;

/// <summary>
/// Handler for CreateTenantCommand.
/// </summary>
public class CreateTenantCommandHandler : IRequestHandler<CreateTenantCommand, CreateTenantResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IJwtTokenService _jwtTokenService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateTenantCommandHandler"/> class.
    /// </summary>
    public CreateTenantCommandHandler(
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        ITenantMembershipRepository membershipRepository,
        IJwtTokenService jwtTokenService)
    {
        _userRepository = userRepository;
        _tenantRepository = tenantRepository;
        _membershipRepository = membershipRepository;
        _jwtTokenService = jwtTokenService;
    }

    /// <inheritdoc/>
    public async Task<CreateTenantResponse> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.FindByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            throw new InvalidOperationException($"User with ID {request.UserId} not found.");
        }

        var alreadyHasMembership = await _membershipRepository.ExistsForUserAsync(request.UserId, cancellationToken);

        if (alreadyHasMembership)
        {
            throw new InvalidOperationException("User already has a tenant membership.");
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.TenantName,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _tenantRepository.Add(tenant);

        var membership = new TenantMembership
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

        var token = _jwtTokenService.GenerateToken(request.UserId, tenant.Id, Role.Administrator);

        return new CreateTenantResponse
        {
            TenantId = tenant.Id,
            AccessToken = token,
        };
    }
}
