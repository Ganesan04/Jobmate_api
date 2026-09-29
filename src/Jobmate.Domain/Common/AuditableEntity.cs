namespace Jobmate.Domain.Common;

/// <summary>Marker for a persisted entity keyed by a generated UUID.</summary>
public interface IEntity
{
    Guid Id { get; }
}

/// <summary>
/// Base for entities carrying creation/update timestamps. All timestamps are UTC;
/// the infrastructure layer stamps them via <c>AuditableEntityInterceptor</c> so
/// handlers never have to remember to set them.
/// </summary>
public abstract class AuditableEntity : IEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
