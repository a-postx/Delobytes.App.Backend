using System.Globalization;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Maps the Ozon daily accruals to finance entries. Amounts are taken exactly as reported and never
/// multiplied by a quantity: the documented schema exposes no quantity that would justify it, and
/// <c>total_amount</c> is already the total of the accrual.
/// </summary>
internal static class OzonFinanceMapper
{
    private const string ItemCategory = "ITEM";
    private const string PostingCategory = "POSTING";
    private const int AcquiringFeeTypeId = 1;

    /// <summary>
    /// Maps one page of accruals.
    /// </summary>
    /// <param name="accruals">Accruals of the page.</param>
    /// <returns>The entries and the mapping counters.</returns>
    public static OzonFinanceMappingResult Map(IReadOnlyList<OzonAccrual> accruals)
    {
        ArgumentNullException.ThrowIfNull(accruals);

        List<ChannelFinanceEntrySnapshot> entries = new();
        FinanceMappingDiagnostics diagnostics = new();

        foreach (OzonAccrual accrual in accruals)
        {
            if (string.Equals(accrual.AccruedCategory, ItemCategory, StringComparison.Ordinal))
            {
                MapItem(accrual, entries, diagnostics);
                continue;
            }

            if (string.Equals(accrual.AccruedCategory, PostingCategory, StringComparison.Ordinal))
            {
                MapPosting(accrual, entries, diagnostics);
                continue;
            }

            diagnostics.SkippedOperations++;
        }

        return new OzonFinanceMappingResult(entries, diagnostics);
    }

    private static void MapItem(
        OzonAccrual accrual,
        List<ChannelFinanceEntrySnapshot> entries,
        FinanceMappingDiagnostics diagnostics)
    {
        if (accrual.ItemFees?.Fees == null)
        {
            return;
        }

        Dictionary<long, decimal> acquiringBySku = new();

        foreach (OzonAccrualSkuFees skuFees in accrual.ItemFees.Fees)
        {
            if (skuFees.Fees == null)
            {
                continue;
            }

            foreach (OzonAccrualFee fee in skuFees.Fees)
            {
                if (fee.TypeId != AcquiringFeeTypeId)
                {
                    diagnostics.SkippedOperations++;
                    continue;
                }

                acquiringBySku[skuFees.Sku] = acquiringBySku.GetValueOrDefault(skuFees.Sku) + (fee.Accrued?.Amount ?? 0m);
            }
        }

        Context context = BuildContext(accrual);

        foreach ((long sku, decimal amount) in acquiringBySku)
        {
            if (amount == 0m)
            {
                continue;
            }

            entries.Add(CreateEntry(
                context,
                externalLineId: null,
                externalOrderId: accrual.UnitNumber,
                externalProductId: sku.ToString(CultureInfo.InvariantCulture),
                FinanceEntryKind.Component,
                FinanceEntryType.Acquiring,
                amount,
                amount >= 0m ? FinanceEntryDirection.Income : FinanceEntryDirection.Expense));
        }

        if (acquiringBySku.Count == 1 && !string.IsNullOrEmpty(accrual.UnitNumber))
        {
            EmitControlTotal(accrual, entries, diagnostics);
        }
        else if (acquiringBySku.Count > 1)
        {
            diagnostics.AccrualsWithoutControlTotal++;
        }
    }

    private static void MapPosting(
        OzonAccrual accrual,
        List<ChannelFinanceEntrySnapshot> entries,
        FinanceMappingDiagnostics diagnostics)
    {
        List<OzonAccrualProduct> products = accrual.Posting?.Products ?? new List<OzonAccrualProduct>();

        if (products.Count == 0)
        {
            return;
        }

        Context context = BuildContext(accrual);

        Dictionary<(long Sku, FinanceEntryType Type), decimal> sums = new();

        bool reversal = false;

        foreach (OzonAccrualProduct product in products)
        {
            OzonAccrualCommission? commission = product.Commission;

            decimal logistics = product.Delivery?.TotalAccrued?.Amount ?? 0m;

            // Logistics is reported even when the product carries no commission block, which is
            // how a non-buyout or a delivery-only accrual arrives.
            Add(sums, product.Sku, FinanceEntryType.Logistics, logistics);

            if (commission == null)
            {
                continue;
            }

            decimal salePrice = commission.SalePrice?.Amount ?? 0m;

            if (salePrice < 0m)
            {
                reversal = true;
            }

            decimal bonus = commission.Bonus?.Amount ?? 0m;
            decimal coinvestment = commission.Coinvestment?.Amount ?? 0m;
            decimal compensation = bonus + coinvestment;

            decimal commissionAmount = commission.Commission?.Amount ?? 0m;
            decimal saleCommission = commission.SaleCommission?.Amount ?? 0m;

            if (commissionAmount == 0m && saleCommission != 0m)
            {
                commissionAmount = saleCommission;
                diagnostics.CommissionFallbacks++;
            }

            decimal? saleAmount = commission.SaleAmount?.Amount;

            if (saleAmount.HasValue)
            {
                decimal expected = salePrice + bonus + coinvestment;

                if (saleAmount.Value != expected)
                {
                    diagnostics.SaleAmountMismatches++;
                }
            }

            Add(sums, product.Sku, FinanceEntryType.Sale, salePrice);
            Add(sums, product.Sku, FinanceEntryType.Compensation, compensation);
            Add(sums, product.Sku, FinanceEntryType.Commission, commissionAmount);
        }

        if (reversal)
        {
            diagnostics.Reversals++;
        }

        foreach (((long sku, FinanceEntryType type), decimal amount) in sums)
        {
            if (amount == 0m)
            {
                continue;
            }

            FinanceEntryDirection direction = ResolveDirection(type, amount, reversal);

            entries.Add(CreateEntry(
                context,
                externalLineId: accrual.UnitNumber,
                externalOrderId: null,
                externalProductId: sku.ToString(CultureInfo.InvariantCulture),
                FinanceEntryKind.Component,
                type,
                amount,
                direction));
        }

        int distinctSkus = products
            .Where(p => p.Commission != null || p.Delivery?.TotalAccrued != null)
            .Select(p => p.Sku)
            .Distinct()
            .Count();

        if (distinctSkus == 1 && products.Count > 0 && products[0].Sku != 0 && !string.IsNullOrEmpty(accrual.UnitNumber))
        {
            EmitControlTotal(accrual, entries, diagnostics);
        }
        else if (distinctSkus > 1)
        {
            // A posting-level total cannot be attributed to one SKU without inventing an allocation,
            // so it is not written at all.
            diagnostics.AccrualsWithoutControlTotal++;
        }
    }

