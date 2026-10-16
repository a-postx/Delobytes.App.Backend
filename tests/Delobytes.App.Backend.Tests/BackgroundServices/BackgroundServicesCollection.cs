using Xunit;

namespace Delobytes.App.Backend.Tests.BackgroundServices;

/// <summary>
/// Serialises the background job tests. MessageTenantContext keeps its value in a static
/// AsyncLocal field, so two test classes running in parallel would interleave their
/// SetTenantId/Clear calls and stamp rows with the wrong tenant.
/// </summary>
[CollectionDefinition("BackgroundServices", DisableParallelization = true)]
public class BackgroundServicesCollection
{
}
