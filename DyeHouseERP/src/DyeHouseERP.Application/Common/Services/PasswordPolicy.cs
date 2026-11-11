using DyeHouseERP.Application.Common.Interfaces;
using FluentValidation.Results;

namespace DyeHouseERP.Application.Common.Services;

/// <summary>
/// Part 2 (password policy): the single enforcement point for password rules, used by the
/// domain/application layer (user creation, self change, admin reset) and mirrored in the
/// frontend change-password form with the SAME messages. The message texts below are
/// duplicated verbatim in the frontend Arabic/English dictionaries under the
/// <c>password.*</c> keys - changing a message here means changing it there too, so the
/// form and the server never disagree about why a password was refused.
/// Rules: minimum 12 characters (matches the bootstrap minimum), maximum 200 (existing
/// R5 ceiling against hashing-cost amplification), must not contain the username, must
/// not equal the current password or any of the last <see cref="MaxHistoryEntries"/>
/// passwords (compared with the untouched PBKDF2 Verify path - hashes only, never plaintext).
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 200;

    /// <summary>How many previous passwords a new one is compared against (the current one plus history).</summary>
    public const int MaxHistoryEntries = 5;

    public const string TooShortMessage = "Password must be at least 12 characters.";
    public const string TooLongMessage = "Password must be at most 200 characters.";
    public const string ContainsUsernameMessage = "Password must not contain the username.";
    public const string ReusedMessage = "Password must not match any of your last 5 passwords.";

    /// <summary>
    /// Returns every rule the candidate breaks (empty = accepted). The current live hash is
    /// always compared too: it is the most recent password by definition, so even a user with
    /// no history rows yet cannot "change" to the password they already have.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        string username,
        string newPassword,
        string? currentPasswordHash,
        IEnumerable<string> historyHashes,
        IPasswordHasher passwordHasher)
    {
        var errors = new List<string>();

        if (newPassword.Length < MinLength)
            errors.Add(TooShortMessage);
        if (newPassword.Length > MaxLength)
            errors.Add(TooLongMessage);

        if (!string.IsNullOrWhiteSpace(username) &&
            newPassword.Contains(username.Trim(), StringComparison.OrdinalIgnoreCase))
            errors.Add(ContainsUsernameMessage);

        // Only worth the PBKDF2 work when the cheap checks passed: history comparison is
        // deliberately expensive (up to 6 verifications), which is also what makes the
        // history useless to an attacker who only reads the database.
        if (errors.Count == 0)
        {
            var recent = historyHashes.Where(h => !string.IsNullOrWhiteSpace(h)).Take(MaxHistoryEntries);
            foreach (var hash in recent.Prepend(currentPasswordHash ?? string.Empty))
            {
                if (!string.IsNullOrWhiteSpace(hash) && passwordHasher.Verify(newPassword, hash))
                {
                    errors.Add(ReusedMessage);
                    break;
                }
            }
        }

        return errors;
    }

    /// <summary>Throws the pipeline's 400-mapped exception when the candidate breaks any rule.</summary>
    public static void EnsureValid(
        string propertyName,
        string username,
        string newPassword,
        string? currentPasswordHash,
        IEnumerable<string> historyHashes,
        IPasswordHasher passwordHasher)
    {
        var errors = Validate(username, newPassword, currentPasswordHash, historyHashes, passwordHasher);
        if (errors.Count > 0)
            throw new Exceptions.ValidationException(
                errors.Select(m => new ValidationFailure(propertyName, m)));
    }
}
