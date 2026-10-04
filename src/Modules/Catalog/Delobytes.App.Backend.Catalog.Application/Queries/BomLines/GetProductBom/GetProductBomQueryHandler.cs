using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.BomLines;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.BomLines.GetProductBom;

public class GetProductBomQueryHandler : IRequestHandler<GetProductBomQuery, GetProductBomResponse>
{
    private readonly IBomLineRepository _repository;
    public GetProductBomQueryHandler(IBomLineRepository repository) {
        _repository = repository;
    }

    public async Task<GetProductBomResponse> Handle(GetProductBomQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<BomLine> lines = await _repository
            .GetActiveByProductIdAsync(request.ProductId, cancellationToken);

        return new GetProductBomResponse { Items = lines.Select(Map).ToList() };
    }

    private static BomLineDto Map(BomLine line)
    {
        return new BomLineDto {
            Id = line.Id,
            ProductId = line.ProductId,
            ComponentId = line.ComponentId,
            Quantity = line.Quantity,
            ValidFrom = line.ValidFrom,
            IsActive = line.IsActive,
            CreatedAt = line.CreatedAt,
            UpdatedAt = line.UpdatedAt,
            Component = line.Component == null ? null : new BomComponentDto {
                Id = line.Component.Id,
                Name = line.Component.Name,
                Unit = line.Component.Unit,
                Category = line.Component.Category,
                ActivePricePerUnit = line.Component.Prices.FirstOrDefault()?.PricePerUnit
            }
        };
    }
}
