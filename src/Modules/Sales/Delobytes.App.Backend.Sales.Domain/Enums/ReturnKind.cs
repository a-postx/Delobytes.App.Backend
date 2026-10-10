namespace Delobytes.App.Backend.Sales.Domain.Enums;

/// <summary>
/// Canonical kind of a return, projected from the verbatim channel reason.
/// </summary>
public enum ReturnKind
{
    /// <summary>
    /// The buyer returned the goods.
    /// </summary>
    Buyer = 0,

    /// <summary>
    /// The goods were returned as defective.
    /// </summary>
    Defective = 1,

    /// <summary>
    /// Only part of the goods was returned.
    /// </summary>
    Partial = 2,

    /// <summary>
    /// A return that does not fit the other kinds; the verbatim reason is kept on the entity.
    /// </summary>
    Other = 3,
}
