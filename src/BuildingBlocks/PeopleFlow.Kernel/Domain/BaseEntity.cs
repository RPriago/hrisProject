namespace PeopleFlow.Kernel.Domain;

public abstract class BaseEntity<TId> : IEquatable<BaseEntity<TId>>
    where TId : notnull, IEquatable<TId>
{
    protected BaseEntity(TId id)
    {
        Id = id;
    }

    protected BaseEntity()
    {
        if (typeof(TId) == typeof(Guid))
        {
            Id = (TId)(object)Guid.CreateVersion7();
        }
        else
        {
            Id = default!;
        }
    }

    public TId Id { get; init; }

    public DateTimeOffset CreatedAtUtc { get; protected set; } = DateTimeOffset.UtcNow;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool HasExplicitCreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; protected set; }

    public DateTimeOffset? UpdatedAtUtc { get; protected set; }

    public Guid? UpdatedBy { get; protected set; }

    public void SetCreatedAudit(DateTimeOffset createdAtUtc, Guid? createdBy = null)
    {
        CreatedAtUtc = createdAtUtc;
        HasExplicitCreatedAtUtc = true;
        if (createdBy.HasValue && createdBy.Value != Guid.Empty)
        {
            CreatedBy = createdBy;
        }
    }

    public void MarkUpdated(DateTimeOffset updatedAtUtc, Guid? updatedBy = null)
    {
        UpdatedAtUtc = updatedAtUtc;
        if (updatedBy.HasValue && updatedBy.Value != Guid.Empty)
        {
            UpdatedBy = updatedBy;
        }
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return obj is BaseEntity<TId> other && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public bool Equals(BaseEntity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.GetType() != GetType()) return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);

    public static bool operator ==(BaseEntity<TId>? left, BaseEntity<TId>? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(BaseEntity<TId>? left, BaseEntity<TId>? right) => !(left == right);
}

public abstract class BaseEntity : BaseEntity<Guid>
{
    protected BaseEntity(Guid id) : base(id)
    {
    }

    protected BaseEntity() : base(Guid.CreateVersion7())
    {
    }
}
