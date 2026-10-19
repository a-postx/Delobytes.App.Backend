using System;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Connection resolver double for client tests whose subject is response mapping, not connection
/// lookup. Every requested id resolves to the same stub connection carrying
/// <see cref="ApiKey"/>, which is separate from the key the mocked transport actually receives.
/// </summary>
internal sealed class FixedConnectionResolver : IConnectionResolver
{
    public const string ApiKey = "test-api-key";

    public Task<Connection> ResolveAsync(string channelCode, CancellationToken ct)
    {
        throw new NotSupportedException(
            "Client tests address a connection by id; the by-channel lookup is not exercised here.");
    }

    public Task<Connection> ResolveByIdAsync(Guid connectionId, CancellationToken ct)
    {
        return Task.FromResult(new Connection
        {
            Id = connectionId,
            Name = "test",
            ApiKey = ApiKey,
            IsActive = true,
            CreatedAt = DateTimeOffset.UnixEpoch
        });
    }
}
