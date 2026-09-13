using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponent;

public class GetComponentQuery : IRequest<GetComponentResponse?>
{
    public Guid Id { get; set; }
}
