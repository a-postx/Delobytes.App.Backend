using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Controllers;
using Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantName;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Controllers;

public class TenantControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly TenantController _controller;

    public TenantControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new TenantController(_mediatorMock.Object);
    }

    [Fact]
    public async Task UpdateTenantName_ValidRequest_ReturnsOkWithResponse()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        string newName = "New Tenant Name";

        UpdateTenantNameRequest request = new UpdateTenantNameRequest
        {
            Name = newName
        };

        UpdateTenantNameResponse expectedResponse = new UpdateTenantNameResponse
        {
            TenantId = tenantId,
            Name = newName
        };

        _mediatorMock
            .Setup(x => x.Send(It.Is<UpdateTenantNameCommand>(c => 
                c.TenantId == tenantId && c.Name == newName), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim("tenantId", tenantId.ToString())
        }));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        ActionResult<UpdateTenantNameResponse> result = await _controller.UpdateTenantName(request, CancellationToken.None);

        // Assert
        OkObjectResult? okResult = Assert.IsType<OkObjectResult>(result.Result);
        UpdateTenantNameResponse? response = Assert.IsType<UpdateTenantNameResponse>(okResult.Value);
        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal(newName, response.Name);

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateTenantNameCommand>(c => c.TenantId == tenantId && c.Name == newName),
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public async Task UpdateTenantName_NoTenantIdClaim_ReturnsUnauthorized()
    {
        // Arrange
        UpdateTenantNameRequest request = new UpdateTenantNameRequest
        {
            Name = "New Name"
        };

        ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity());

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        ActionResult<UpdateTenantNameResponse> result = await _controller.UpdateTenantName(request, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedResult>(result.Result);

        _mediatorMock.Verify(x => x.Send(
            It.IsAny<UpdateTenantNameCommand>(),
            It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task UpdateTenantName_InvalidTenantIdClaim_ReturnsUnauthorized()
    {
        // Arrange
        UpdateTenantNameRequest request = new UpdateTenantNameRequest
        {
            Name = "New Name"
        };

        ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim("tenantId", "invalid-guid")
        }));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        ActionResult<UpdateTenantNameResponse> result = await _controller.UpdateTenantName(request, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedResult>(result.Result);

        _mediatorMock.Verify(x => x.Send(
            It.IsAny<UpdateTenantNameCommand>(),
            It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Theory]
    [InlineData("Tenant Name")]
    [InlineData("Пространство 1")]
    [InlineData("テナント")]
    [InlineData("A")]
    public async Task UpdateTenantName_VariousValidNames_SendsCommandWithCorrectName(string name)
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        UpdateTenantNameRequest request = new UpdateTenantNameRequest
        {
            Name = name
        };

        UpdateTenantNameResponse expectedResponse = new UpdateTenantNameResponse
        {
            TenantId = tenantId,
            Name = name
        };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateTenantNameCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim("tenantId", tenantId.ToString())
        }));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        ActionResult<UpdateTenantNameResponse> result = await _controller.UpdateTenantName(request, CancellationToken.None);

        // Assert
        OkObjectResult? okResult = Assert.IsType<OkObjectResult>(result.Result);
        UpdateTenantNameResponse? response = Assert.IsType<UpdateTenantNameResponse>(okResult.Value);
        Assert.Equal(name, response.Name);

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateTenantNameCommand>(c => c.Name == name),
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }
}
