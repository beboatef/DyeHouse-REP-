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
            var after = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
            return (null, JsonSerializer.Serialize(after, options));
        }

        if (entry.State == EntityState.Deleted)
        {
            var before = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
            return (JsonSerializer.Serialize(before, options), null);
        }

        // Modified: only the properties that actually changed.
        var changed = entry.Properties.Where(p => p.IsModified).ToList();
        var beforeChanged = changed.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
        var afterChanged = changed.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
        return (JsonSerializer.Serialize(beforeChanged, options), JsonSerializer.Serialize(afterChanged, options));
    }
}
