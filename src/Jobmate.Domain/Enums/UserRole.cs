namespace Jobmate.Domain.Enums;

/// <summary>
/// The two modes of an account. Every user can be both; <c>User.ActiveRole</c> holds
/// the one they are currently in. Serialised to JSON as camelCase: worker, workGiver.
/// </summary>
public enum UserRole
{
    Worker = 1,
    WorkGiver = 2
}
