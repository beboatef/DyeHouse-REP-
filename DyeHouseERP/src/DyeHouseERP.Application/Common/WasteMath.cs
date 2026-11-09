namespace DyeHouseERP.Application.Common;

/// <summary>
/// The ONE definition of الهالك (waste) in the system - deliberately a calculation, never a stored
/// balance.
///
/// Approved policy: waste is a production LOSS, not stock. It is therefore never posted: no ledger
/// row, no warehouse balance and no document are created for it, so nothing here or in its callers
/// writes anything. Every screen that shows waste derives it with this method, which is what stops
/// two reports from ever quoting different figures for the same Job Order.
///
///     waste = original raw (KG) - first-grade ready (KG) - second grade (KG) - separates (KG)
///
/// KILOGRAMS ONLY, deliberately: the waste is a weight, the metre figures on the same lines are a
/// length, and there is no conversion between the two anywhere in this system - so there is no
/// "metre waste" and nothing is inferred to produce one. Null when the KG streams are missing, or
/// when the difference would be negative: an impossible "waste" means the figures overlap or the raw
/// quantity is understated, which is a data problem and not a number to print.
/// </summary>
public static class WasteMath
{
    public static decimal? Kg(decimal? originalRawKg, decimal? firstGradeKg, decimal? separatesKg, decimal? secondGradeKg)
    {
        if (originalRawKg is null || firstGradeKg is null) return null;

        var waste = originalRawKg.Value - firstGradeKg.Value - (separatesKg ?? 0m) - (secondGradeKg ?? 0m);
        return waste < 0m ? null : waste;
    }

    /// <summary>Waste as a percentage of the original raw weight. Null wherever the waste itself is null.</summary>
    public static decimal? Percent(decimal? wasteKg, decimal? originalRawKg)
        => wasteKg is null || originalRawKg is not > 0m
            ? null
            : Math.Round(wasteKg.Value / originalRawKg.Value * 100m, 2);
}
