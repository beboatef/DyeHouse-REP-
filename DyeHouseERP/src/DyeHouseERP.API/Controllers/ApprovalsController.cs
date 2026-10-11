using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Approvals.Commands;
using DyeHouseERP.Application.Approvals.DTOs;
using DyeHouseERP.Application.Approvals.Queries;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Centralized Approval Center (spec section 44) and the separation-of-duties
/// workflow behind it (spec section 24).
///
/// The queue rows link to the module that owns them, so every approval keeps
/// its original permission, validation and audit trail instead of being
/// re-implemented here. The three write endpoints only delegate:
///   * submitting requires the module's own action permission (enforced in the
///     handler, which is the single source of truth);
///   * approving/rejecting requires inventory.approve_negative_stock, and the
///     approver's identity always comes from their authenticated session - the
///     request body can never name an approver;
///   * approving EXECUTES the module's own command, which consumes the
///     approval in the same transaction as the stock movement.
/// </summary>
[ApiController]
[Route("api/approvals")]
[Authorize]
public class ApprovalsController : ControllerBase
{
    private readonly ISender _mediator;
    public ApprovalsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ApprovalsView)]
    [ProducesResponseType(typeof(ApprovalCenterDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApprovalCenterDto>> Get([FromQuery] int recentDays = 30)
        => Ok(await _mediator.Send(new GetApprovalCenterQuery(recentDays)));

    /// <summary>
    /// The separation-of-duties requests themselves (spec section 24): who asked
    /// for what, why, and - once decided - who decided it and why not.
    /// </summary>
    [HttpGet("requests")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ApprovalsView)]
    [ProducesResponseType(typeof(List<ApprovalRequestDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ApprovalRequestDto>>> GetRequests(
        [FromQuery] ApprovalRequestStatus? status, [FromQuery] ApprovalRequestType? type)
        => Ok(await _mediator.Send(new GetApprovalRequestsQuery(status, type)));

    /// <summary>
    /// Ask a DIFFERENT user to authorise a negative-stock override. The payload
    /// is the module's own command; it is normalised server-side (always an
    /// override, always carrying this request's reason, never an approver).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApprovalRequestDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApprovalRequestDto>> Submit([FromBody] SubmitApprovalRequestCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetRequests), new { }, result);
    }

    /// <summary>
    /// Approves a pending request and EXECUTES the approved operation through the
    /// module's own command. The approver is the authenticated caller, who must
    /// not be the requester.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryApproveNegativeStock)]
    [ProducesResponseType(typeof(ApprovalRequestDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApprovalRequestDto>> Approve(Guid id)
        => Ok(await _mediator.Send(new ApproveApprovalRequestCommand(id)));

    /// <summary>Refuses a pending request with a reason the requester can act on. Nothing is posted.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryApproveNegativeStock)]
    [ProducesResponseType(typeof(ApprovalRequestDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApprovalRequestDto>> Reject(Guid id, [FromBody] RejectApprovalRequestBody body)
        => Ok(await _mediator.Send(new RejectApprovalRequestCommand(id, body.Reason)));
}

public record RejectApprovalRequestBody(string Reason);
