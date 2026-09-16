using System.Security.Claims;
using Delobytes.App.Backend.Application.Behaviours;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantLegalEntity;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests for the authorization contract of the legal entity update command (stage 10).
/// The command declares its allowed roles through IRequireRole, and the MediatR
/// pipeline behaviour enforces them independently of the controller.
/// </summary>
public class UpdateTenantLegalEntityAuthorizationTests
{
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly DefaultHttpContext _httpContext;
    private readonly Mock<RequestHandlerDelegate<UpdateTenantLegalEntityResponse>> _nextMock;

    public UpdateTenantLegalEntityAuthorizationTests()
    {
        _httpContext = new DefaultHttpContext();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(_httpContext);

        _nextMock = new Mock<RequestHandlerDelegate<UpdateTenantLegalEntityResponse>>();
        _nextMock
            .Setup(x => x())
            .ReturnsAsync(new UpdateTenantLegalEntityResponse());
    }

    private AuthorizationBehaviour<UpdateTenantLegalEntityCommand, UpdateTenantLegalEntityResponse> CreateBehaviour() =>
        new AuthorizationBehaviour<UpdateTenantLegalEntityCommand, UpdateTenantLegalEntityResponse>(
            _httpContextAccessorMock.Object,
            NullLogger<AuthorizationBehaviour<UpdateTenantLegalEntityCommand, UpdateTenantLegalEntityResponse>>.Instance);

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

    private static UpdateTenantLegalEntityCommand BuildCommand() => new UpdateTenantLegalEntityCommand
    {
        TenantId = Guid.NewGuid(),
        TaxType = TaxType.Usn,
        TaxRatePercent = 6m,
        VatType = VatType.None,
    };

    [Fact]
    public void Command_DeclaresAdministratorAsTheOnlyAllowedRole()
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildCommand();

        // Act & Assert
        command.AllowedRoles.Should().ContainSingle()
            .Which.Should().Be(Role.Administrator);
    }

    [Fact]
    public async Task Handle_AdministratorRole_AllowsExecution()
    {
        // Arrange
        AuthorizationBehaviour<UpdateTenantLegalEntityCommand, UpdateTenantLegalEntityResponse> behaviour = CreateBehaviour();
        SetupAuthenticatedUser(Role.Administrator);

        // Act
        await behaviour.Handle(BuildCommand(), _nextMock.Object, CancellationToken.None);

        // Assert
        _nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Handle_ManagerRole_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        AuthorizationBehaviour<UpdateTenantLegalEntityCommand, UpdateTenantLegalEntityResponse> behaviour = CreateBehaviour();
        SetupAuthenticatedUser(Role.Manager);

        // Act
        Func<Task> act = async () => await behaviour.Handle(BuildCommand(), _nextMock.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*недостаточно прав*");
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_ReadOnlyRole_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        AuthorizationBehaviour<UpdateTenantLegalEntityCommand, UpdateTenantLegalEntityResponse> behaviour = CreateBehaviour();
        SetupAuthenticatedUser(Role.ReadOnly);

        // Act
        Func<Task> act = async () => await behaviour.Handle(BuildCommand(), _nextMock.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*недостаточно прав*");
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        AuthorizationBehaviour<UpdateTenantLegalEntityCommand, UpdateTenantLegalEntityResponse> behaviour = CreateBehaviour();
        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        Func<Task> act = async () => await behaviour.Handle(BuildCommand(), _nextMock.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*не аутентифицирован*");
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_MissingRoleClaim_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        AuthorizationBehaviour<UpdateTenantLegalEntityCommand, UpdateTenantLegalEntityResponse> behaviour = CreateBehaviour();
        SetupAuthenticatedUser(null);

        // Act
        Func<Task> act = async () => await behaviour.Handle(BuildCommand(), _nextMock.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Роль пользователя не определена*");
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_UnparsableRoleClaim_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        AuthorizationBehaviour<UpdateTenantLegalEntityCommand, UpdateTenantLegalEntityResponse> behaviour = CreateBehaviour();

        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("role", "SuperUser"),
            },
            "TestAuth"));

        // Act
        Func<Task> act = async () => await behaviour.Handle(BuildCommand(), _nextMock.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Недопустимое значение роли*");
        _nextMock.Verify(x => x(), Times.Never);
    }
}
