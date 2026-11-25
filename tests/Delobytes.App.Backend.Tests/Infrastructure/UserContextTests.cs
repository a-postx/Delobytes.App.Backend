using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Verifies how the JWT "sub" claim is turned into <see cref="IUserContext.UserId"/>.
/// Controllers no longer parse claims themselves, so this is the only place the
/// claim-to-Guid mapping is exercised.
/// </summary>
public class UserContextTests
{
    private static UserContext BuildContext(HttpContext? httpContext, MessageUserContext messageUserContext)
    {
        Mock<IHttpContextAccessor> accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        return new UserContext(accessorMock.Object, messageUserContext);
    }

    private static DefaultHttpContext BuildHttpContext(params System.Security.Claims.Claim[] claims)
    {
        DefaultHttpContext httpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(claims, "TestAuth")),
        };

        return httpContext;
    }

    [Fact]
    public void UserId_WithValidSubClaim_ReturnsParsedGuid()
    {
        Guid userId = Guid.NewGuid();
        UserContext context = BuildContext(
            BuildHttpContext(new System.Security.Claims.Claim(JwtClaimNames.Subject, userId.ToString())),
            new MessageUserContext());

        context.UserId.Should().Be(userId);
    }

    [Fact]
    public void UserId_WithNameIdentifierClaim_ReturnsParsedGuid()
    {
        Guid userId = Guid.NewGuid();
        UserContext context = BuildContext(
            BuildHttpContext(new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())),
            new MessageUserContext());

        context.UserId.Should().Be(userId);
    }

    [Fact]
    public void UserId_WithMalformedSubClaim_FallsBackToMessageContext()
    {
        Guid messageUserId = Guid.NewGuid();
        MessageUserContext messageUserContext = new MessageUserContext();
        messageUserContext.SetUserId(messageUserId);

        try
        {
            UserContext context = BuildContext(
                BuildHttpContext(new System.Security.Claims.Claim(JwtClaimNames.Subject, "not-a-guid")),
                messageUserContext);

            context.UserId.Should().Be(messageUserId);
        }
        finally
        {
            messageUserContext.Clear();
        }
    }

    [Fact]
    public void UserId_WithoutAnySource_ReturnsNull()
    {
        UserContext context = BuildContext(BuildHttpContext(), new MessageUserContext());

        context.UserId.Should().BeNull();
    }

    [Fact]
    public void UserId_WithNullHttpContext_ReturnsMessageUserId()
    {
        Guid messageUserId = Guid.NewGuid();
        MessageUserContext messageUserContext = new MessageUserContext();
        messageUserContext.SetUserId(messageUserId);

        try
        {
            UserContext context = BuildContext(null, messageUserContext);

            context.UserId.Should().Be(messageUserId);
        }
        finally
        {
            messageUserContext.Clear();
        }
    }
}
