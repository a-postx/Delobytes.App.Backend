namespace Delobytes.App.Backend.Catalog.Application.Queries.ChannelParameterSets.GetActiveChannelParameterSet;

public class GetActiveChannelParameterSetResponse
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public decimal CommissionPercent { get; set; }
    public decimal AcquiringPercent { get; set; }
    public decimal SppPercent { get; set; }
    public bool SppEnabled { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool Found { get; set; }
}
