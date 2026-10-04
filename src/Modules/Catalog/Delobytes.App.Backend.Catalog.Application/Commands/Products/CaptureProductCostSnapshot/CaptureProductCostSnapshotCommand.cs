using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.CaptureProductCostSnapshot;

public class CaptureProductCostSnapshotCommand : IRequest<CaptureProductCostSnapshotResponse>
{
    public Guid ProductId { get; set; }
}
