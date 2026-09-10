using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Suppliers.GetSupplier;

public class GetSupplierQuery : IRequest<GetSupplierResponse?>
{
    public Guid Id { get; set; }
}
