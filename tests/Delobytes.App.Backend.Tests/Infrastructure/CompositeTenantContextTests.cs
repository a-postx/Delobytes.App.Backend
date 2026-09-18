using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Security.Claims;

namespace Delobytes.App.Backend.Tests.Messaging;

public class CompositeTenantContextTests
{
    private static CompositeTenantContext BuildContext(
        Mock<IHttpContextAccessor> accessorMock,
        MessageTenantContext messageTenantContext)
    {
        return new CompositeTenantContext(accessorMock.Object, messageTenantContext);
    }

    private static Mock<IHttpContextAccessor> BuildHttpAccessorWithClaim(Guid tenantId)
    {
        Claim claim = new Claim("tenantId", tenantId.ToString());
        ClaimsIdentity identity = new ClaimsIdentity(new[] { claim });
        ClaimsPrincipal principal = new ClaimsPrincipal(identity);

        DefaultHttpContext httpContext = new DefaultHttpContext();
        httpContext.User = principal;

        Mock<IHttpContextAccessor> accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        return accessorMock;
    }

    private static Mock<IHttpContextAccessor> BuildHttpAccessorWithNoContext()
    {
        Mock<IHttpContextAccessor> accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        return accessorMock;
    }

    private static Mock<IHttpContextAccessor> BuildHttpAccessorWithNoClaim()
    {
        DefaultHttpContext httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        Mock<IHttpContextAccessor> accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns(httpContext);
        return accessorMock;
    }

    [Fact]
    public void TenantId_WhenHttpContextHasClaim_ReturnsHttpTenantId()
    {
        Guid httpTenantId = Guid.NewGuid();
        Guid messageTenantId = Guid.NewGuid();
        MessageTenantContext messageTenantContext = new MessageTenantContext();
        messageTenantContext.SetTenantId(messageTenantId);

        CompositeTenantContext context = BuildContext(
            BuildHttpAccessorWithClaim(httpTenantId),
            messageTenantContext);

        context.TenantId.Should().Be(httpTenantId, "HTTP claim has priority over message context");
    }

    [Fact]
    public void TenantId_WhenHttpContextHasClaim_DoesNotFallBackToMessageContext()
    {
        Guid httpTenantId = Guid.NewGuid();
        Guid differentMessageTenantId = Guid.NewGuid();
        MessageTenantContext messageTenantContext = new MessageTenantContext();
        messageTenantContext.SetTenantId(differentMessageTenantId);

        CompositeTenantContext context = BuildContext(
            BuildHttpAccessorWithClaim(httpTenantId),
            messageTenantContext);

        context.TenantId.Should().NotBe(differentMessageTenantId);
    }

    [Fact]
    public void TenantId_WhenHttpContextIsNull_ReturnsTenantIdFromMessageContext()
    {
        Guid messageTenantId = Guid.NewGuid();
        MessageTenantContext messageTenantContext = new MessageTenantContext();
        messageTenantContext.SetTenantId(messageTenantId);

        CompositeTenantContext context = BuildContext(
            BuildHttpAccessorWithNoContext(),
            messageTenantContext);

        context.TenantId.Should().Be(messageTenantId);
    }

    [Fact]
    public void TenantId_WhenHttpContextHasNoClaim_ReturnsTenantIdFromMessageContext()
    {
        Guid messageTenantId = Guid.NewGuid();
        MessageTenantContext messageTenantContext = new MessageTenantContext();
        messageTenantContext.SetTenantId(messageTenantId);

        CompositeTenantContext context = BuildContext(
            BuildHttpAccessorWithNoClaim(),
            messageTenantContext);

        context.TenantId.Should().Be(messageTenantId);
    }

    [Fact]
    public void TenantId_WhenNeitherSourceHasTenant_ReturnsNull()
    {
        MessageTenantContext messageTenantContext = new MessageTenantContext();

        CompositeTenantContext context = BuildContext(
            BuildHttpAccessorWithNoContext(),
            messageTenantContext);

        context.TenantId.Should().BeNull();
    }

    [Fact]
    public void TenantId_WhenHttpClaimIsNotValidGuid_FallsBackToMessageContext()
    {
        Guid messageTenantId = Guid.NewGuid();
        MessageTenantContext messageTenantContext = new MessageTenantContext();
        messageTenantContext.SetTenantId(messageTenantId);

        Claim invalidClaim = new Claim("tenantId", "not-a-guid");
        ClaimsPrincipal principal = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { invalidClaim }));

        DefaultHttpContext httpContext = new DefaultHttpContext();
        httpContext.User = principal;

        Mock<IHttpContextAccessor> accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        CompositeTenantContext context = BuildContext(accessorMock, messageTenantContext);

        context.TenantId.Should().Be(messageTenantId,
            "invalid HTTP claim must not block fallback to message context");
    }

    [Fact]
    public void TenantId_HttpClaimAlwaysConsulted_MessageContextCanChangeAfterConstruction()
    {
        Guid messageTenantId = Guid.NewGuid();
        MessageTenantContext messageTenantContext = new MessageTenantContext();

        CompositeTenantContext context = BuildContext(
            BuildHttpAccessorWithNoContext(),
            messageTenantContext);

        context.TenantId.Should().BeNull();

        messageTenantContext.SetTenantId(messageTenantId);

        // No rebuild — context reads MessageTenantContext dynamically
        context.TenantId.Should().Be(messageTenantId);
    }
}
