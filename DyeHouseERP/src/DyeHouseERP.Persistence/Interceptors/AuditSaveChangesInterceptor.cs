using System.Text.Json;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DyeHouseERP.Persistence.Interceptors;

/// <summary>
/// Automatically writes an AuditLogEntry for every Added/Modified/Deleted
/// entity in each SaveChanges call (spec section 42) - so audit coverage
/// is a property of the persistence layer, not something every command
/// handler has to remember to do by hand. Skips AuditLogEntry itself (no
/// recursion) and skips pure read-only entities that were never tracked
/// as changed.
///
/// Redaction rules (B2): no binary content and no secret material ever
/// reaches the audit payload. byte[] properties (e.g. Attachment.Content)
/// are replaced with a "[REDACTED size]" marker so an upload is still
/// visible as an event without bloating the row; properties whose names are
/// known to carry secrets (password hashes, tokens, keys) are replaced with
/// a plain "[REDACTED]" marker. The original values are never stored.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUser;

    public AuditSaveChangesInterceptor(ICurrentUserService currentUser) => _currentUser = currentUser;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            AppendAuditEntries(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AppendAuditEntries(DbContext context)
    {
        var now = DateTime.UtcNow;
        var userName = SafeUserName();

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLogEntry
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            var action = entry.State switch
            {
                EntityState.Added => "Create",
                EntityState.Modified => "Update",
                EntityState.Deleted => "Delete",
                _ => "Unknown"
            };

            var entityId = TryGetId(entry);
            var (before, after) = BuildBeforeAfter(entry);

            context.Set<AuditLogEntry>().Add(new AuditLogEntry(
                now, userName, action, entry.Entity.GetType().Name, entityId, before, after, ipAddress: SafeIpAddress(), reason: null));
        }
    }

    private string SafeUserName()
    {
        // ICurrentUserService reads from HttpContext, which isn't available for
        // background/seed operations - fall back to "system" rather than throw.
        try { return _currentUser.UserName; } catch { return "system"; }
    }

    private string? SafeIpAddress()
    {
        try { return _currentUser.IpAddress; } catch { return null; }
    }

    private static string? TryGetId(EntityEntry entry)
    {
        var idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
        return idProperty?.CurrentValue?.ToString();
    }

    private static (string? Before, string? After) BuildBeforeAfter(EntityEntry entry)
    {
        // Only scalar properties are captured (not navigations) - keeps the
        // payload small and avoids serializing unrelated related aggregates.
        var options = new JsonSerializerOptions { WriteIndented = false };

        if (entry.State == EntityState.Added)
        {
            var after = entry.Properties.ToDictionary(p => p.Metadata.Name, p => RedactIfSensitive(p));
            return (null, JsonSerializer.Serialize(after, options));
        }

        if (entry.State == EntityState.Deleted)
        {
            var before = entry.Properties.ToDictionary(p => p.Metadata.Name, p => RedactIfSensitive(p, useOriginal: true));
            return (JsonSerializer.Serialize(before, options), null);
        }

        // Modified: only the properties that actually changed.
        var changed = entry.Properties.Where(p => p.IsModified).ToList();
        var beforeChanged = changed.ToDictionary(p => p.Metadata.Name, p => RedactIfSensitive(p, useOriginal: true));
        var afterChanged = changed.ToDictionary(p => p.Metadata.Name, p => RedactIfSensitive(p));
        return (JsonSerializer.Serialize(beforeChanged, options), JsonSerializer.Serialize(afterChanged, options));
    }

    /// <summary>
    /// Returns the audit-safe value for one property: secrets become the
    /// "[REDACTED]" marker, binary content becomes "[REDACTED size N]" (the
    /// byte length is useful for spotting oversized uploads without storing
    /// the bytes), and everything else passes through untouched.
    /// </summary>
    private static object? RedactIfSensitive(PropertyEntry entry, bool useOriginal = false)
        => AuditValueRedactor.RedactIfSensitive(entry.Metadata.Name, useOriginal ? entry.OriginalValue : entry.CurrentValue);
}

/// <summary>
/// Pure redaction rules for audit payloads, split out of the interceptor so
/// they can be unit-tested without an EF model (B2). Secrets are masked with
/// a fixed marker; binary content is replaced by a size marker - the actual
/// bytes are never returned, no matter the entity.
/// </summary>
public static class AuditValueRedactor
{
    internal static readonly string[] SensitivePropertyNames =
    {
        "PasswordHash",
        "Password",
        "Secret",
        "Token",
        "ApiKey"
    };

    public const string RedactedMarker = "[REDACTED]";

    public static bool IsSensitiveName(string propertyName)
    {
        foreach (var sensitive in SensitivePropertyNames)
        {
            if (propertyName.EndsWith(sensitive, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static object? RedactIfSensitive(string propertyName, object? value)
    {
        if (IsSensitiveName(propertyName))
            return RedactedMarker;

        if (value is byte[] bytes)
            return $"[REDACTED size {bytes.Length}]";

        return value;
    }
}
