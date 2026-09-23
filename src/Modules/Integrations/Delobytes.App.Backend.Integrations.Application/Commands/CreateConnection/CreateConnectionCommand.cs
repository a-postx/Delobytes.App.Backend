using Delobytes.App.Backend.Integrations.Application.DTOs.Connections;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CreateConnection;

public class CreateConnectionCommand : IRequest<CreateConnectionResponse>
{
    public Guid ChannelId { get; set; }
    public string SystemChannelTemplateCode { get; set; } = default!;
    public string ApiKey { get; set; } = default!;
    public string? ApiSecret { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
}
