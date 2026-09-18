using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Domain.Common;

namespace WonderFleet.Infrastructure.Persistence;

/// Stamps CreatedAt/UpdatedAt and turns accidental hard deletes of history-bearing rows into soft deletes.
public sealed class AuditableEntityInterceptor(IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;
        var now = clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ISoftDeletable soft && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                soft.DeletedAt ??= now;
            }

            if (entry.Entity is not AuditableEntity auditable) continue;
            switch (entry.State)
            {
                case EntityState.Added:
                    auditable.CreatedAt = now;
                    auditable.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    auditable.UpdatedAt = now;
                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                    break;
            }
        }
    }
}
