using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CancelProductsImport;

public class CancelProductsImportCommand : IRequest
{
    public Guid JobRunId { get; set; }
}
