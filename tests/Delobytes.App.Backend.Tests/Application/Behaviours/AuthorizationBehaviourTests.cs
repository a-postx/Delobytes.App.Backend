using System.Security.Claims;
using Delobytes.App.Backend.Application.Behaviours;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Delobytes.App.Backend.Tests.Application.Behaviours;

public class AuthorizationBehaviourTests
{
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<IUserContext> _userContextMock;
    private readonly Mock<ITenantContext> _tenantContextMock;
    private DefaultHttpContext _httpContext;

    public AuthorizationBehaviourTests()
    {
        _httpContext = new DefaultHttpContext();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(_httpContext);
        _userContextMock = new Mock<IUserContext>();
        _tenantContextMock = new Mock<ITenantContext>();
    }

    [Fact]
    public async Task Handle_CommandWithoutIRequireRole_ShouldCallNext()
    {
        AuthorizationBehaviour<AuthTestCommandWithoutRole, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommandWithoutRole>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommandWithoutRole command = new AuthTestCommandWithoutRole();

        AuthTestResponse result = await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        result.Success.Should().BeTrue();
        nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Handle_AdministratorCommand_WithAdministratorRole_ShouldSucceed()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        SetupHttpContextWithRole(Role.Administrator);

        AuthTestResponse result = await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        result.Success.Should().BeTrue();
        nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Handle_AdministratorCommand_WithManagerRole_ShouldThrowForbidden()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        SetupHttpContextWithRole(Role.Manager);

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        AppException exception = (await act.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.Common.Forbidden);
        exception.Message.Should().Contain("недостаточно прав");
        nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_AdministratorCommand_WithReadOnlyRole_ShouldThrowForbidden()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        SetupHttpContextWithRole(Role.ReadOnly);

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        AppException exception = (await act.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.Common.Forbidden);
        nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_ManagerOrHigherCommand_WithAdministratorRole_ShouldSucceed()
    {
        AuthorizationBehaviour<AuthTestManagerCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestManagerCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestManagerCommand command = new AuthTestManagerCommand();
        SetupHttpContextWithRole(Role.Administrator);

        AuthTestResponse result = await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        result.Success.Should().BeTrue();
        nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Handle_ManagerOrHigherCommand_WithManagerRole_ShouldSucceed()
    {
        AuthorizationBehaviour<AuthTestManagerCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestManagerCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestManagerCommand command = new AuthTestManagerCommand();
        SetupHttpContextWithRole(Role.Manager);

        AuthTestResponse result = await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        result.Success.Should().BeTrue();
        nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Handle_ManagerOrHigherCommand_WithReadOnlyRole_ShouldThrowForbidden()
    {
        AuthorizationBehaviour<AuthTestManagerCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestManagerCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestManagerCommand command = new AuthTestManagerCommand();
        SetupHttpContextWithRole(Role.ReadOnly);

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        AppException exception = (await act.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.Common.Forbidden);
        nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_CommandRestrictedToManager_WithAdministratorRole_ShouldThrowForbidden()
    {
        // Locks in Contracts.Authorization.IRequireRole semantics: AllowedRoles is authoritative,
        // Administrator receives no implicit bypass, so a request can exclude it deliberately.
        AuthorizationBehaviour<AuthTestManagerOnlyCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestManagerOnlyCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestManagerOnlyCommand command = new AuthTestManagerOnlyCommand();
        SetupHttpContextWithRole(Role.Administrator);

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        AppException exception = (await act.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.Common.Forbidden);
        exception.Message.Should().Contain("недостаточно прав");
        nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_CommandRestrictedToManager_WithManagerRole_ShouldSucceed()
    {
        AuthorizationBehaviour<AuthTestManagerOnlyCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestManagerOnlyCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestManagerOnlyCommand command = new AuthTestManagerOnlyCommand();
        SetupHttpContextWithRole(Role.Manager);

        AuthTestResponse result = await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        result.Success.Should().BeTrue();
        nextMock.Verify(x => x(), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ShouldThrowUnauthorizedAccessException()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        SetupHttpContextUnauthenticated();

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*не аутентифицирован*");
        nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_MissingRoleClaim_ShouldThrowForbidden()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        SetupHttpContextWithoutRoleClaim();

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        AppException exception = (await act.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.Common.Forbidden);
        exception.Message.Should().Contain("Роль пользователя не определена");
        nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidRoleValue_ShouldThrowForbidden()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        SetupHttpContextWithInvalidRole();

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        AppException exception = (await act.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.Common.Forbidden);
        exception.Message.Should().Contain("Недопустимое значение роли");
        nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_NullHttpContext_ShouldThrowUnauthorizedAccessException()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        nextMock.Verify(x => x(), Times.Never);
    }

    private AuthorizationBehaviour<TCommand, AuthTestResponse> CreateBehaviour<TCommand>()
        where TCommand : notnull
    {
        return new AuthorizationBehaviour<TCommand, AuthTestResponse>(
            _httpContextAccessorMock.Object,
            _userContextMock.Object,
            _tenantContextMock.Object,
            NullLogger<AuthorizationBehaviour<TCommand, AuthTestResponse>>.Instance);
    }

    private Mock<RequestHandlerDelegate<AuthTestResponse>> CreateNextMock()
    {
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = new Mock<RequestHandlerDelegate<AuthTestResponse>>();
        nextMock.Setup(x => x()).ReturnsAsync(new AuthTestResponse { Success = true });
        return nextMock;
    }

    private void SetupHttpContextWithRole(Role role)
    {
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        _userContextMock.SetupGet(x => x.UserId).Returns(userId);
        _tenantContextMock.SetupGet(x => x.TenantId).Returns(tenantId);

        ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, userId.ToString()),
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.Role, role.ToString()),
        }, "TestAuth"));

        _httpContext.User = user;
    }

    private void SetupHttpContextUnauthenticated()
    {
        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
    }

    private void SetupHttpContextWithoutRoleClaim()
    {
        ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.TenantId, Guid.NewGuid().ToString()),
        }, "TestAuth"));

        _httpContext.User = user;
    }

    private void SetupHttpContextWithInvalidRole()
    {
        ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.TenantId, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Role, "InvalidRole"),
        }, "TestAuth"));

        _httpContext.User = user;
    }
}

// Вынесены на уровень namespace — Castle.DynamicProxy требует публичного доступа
// к типам, используемым в generic-параметрах Mock<T>.
public class AuthTestResponse
{
    public bool Success { get; set; }
}

public class AuthTestCommand : IRequest<AuthTestResponse>, IRequireRole
{
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

public class AuthTestManagerCommand : IRequest<AuthTestResponse>, IRequireRole
{
    public Role[] AllowedRoles => new[] { Role.Administrator, Role.Manager };
}

public class AuthTestManagerOnlyCommand : IRequest<AuthTestResponse>, IRequireRole
{
    public Role[] AllowedRoles => new[] { Role.Manager };
}

public class AuthTestCommandWithoutRole : IRequest<AuthTestResponse>
{
}
