using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductDeletionStatus;

public class GetProductDeletionStatusResponse
{
    public bool Found { get; set; }

    public Guid ProductId { get; set; }

    public ProductStatus Status { get; set; }

    public DateTimeOffset? DeletionRequestedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string StatusMessage { get; set; } = string.Empty;
}
