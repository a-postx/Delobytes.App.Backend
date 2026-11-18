using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Domain.Entities;

/// <summary>
/// One fact of money reported by a channel, appended to the journal and never edited afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Money for a single sale arrives in several documents and on different days: Wildberries reports
/// the sale and the delivery as two rows of the weekly report and may add a refund later; Ozon
/// reports the sale, the commission and the acquiring fee as separate accruals. A journal is the
/// only shape that survives this: a row per fact, recomputable into a result, correctable by
/// appending rather than by overwriting.
/// </para>
/// <para>
/// Identity is <see cref="Source"/>, <see cref="ReportInstanceKey"/>, <see cref="ExternalEntryId"/>,
/// <see cref="ExternalProductId"/> and <see cref="Type"/> together: one report row can carry several
/// typed amounts (a Wildberries sale row reports the revenue, the commission and the acquiring fee
/// at once), and one report file can be issued more than once.
/// </para>
/// <para>
/// <see cref="Amount"/> is always the absolute value; the direction is carried separately by
/// <see cref="Direction"/> because the sign inside a channel payload does not reliably describe who
/// pays whom.
/// </para>
/// <para>
/// A row is never deleted or recalculated. When a newer report instance repeats a fact, the older
/// row is marked by <see cref="SupersededByEntryId"/> and excluded from the aggregate; the amounts
/// themselves stay untouched.
/// </para>
/// </remarks>
public class PnlEntry : ITenantScoped, IRowVersionedEntity
{
    /// <summary>
    /// Gets or sets the journal row unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the external document the fact was read from.
    /// </summary>
    public PnlSource Source { get; set; }

    /// <summary>
    /// Gets or sets the identity of the report instance the fact belongs to. Wildberries:
    /// <c>reportId:createDate</c>, because a weekly report is re-issued and the same row may come
    /// again with corrected amounts. Ozon: an empty string, because an accrual is never re-issued
    /// and <see cref="ExternalEntryId"/> alone identifies it.
    /// </summary>
    public string ReportInstanceKey { get; set; } = default!;

    /// <summary>
    /// Gets or sets the channel-side identity of the row: the Wildberries <c>rrdId</c> or the Ozon
    /// <c>accrual_id</c>. Kept as a string because the two channels number their rows differently.
    /// </summary>
    public string ExternalEntryId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the sales channel (Catalog.Channel.Id — no FK across modules).
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the channel-side order identity. Null where the document does not carry one:
    /// a Wildberries report row identifies the unit by <c>srid</c> only. Required for an Ozon
    /// <c>ITEM</c> accrual, which carries the order number rather than the posting number.
    /// </summary>
    public string? ExternalOrderId { get; set; }

    /// <summary>
    /// Gets or sets the channel-side unit identity: the Wildberries <c>srid</c> or the Ozon posting
    /// number. Null for an Ozon <c>ITEM</c> accrual, which is attributed to the order and the
    /// product instead.
    /// </summary>
    public string? ExternalLineId { get; set; }

    /// <summary>
    /// Gets or sets the channel-side product identity: the Wildberries <c>nmId</c> or the Ozon
    /// <c>sku</c>. This is what an entry without a line identity is attributed by.
    /// </summary>
    public string ExternalProductId { get; set; } = default!;

    /// <summary>
    /// Gets or sets whether the entry is a component of the result or the channel's own total for
    /// the document.
    /// </summary>
    public PnlEntryKind Kind { get; set; } = PnlEntryKind.Component;

    /// <summary>
    /// Gets or sets the business meaning of the amount.
    /// </summary>
    public PnlEntryType Type { get; set; }

    /// <summary>
    /// Gets or sets the absolute amount. The sign is expressed by <see cref="Direction"/>, never by
    /// this value.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Gets or sets which way <see cref="Amount"/> moves the result.
    /// </summary>
    public PnlEntryDirection Direction { get; set; }

    /// <summary>
    /// Gets or sets the entry currency.
    /// </summary>
    public string Currency { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date the operation happened per the channel: the Wildberries <c>saleDt</c>
    /// or the Ozon accrual date. Not the date the entry was imported.
    /// </summary>
    public DateOnly DocumentDate { get; set; }

    /// <summary>
    /// Gets or sets the first day of the reporting period the document covers.
    /// </summary>
    public DateOnly PeriodFrom { get; set; }

    /// <summary>
    /// Gets or sets the last day of the reporting period the document covers.
    /// </summary>
    public DateOnly PeriodTo { get; set; }

    /// <summary>
    /// Gets or sets the time the entry was appended to the journal. Distinct from
    /// <see cref="DocumentDate"/>: a weekly report describes days that are already over.
    /// </summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the entry that replaced this one, when a newer report
    /// instance repeated the same fact. A non-null value excludes the row from the aggregate.
    /// </summary>
    public Guid? SupersededByEntryId { get; set; }

    /// <summary>
    /// Gets or sets the time the row was superseded.
    /// </summary>
    public DateTimeOffset? SupersededAt { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the row version for optimistic concurrency (PostgreSQL xmin).
    /// </summary>
    public uint RowVersion { get; set; }
}
