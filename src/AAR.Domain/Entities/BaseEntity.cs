// =============================================================================
// AAR.Domain - Entities/BaseEntity.cs
// Base class for all domain entities
// =============================================================================

namespace AAR.Domain.Entities;

/// <summary>
/// Base class for all domain entities with common properties
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Unique identifier for the entity
    /// </summary>
    public Guid Id { get; protected set; } = Guid.NewGuid();
    
    /// <summary>
    /// Timestamp when the entity was created (UTC)
    /// </summary>
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Timestamp when the entity was last modified (UTC)
    /// </summary>
    public DateTime? UpdatedAt { get; protected set; }

    /// <summary>
    /// Updates the modification timestamp
    /// </summary>
    protected void SetUpdated() => UpdatedAt = DateTime.UtcNow;
}
