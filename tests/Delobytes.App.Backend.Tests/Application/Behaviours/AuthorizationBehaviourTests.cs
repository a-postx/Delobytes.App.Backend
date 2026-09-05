using System.Security.Claims;
using Delobytes.App.Backend.Application.Behaviours;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Delobytes.App.Backend.Tests.Application.Behaviours;

public class AuthorizationBehaviourTests
{
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private DefaultHttpContext _httpContext;

    public AuthorizationBehaviourTests()
    {
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(_httpContext);
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
    public async Task Handle_AdministratorCommand_WithManagerRole_ShouldThrowUnauthorizedAccessException()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        SetupHttpContextWithRole(Role.Manager);

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*недостаточно прав*");
        nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_AdministratorCommand_WithReadOnlyRole_ShouldThrowUnauthorizedAccessException()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        SetupHttpContextWithRole(Role.ReadOnly);

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*недостаточно прав*");
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
    public async Task Handle_ManagerOrHigherCommand_WithReadOnlyRole_ShouldThrowUnauthorizedAccessException()
    {
        AuthorizationBehaviour<AuthTestManagerCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestManagerCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestManagerCommand command = new AuthTestManagerCommand();
        SetupHttpContextWithRole(Role.ReadOnly);

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        nextMock.Verify(x => x(), Times.Never);
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
    public async Task Handle_MissingRoleClaim_ShouldThrowUnauthorizedAccessException()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        SetupHttpContextWithoutRoleClaim();

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Роль пользователя не определена*");
        nextMock.Verify(x => x(), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidRoleValue_ShouldThrowUnauthorizedAccessException()
    {
        AuthorizationBehaviour<AuthTestCommand, AuthTestResponse> behaviour = CreateBehaviour<AuthTestCommand>();
        Mock<RequestHandlerDelegate<AuthTestResponse>> nextMock = CreateNextMock();
        AuthTestCommand command = new AuthTestCommand();
        SetupHttpContextWithInvalidRole();

        Func<Task> act = async () => await behaviour.Handle(command, nextMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Недопустимое значение роли*");
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
        ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("userId", Guid.NewGuid().ToString()),
            new Claim("tenantId", Guid.NewGuid().ToString()),
            new Claim("role", role.ToString()),
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
            new Claim("userId", Guid.NewGuid().ToString()),
            new Claim("tenantId", Guid.NewGuid().ToString()),
        }, "TestAuth"));

        _httpContext.User = user;
    }

    private void SetupHttpContextWithInvalidRole()
    {
        ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("userId", Guid.NewGuid().ToString()),
            new Claim("tenantId", Guid.NewGuid().ToString()),
            new Claim("role", "InvalidRole"),
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

public class AuthTestCommandWithoutRole : IRequest<AuthTestResponse>
{
}
