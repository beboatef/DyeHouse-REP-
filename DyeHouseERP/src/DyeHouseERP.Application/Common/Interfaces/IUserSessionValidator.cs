namespace DyeHouseERP.Application.Common.Interfaces;

/// <summary>The live authorization state of the user a token was issued to (H5).</summary>
/// <param name="UserId">Identity the token was issued for.</param>
/// <param name="Username">Current username in the database.</param>
/// <param name="IsActive">False as soon as an administrator deactivates the account.</param>
/// <param name="Roles">Current permission set, already split and trimmed.</param>
public sealed record UserSessionState(Guid UserId, string Username, bool IsActive, IReadOnlyList<string> Roles);

/// <summary>
/// Reads the CURRENT state of a user while a request's JWT is being validated
/// (H5). A self-contained token stays valid until it expires (8 hours), so
/// without this check a deactivated - or permission-stripped - user keeps full
/// access for the rest of that window. Implementations must be read-only and
/// cheap: one indexed row lookup, no tracking.
/// </summary>
public interface IUserSessionValidator
{
    /// <summary>Returns the live state, or null when the user no longer exists.</summary>
    Task<UserSessionState?> GetSessionStateAsync(Guid userId, CancellationToken cancellationToken = default);
}
