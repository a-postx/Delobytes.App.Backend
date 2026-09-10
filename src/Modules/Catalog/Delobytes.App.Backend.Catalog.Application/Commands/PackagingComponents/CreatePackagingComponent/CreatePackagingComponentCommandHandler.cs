using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponent;

public class CreatePackagingComponentCommandHandler : IRequestHandler<CreatePackagingComponentCommand, CreatePackagingComponentResponse>
{
    private readonly IPackagingComponentRepository _repository;

    public CreatePackagingComponentCommandHandler(IPackagingComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreatePackagingComponentResponse> Handle(CreatePackagingComponentCommand request, CancellationToken cancellationToken)
    {
        PackagingComponent component = new PackagingComponent
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Unit = request.Unit,
            PricePerUnit = request.PricePerUnit,
            SupplierId = request.SupplierId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repository.Add(component);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreatePackagingComponentResponse { Id = component.Id };
    }
}
