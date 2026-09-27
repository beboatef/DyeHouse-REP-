using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.FormationRequests.Commands;

/// <summary>
/// Keeps an approved Formation Request in step with the Job Orders converted
/// from it (spec sections 31-32), without ever double-counting:
///
///  - the first time one of its Job Orders starts, the request moves to In Progress;
///  - when a Job Order linked to ONE GROUP completes, only the INCREMENT since the
///    group's last recorded production is added - so re-running completion, or a
///    second Job Order for the same group, can never inflate the produced quantity;
///  - the request's own status (In Progress / Partially Completed / Completed) is
///    then recalculated from the groups, which is the only place it is decided.
/// </summary>
internal static class FormationProductionSync
{
    public static async Task OnProductionStartedAsync(
        IApplicationDbContext db, ProductionOrder order, string user, CancellationToken cancellationToken)
    {
        if (!order.FormationRequestId.HasValue) return;

        var request = await db.FormationRequests.Include(r => r.Groups)
            .FirstOrDefaultAsync(r => r.Id == order.FormationRequestId.Value, cancellationToken);

        if (request is null) return;

        if (request.Status == FormationRequestStatus.Approved)
            request.StartProduction(user);
    }

    public static async Task OnProductionCompletedAsync(
        IApplicationDbContext db, ProductionOrder order, string user, CancellationToken cancellationToken)
    {
        if (!order.FormationRequestId.HasValue || !order.FormationGroupId.HasValue) return;

        var request = await db.FormationRequests.Include(r => r.Groups)
            .FirstOrDefaultAsync(r => r.Id == order.FormationRequestId.Value, cancellationToken);
        if (request is null) return;

        var group = request.Groups.FirstOrDefault(g => g.Id == order.FormationGroupId.Value);
        if (group is null) return;

        var produced = await ResolveProducedQuantityAsync(db, order, cancellationToken);
        if (produced <= 0) return;

        // Only the increment: the same physical quantity is never counted twice (spec section 53).
        var increment = produced - group.ProducedQuantity;
        if (increment <= 0) return;

        // Never claim more than the group planned - the surplus is waste/over-run, not more planned output.
        var capped = Math.Min(increment, group.RemainingQuantity);
        if (capped <= 0) return;

        request.RecordGroupProduction(group.Id, capped, user);
    }

    /// <summary>
    /// The real produced quantity of a completed order: the last completed stage's output,
    /// falling back to the requested quantity when no stage recorded output. Never a derived
    /// KG&lt;-&gt;Meter conversion - it uses whichever of the two the order was planned in.
    /// </summary>
    private static async Task<decimal> ResolveProducedQuantityAsync(
        IApplicationDbContext db, ProductionOrder order, CancellationToken cancellationToken)
    {
        var stages = await db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => s.ProductionOrderId == order.Id)
            .OrderBy(s => s.Sequence)
            .ToListAsync(cancellationToken);

        var withOutput = stages
            .Where(s => s.Status == StageExecutionStatus.Completed && (s.OutputKg.HasValue || s.OutputMeter.HasValue))
            .ToList();

        if (withOutput.Count > 0)
        {
            var last = withOutput[^1];
            var byKg = order.RequestedQuantityMeter is null && last.OutputKg.HasValue;
            return byKg ? last.OutputKg!.Value : last.OutputMeter ?? last.OutputKg ?? 0m;
        }

        return order.RequestedQuantityKg ?? order.RequestedQuantityMeter ?? 0m;
    }
}
