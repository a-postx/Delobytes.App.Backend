using Microsoft.Extensions.Configuration;

namespace Delobytes.App.Backend.Contracts.Modules;

/// <summary>
/// Inputs a module needs while registering its services.
/// </summary>
/// <param name="Configuration">Application configuration.</param>
/// <param name="Secrets">Validated application secrets.</param>
public sealed record ModuleContext(IConfiguration Configuration, Secrets.AppSecrets Secrets);