    private static FinanceEntryDirection ResolveDirection(
        FinanceEntryType type,
        decimal amount,
        bool reversal)
    {
        if (type == FinanceEntryType.Sale)
        {
            return amount >= 0m ? FinanceEntryDirection.Income : FinanceEntryDirection.Expense;
        }

        if (reversal)
        {
            return amount >= 0m ? FinanceEntryDirection.Expense : FinanceEntryDirection.Income;
        }

        return amount >= 0m ? FinanceEntryDirection.Income : FinanceEntryDirection.Expense;
    }

    private static void EmitControlTotal(
        OzonAccrual accrual,
        List<ChannelFinanceEntrySnapshot> entries,
        FinanceMappingDiagnostics diagnostics)
    {
        decimal total = accrual.TotalAmount?.Amount ?? 0m;

        if (total == 0m)
        {
            return;
        }

        Context context = BuildContext(accrual);
        string productId = string.Equals(accrual.AccruedCategory, ItemCategory, StringComparison.Ordinal)
            ? (accrual.ItemFees?.Fees?.FirstOrDefault()?.Sku ?? 0).ToString(CultureInfo.InvariantCulture)
            : (accrual.Posting?.Products?.FirstOrDefault()?.Sku ?? 0).ToString(CultureInfo.InvariantCulture);

        if (productId == "0")
        {
            diagnostics.AccrualsWithoutControlTotal++;
            return;
        }

        entries.Add(CreateEntry(
            context,
            externalLineId: string.Equals(accrual.AccruedCategory, PostingCategory, StringComparison.Ordinal)
                ? accrual.UnitNumber
                : null,
            externalOrderId: string.Equals(accrual.AccruedCategory, ItemCategory, StringComparison.Ordinal)
                ? accrual.UnitNumber
                : null,
            externalProductId: productId,
            FinanceEntryKind.ControlTotal,
            FinanceEntryType.Payout,
            total,
            total >= 0m ? FinanceEntryDirection.Income : FinanceEntryDirection.Expense));
    }

    private static void Add(
        Dictionary<(long Sku, FinanceEntryType Type), decimal> sums,
        long sku,
        FinanceEntryType type,
        decimal amount)
    {
        sums[(sku, type)] = sums.GetValueOrDefault((sku, type)) + amount;
    }

    private static Context BuildContext(OzonAccrual accrual)
    {
        DateOnly documentDate = DateOnly.TryParseExact(
            accrual.Date,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly parsed)
            ? parsed
            : default;

        string currency = string.IsNullOrWhiteSpace(accrual.TotalAmount?.Currency)
            ? "RUB"
            : accrual.TotalAmount!.Currency!;

        return new Context(
            accrual.AccrualId.ToString(CultureInfo.InvariantCulture),
            currency,
            documentDate);
    }

    private static ChannelFinanceEntrySnapshot CreateEntry(
        Context context,
        string? externalLineId,
        string? externalOrderId,
        string externalProductId,
        FinanceEntryKind kind,
        FinanceEntryType type,
        decimal amount,
        FinanceEntryDirection direction)
    {
        return new ChannelFinanceEntrySnapshot
        {
            Source = FinanceEntrySource.OzonAccrual,
            ReportInstanceKey = string.Empty,
            ExternalEntryId = context.ExternalEntryId,
            ExternalOrderId = externalOrderId,
            ExternalLineId = externalLineId,
            ExternalProductId = externalProductId,
            Kind = kind,
            Type = type,
            Amount = Math.Abs(amount),
            Direction = direction,
            Currency = context.Currency,
            DocumentDate = context.DocumentDate,
            PeriodFrom = context.DocumentDate,
            PeriodTo = context.DocumentDate,
        };
    }

    private readonly record struct Context(
        string ExternalEntryId,
        string Currency,
        DateOnly DocumentDate);
}

/// <summary>
/// The result of mapping one page of Ozon accruals.
/// </summary>
/// <param name="Entries">The finance entries of the page.</param>
/// <param name="Diagnostics">What the mapper skipped or had to fall back on.</param>
internal sealed record OzonFinanceMappingResult(
    IReadOnlyList<ChannelFinanceEntrySnapshot> Entries,
    FinanceMappingDiagnostics Diagnostics);
