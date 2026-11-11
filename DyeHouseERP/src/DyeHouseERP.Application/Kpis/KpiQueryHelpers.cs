using FluentValidation;

namespace DyeHouseERP.Application.Kpis;

/// <summary>
/// Shared KPI plumbing: the timezone every result carries, range validation
/// and the divide-by-zero rule (null, never 0 or Infinity).
/// </summary>
public static class KpiQueryHelpers
{
    /// <summary>
    /// The timezone every KPI result carries. All ERP timestamps are stored in
    /// UTC and every KPI filter compares UTC values, so the honest label for
    /// the range is UTC - there is no company timezone setting to consult.
    /// </summary>
    public const string KpiTimeZone = "UTC";

    /// <summary>Division for KPI ratios: null when the denominator is zero.</summary>
    public static decimal? DivideOrNull(decimal numerator, decimal denominator)
        => denominator == 0m ? null : Math.Round(numerator / denominator * 100m, 2);

    /// <summary>Division for hour ratios: null when the denominator is zero.</summary>
    public static decimal? DivideOrNull(double numerator, double denominator)
        => denominator == 0 ? null : Math.Round((decimal)(numerator / denominator * 100.0), 2);

    /// <summary>Average of a non-empty set, else null.</summary>
    public static double? AverageOrNull(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : list.Average();
    }
}

/// <summary>Validates the optional From/To window shared by the period KPIs.</summary>
public abstract class KpiRangeValidator<T> : AbstractValidator<T> where T : IKpiRange
{
    protected KpiRangeValidator()
    {
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("To must be on or after From.");
    }
}

/// <summary>Range window accepted by the period KPI queries.</summary>
public interface IKpiRange
{
    DateTime? From { get; }
    DateTime? To { get; }
}
