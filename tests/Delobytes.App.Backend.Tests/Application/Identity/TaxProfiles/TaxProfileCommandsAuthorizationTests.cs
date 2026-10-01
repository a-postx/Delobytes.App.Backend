using System.Security.Claims;
using Delobytes.App.Backend.Application.Behaviours;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.CreateTenantTaxProfile;
using Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.DeleteTenantTaxProfile;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.TaxProfiles;

/// <summary>
/// Authorization tests for the tax profile commands.
///
/// Both commands declare Administrator through IRequireRole, and the MediatR pipeline
/// behaviour enforces that independently of the controller, so a request that somehow
/// bypasses the controller is still refused.
/// </summary>
public class TaxProfileCommandsAuthorizationTests
{
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly DefaultHttpContext _httpContext;

    public TaxProfileCommandsAuthorizationTests()
    {
        _httpContext = new DefaultHttpContext();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(_httpContext);
    }

    private void SetupAuthenticatedUser(Role? role)
    {
        List<Claim> claims = new List<Claim>
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tenantId", Guid.NewGuid().ToString()),
        };

        if (role.HasValue)
        {
            claims.Add(new Claim("role", role.Value.ToString()));
        }

        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static CreateTenantTaxProfileCommand BuildCreateCommand() => new CreateTenantTaxProfileCommand
    {
        TenantId = Guid.NewGuid(),
        Regime = TaxRegime.UsnIncome,
        RatePercent = 6m,
        Vat = VatType.None,
        ValidFrom = new DateOnly(2026, 1, 1),
    };

    private static DeleteTenantTaxProfileCommand BuildDeleteCommand() => new DeleteTenantTaxProfileCommand
    {
        TenantId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
    };

    private AuthorizationBehaviour<CreateTenantTaxProfileCommand, CreateTenantTaxProfileResponse> CreateCreateBehaviour() =>
        new AuthorizationBehaviour<CreateTenantTaxProfileCommand, CreateTenantTaxProfileResponse>(
            _httpContextAccessorMock.Object,
            NullLogger<AuthorizationBehaviour<CreateTenantTaxProfileCommand, CreateTenantTaxProfileResponse>>.Instance);

    private AuthorizationBehaviour<DeleteTenantTaxProfileCommand, DeleteTenantTaxProfileResponse> CreateDeleteBehaviour() =>
        new AuthorizationBehaviour<DeleteTenantTaxProfileCommand, DeleteTenantTaxProfileResponse>(
            _httpContextAccessorMock.Object,
            NullLogger<AuthorizationBehaviour<DeleteTenantTaxProfileCommand, DeleteTenantTaxProfileResponse>>.Instance);

    [Fact]
    public void BothCommands_DeclareAdministratorAsTheOnlyAllowedRole()
    {
        // Act & Assert
        BuildCreateCommand().AllowedRoles.Should().ContainSingle()
            .Which.Should().Be(Role.Administrator);

        BuildDeleteCommand().AllowedRoles.Should().ContainSingle()
            .Which.Should().Be(Role.Administrator);
    }

    [Theory]
    [InlineData(Role.Manager)]
    [InlineData(Role.ReadOnly)]
    public async Task Create_NonAdministratorRole_IsRefused(Role role)
    {
        // Arrange
        AuthorizationBehaviour<CreateTenantTaxProfileCommand, CreateTenantTaxProfileResponse> behaviour =
            CreateCreateBehaviour();
        SetupAuthenticatedUser(role);

        Mock<RequestHandlerDelegate<CreateTenantTaxProfileResponse>> nextMock = new Mock<RequestHandlerDelegate<CreateTenantTaxProfileResponse>>();
        nextMock.Setup(x => x()).ReturnsAsync(new CreateTenantTaxProfileResponse());

        // Act
        Func<Task> act = async () => await behaviour.Handle(BuildCreateCommand(), nextMock.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*недостаточно прав*");
        nextMock.Verify(x => x(), Times.Never);
    }

    [Theory]
    [InlineData(Role.Manager)]
    [InlineData(Role.ReadOnly)]
    public async Task Delete_NonAdministratorRole_IsRefused(Role role)
    {
        // Arrange
        AuthorizationBehaviour<DeleteTenantTaxProfileCommand, DeleteTenantTaxProfileResponse> behaviour =
            CreateDeleteBehaviour();
        SetupAuthenticatedUser(role);

        Mock<RequestHandlerDelegate<DeleteTenantTaxProfileResponse>> nextMock = new Mock<RequestHandlerDelegate<DeleteTenantTaxProfileResponse>>();
        nextMock.Setup(x => x()).ReturnsAsync(new DeleteTenantTaxProfileResponse());

        // Act
        Func<Task> act = async () => await behaviour.Handle(BuildDeleteCommand(), nextMock.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*недостаточно прав*");
        nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Create_AdministratorRole_IsAllowed()
    {
        // Arrange
        AuthorizationBehaviour<CreateTenantTaxProfileCommand, CreateTenantTaxProfileResponse> behaviour =
            CreateCreateBehaviour();
        SetupAuthenticatedUser(Role.Administrator);

        Mock<RequestHandlerDelegate<CreateTenantTaxProfileResponse>> nextMock = new Mock<RequestHandlerDelegate<CreateTenantTaxProfileResponse>>();
        nextMock.Setup(x => x()).ReturnsAsync(new CreateTenantTaxProfileResponse());

        // Act
        await behaviour.Handle(BuildCreateCommand(), nextMock.Object, CancellationToken.None);

        // Assert
        nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Create_UnauthenticatedUser_IsRefused()
    {
        // Arrange
        AuthorizationBehaviour<CreateTenantTaxProfileCommand, CreateTenantTaxProfileResponse> behaviour =
            CreateCreateBehaviour();
        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        Mock<RequestHandlerDelegate<CreateTenantTaxProfileResponse>> nextMock = new Mock<RequestHandlerDelegate<CreateTenantTaxProfileResponse>>();
        nextMock.Setup(x => x()).ReturnsAsync(new CreateTenantTaxProfileResponse());

        // Act
        Func<Task> act = async () => await behaviour.Handle(BuildCreateCommand(), nextMock.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*не аутентифицирован*");
        nextMock.Verify(x => x(), Times.Never);
    }
}
