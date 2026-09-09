using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.PackagingComponents.GetPackagingComponent;

public class GetPackagingComponentQuery : IRequest<GetPackagingComponentResponse?>
{
    public Guid Id { get; set; }
}
