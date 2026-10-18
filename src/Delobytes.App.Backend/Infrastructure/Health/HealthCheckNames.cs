namespace Delobytes.App.Backend.Infrastructure.Health;

public static class HealthCheckNames
{
    public const string Memory = nameof(Memory);
    public const string Database = nameof(Database);
    public const string MessageBus = nameof(MessageBus);
    public const string DistributedCache = nameof(DistributedCache);
}
