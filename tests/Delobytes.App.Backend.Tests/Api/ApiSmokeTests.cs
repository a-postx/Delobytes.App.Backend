using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;

namespace Delobytes.App.Backend.Tests.Api;

/// <summary>
/// Route-level smoke tests: every module is reachable through the real pipeline and closed to anonymous callers.
/// </summary>
public sealed class ApiSmokeTests : IClassFixture<ApiHost>
{
    private readonly ApiHost _host;

    public ApiSmokeTests(ApiHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task Get_Status_Returns200()
    {
        using HttpResponseMessage response = await _host.Client.GetAsync("/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_SwaggerJson_Returns200()
    {
        using HttpResponseMessage response = await _host.Client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/catalogs/products")]
    [InlineData("/api/sales/orders/chart")]
    [InlineData("/api/finance/pnl-lines")]
    [InlineData("/api/tenant/legal-entity")]
    [InlineData("/api/integrations/channels")]
    public async Task ModuleRoute_WithoutToken_Returns401(string route)
    {
        using HttpResponseMessage response = await _host.Client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("Bearer");
    }

    [Fact]
    public async Task Post_InvalidBodyWithoutToken_IsRejectedBeforeModelBinding()
    {
        using StringContent body = new("{}", System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _host.Client.PostAsync("/api/catalogs/products", body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_UnknownRoute_Returns404()
    {
        using HttpResponseMessage response = await _host.Client.GetAsync("/api/no-such-module/resource");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
