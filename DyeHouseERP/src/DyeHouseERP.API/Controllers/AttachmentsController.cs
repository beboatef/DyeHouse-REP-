using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Attachments.Commands;
using DyeHouseERP.Application.Attachments.DTOs;
using DyeHouseERP.Application.Attachments.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Private attachments on business documents (spec section 47). Bytes are
/// stored in the database and served only through this controller, so an
/// attachment is never reachable without a valid token and the attachment
/// permissions - there is no public static path.
/// </summary>
[ApiController]
[Route("api/attachments")]
[Authorize]
public class AttachmentsController : ControllerBase
{
    private readonly ISender _mediator;
    public AttachmentsController(ISender mediator) => _mediator = mediator;

    /// <summary>Attachments of one record, metadata only (no bytes).</summary>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AttachmentsView)]
    [ProducesResponseType(typeof(List<AttachmentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AttachmentDto>>> Get([FromQuery] string entityType, [FromQuery] Guid entityId)
        => Ok(await _mediator.Send(new GetAttachmentsQuery(entityType, entityId)));

    [HttpGet("{id:guid}/content")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AttachmentsView)]
    public async Task<IActionResult> Download(Guid id)
    {
        var file = await _mediator.Send(new GetAttachmentContentQuery(id));
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AttachmentsManage)]
    [RequestSizeLimit(UploadAttachmentCommandValidator.MaxBytes)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AttachmentDto>> Upload(
        [FromForm] string entityType, [FromForm] Guid entityId, [FromForm] string? description, IFormFile file)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);

        var command = new UploadAttachmentCommand(entityType, entityId, file.FileName, file.ContentType,
            stream.ToArray(), description);

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Download), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AttachmentsManage)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AttachmentDto>> Update(Guid id, [FromBody] UpdateAttachmentDescriptionCommand command)
        => Ok(await _mediator.Send(command with { Id = id }));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AttachmentsManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _mediator.Send(new DeleteAttachmentCommand(id));
        return NoContent();
    }
}
