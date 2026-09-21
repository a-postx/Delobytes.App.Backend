namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.RequestProductDeletion;

public class RequestProductDeletionResponse
{
    public bool Found { get; set; }

    /// <summary>False when the product state prevents initiating deletion.</summary>
    public bool Accepted { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid ProductId { get; set; }
}
