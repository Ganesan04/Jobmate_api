namespace Jobmate.Domain.Enums;

/// <summary>
/// Platform roles. A single role per user; worker and work giver are the consumer
/// roles, admin is the internal console role and is never exposed to consumers.
/// Serialised to JSON as camelCase: worker, workGiver, admin.
/// </summary>
public enum UserRole
{
    Worker = 1,
    WorkGiver = 2,
    Admin = 3
}
