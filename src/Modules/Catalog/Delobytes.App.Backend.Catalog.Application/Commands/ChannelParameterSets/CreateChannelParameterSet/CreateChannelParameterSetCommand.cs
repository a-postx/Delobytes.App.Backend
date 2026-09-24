using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ChannelParameterSets.CreateChannelParameterSet;

public class CreateChannelParameterSetCommand : IRequest<CreateChannelParameterSetResponse>
{
    public Guid ChannelId { get; set; }
    public decimal CommissionPercent { get; set; }
    public decimal AcquiringPercent { get; set; }
    public decimal SppPercent { get; set; }
    public bool SppEnabled { get; set; }
    public DateOnly ValidFrom { get; set; }
}
