using System;

namespace Delobytes.App.Backend.Tests.TestSupport;

/// <summary>
/// Tenant context double with a fixed identifier. Tests that only need a tenant to satisfy a
/// constructor use it instead of rebuilding the same ITenantContext mock.
/// </summary>
internal sealed class StubTenantContext : Delobytes.App.Backend.Contracts.Interfaces.ITenantContext
{
    public Guid? TenantId { get; init; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
}
