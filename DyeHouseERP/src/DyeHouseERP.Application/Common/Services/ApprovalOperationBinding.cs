using System.Globalization;
using System.Text;
using System.Text.Json;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Application.Common.Services;

/// <summary>
/// One stock movement that may only take a balance negative with a stored,
/// single-use approval (spec section 24).
///
/// An approval is bound to the WHOLE operation, not just to a document: the
/// type, the subject document and the exact facts that were approved - item,
/// account, quantity in each unit, and the operation's own discriminator
/// (transfer accounts, release reason, adjustment direction). The guard compares
/// this signature with the one stored on the request, so an approval given for
/// one quantity, unit, item or account cannot authorise a different movement.
/// </summary>
public interface IApprovalBoundOperation
{
    /// <summary>Which separation-of-duties request type authorises this movement.</summary>
    ApprovalRequestType ApprovalType { get; }

    /// <summary>The subject the approval is about (Job Order or RawMessage).</summary>
    Guid ApprovalDocumentId { get; }

    /// <summary>
    /// Canonical, order-stable description of exactly what is being requested:
    /// the operation, its subject, item, account(s), quantities and unit. Two
    /// operations with the same signature are the same movement.
    /// </summary>
    string OperationSignature { get; }
}

/// <summary>
/// Storage plumbing for the operation signature.
///
/// The signature travels INSIDE the approval payload JSON as one extra property
/// (`operationSignature`) next to the module command it authorises. That keeps
/// the database schema unchanged - a new column plus a migration would be a
/// larger change than the problem needs - and it cannot drift from the command:
/// whoever reads the payload can always read the exact facts the approval was
/// granted for. The module command deserializer ignores the extra property, so
/// the payload is still the module's own command, byte for byte as before.
/// </summary>
public static class ApprovalBinding
{
    private const string SignatureProperty = "operationSignature";

    /// <summary>
    /// Canonical text for a quantity: invariant culture, no thousands
    /// separators, and a distinct marker for "not specified" so 0/null/KG and
    /// Meter can never collapse into the same signature.
    /// </summary>
    public static string Quantity(decimal? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "-";

    /// <summary>The command's own JSON with the approved-operation signature added.</summary>
    public static string EmbedSignature(string commandJson, string operationSignature)
    {
        if (string.IsNullOrWhiteSpace(operationSignature))
            throw new DomainException("An approval request must record the operation it authorises.");

        using var document = JsonDocument.Parse(commandJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new DomainException("An approval request must carry the action to approve as a JSON object.");

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(SignatureProperty, operationSignature);
            foreach (var property in document.RootElement.EnumerateObject())
                property.WriteTo(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The signature stored on a request. Missing or unreadable means the
    /// request cannot be bound to anything, so it is refused rather than treated
    /// as a blanket authorisation.
    /// </summary>
    public static string ReadSignature(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(SignatureProperty, out var signature)
                && signature.ValueKind == JsonValueKind.String)
            {
                var value = signature.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value!;
            }
        }
        catch (JsonException)
        {
            // Falls through to the refusal below - a payload we cannot read can
            // never be used to authorise a movement.
        }

        throw new DomainException(
            "This approval request does not record which operation was approved, so it cannot authorise a stock movement. " +
            "Submit a new request for the operation you want to perform.");
    }
}
