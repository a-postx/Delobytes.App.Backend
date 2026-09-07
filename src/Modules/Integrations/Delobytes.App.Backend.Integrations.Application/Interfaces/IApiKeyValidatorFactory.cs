namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

public interface IApiKeyValidatorFactory
{
    /// <exception cref="NotSupportedException">Thrown when no validator exists for the given code.</exception>
    public IApiKeyValidator Create(string channelCode);
}
