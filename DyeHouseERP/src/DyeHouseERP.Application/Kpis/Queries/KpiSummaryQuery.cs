using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Kpis.DTOs;
using FluentValidation;
using MediatR;

namespace DyeHouseERP.Application.Kpis.Queries;

/// <summary>
/// Compact dashboard rollup (KPI section of the dashboard): sends the seven
/// KPI queries for the same window and projects the headline numbers. It
/// performs no computation of its own, so the dashboard can never disagree
/// with the KPI detail screens.
/// </summary>
public record GetKpiSummaryQuery(DateTime? From = null, DateTime? To = null) : IRequest<KpiSummaryResult>, IKpiRange;

public class GetKpiSummaryQueryValidator : KpiRangeValidator<GetKpiSummaryQuery>;

public class GetKpiSummaryQueryHandler : IRequestHandler<GetKpiSummaryQuery, KpiSummaryResult>
{
    private readonly ISender _sender;
    public GetKpiSummaryQueryHandler(ISender sender) => _sender = sender;

    public async Task<KpiSummaryResult> Handle(GetKpiSummaryQuery request, CancellationToken cancellationToken)
    {
        var waste = await _sender.Send(new GetWasteRateKpiQuery(request.From, request.To), cancellationToken);
        var lead = await _sender.Send(new GetLeadTimeKpiQuery(request.From, request.To), cancellationToken);
        var utilisation = await _sender.Send(new GetMachineUtilisationKpiQuery(request.From, request.To), cancellationToken);
        var throughput = await _sender.Send(new GetThroughputKpiQuery(request.From, request.To), cancellationToken);
        var exposure = await _sender.Send(new GetCustomerExposureKpiQuery(), cancellationToken);
        var cheques = await _sender.Send(new GetChequesDueKpiQuery(), cancellationToken);
        var governance = await _sender.Send(new GetGovernanceKpiQuery(request.From, request.To), cancellationToken);

        return new KpiSummaryResult
        {
            From = request.From,
            To = request.To,
            TimeZone = KpiQueryHelpers.KpiTimeZone,
            ComputedAtUtc = DateTime.UtcNow,
            OverallWasteRatePct = waste.OverallRatePct,
            TotalWasteKg = waste.TotalWasteKg,
            AverageLeadTimeDays = lead.AverageLeadTimeDays,
            FinishedOrders = lead.FinishedOrders,
            OverallUtilisationPct = utilisation.OverallUtilisationPct,
            TotalRunningHours = utilisation.TotalRunningHours,
            TotalThroughputKg = throughput.TotalQuantityKg,
            TotalCustomerExposure = exposure.TotalOpenBalance,
            ChequesDueCount = cheques.ChequesCount,
            ChequesDueTotal = cheques.TotalAmount,
            OverridesCount = governance.OverridesCount,
            ReopeningsCount = governance.ReopeningsCount
        };
    }
}
