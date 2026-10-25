using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.BomLines;

public class GetProductBomHistoryQuery : IRequest<GetProductBomHistoryResponse>
{
    public Guid ProductId { get; set; }
}

public class GetProductBomHistoryResponse
{
    public List<BomLineDto> Items { get; set; } = new List<BomLineDto>();
}

public class GetProductBomHistoryQueryHandler : IRequestHandler<GetProductBomHistoryQuery, GetProductBomHistoryResponse>
{
    private readonly IBomLineRepository _repository;

    public GetProductBomHistoryQueryHandler(IBomLineRepository repository) {
        _repository = repository;
    }

    public async Task<GetProductBomHistoryResponse> Handle(GetProductBomHistoryQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<BomLine> lines = await _repository
            .GetHistoryByProductIdAsync(request.ProductId, cancellationToken);

        return new GetProductBomHistoryResponse { Items = lines.Select(Map).ToList() };
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
                Category = line.Component.Category
            }
        };
    }
}
