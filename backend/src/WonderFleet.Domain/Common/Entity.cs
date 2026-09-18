namespace WonderFleet.Domain.Common;

/// Base type for every entity. Ids are time-ordered (UUIDv7) so B-tree indexes stay compact.
public abstract class Entity
{
    public Guid Id { get; set; } = SequentialId.New();
}

public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// Entities that are never hard-deleted (business history must be preserved).
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}

public static class SequentialId
{
    public static Guid New() =>
#if NET9_0_OR_GREATER
        Guid.CreateVersion7();
#else
        Guid.NewGuid();
#endif
}
