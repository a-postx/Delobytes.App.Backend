using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components.UpdateComponent;

public class UpdateComponentCommandHandler : IRequestHandler<UpdateComponentCommand, UpdateComponentResponse>
{
    private readonly IComponentRepository _repository;

    public UpdateComponentCommandHandler(IComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateComponentResponse> Handle(UpdateComponentCommand request, CancellationToken cancellationToken)
    {
        Component? component = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            return new UpdateComponentResponse { Found = false };
        }

        component.Name = request.Name;
        component.Description = request.Description;
        component.Unit = request.Unit;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateComponentResponse { Found = true };
    }
}
