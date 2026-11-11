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
    /// <summary>Initializes a new instance of the AttachmentsController class.</summary>
    public AttachmentsController(ISender mediator) => _mediator = mediator;

    /// <summary>Attachments of one record, metadata only (no bytes).</summary>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AttachmentsView)]
    [ProducesResponseType(typeof(List<AttachmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<AttachmentDto>>> Get([FromQuery] string entityType, [FromQuery] Guid entityId)
        => Ok(await _mediator.Send(new GetAttachmentsQuery(entityType, entityId)));

    /// <summary>Downloads document attachments.</summary>
    /// <remarks>Route: GET api/attachments/{id:guid}/content. Requires the 'attachments.view' permission.</remarks>
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AttachmentsView)]
    public async Task<IActionResult> Download(Guid id)
    {
        var file = await _mediator.Send(new GetAttachmentContentQuery(id));
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>Uploads document attachments.</summary>
    /// <remarks>Route: POST api/attachments. Requires the 'attachments.manage' permission.</remarks>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AttachmentsManage)]
    [RequestSizeLimit(UploadAttachmentCommandValidator.MaxBytes)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
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

    /// <summary>Updates document attachments.</summary>
    /// <remarks>Route: PUT api/attachments/{id:guid}. Requires the 'attachments.manage' permission.</remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AttachmentsManage)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AttachmentDto>> Update(Guid id, [FromBody] UpdateAttachmentDescriptionCommand command)
        => Ok(await _mediator.Send(command with { Id = id }));

    /// <summary>Deletes document attachments.</summary>
    /// <remarks>Route: DELETE api/attachments/{id:guid}. Requires the 'attachments.manage' permission.</remarks>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AttachmentsManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _mediator.Send(new DeleteAttachmentCommand(id));
        return NoContent();
    }
}
