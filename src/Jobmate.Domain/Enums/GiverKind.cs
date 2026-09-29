namespace Jobmate.Domain.Enums;

/// <summary>Whether a work giver hires personally or through a registered organisation.</summary>
public enum GiverKind
{
    /// <summary>Individual hiring for themselves.</summary>
    Individual = 1,

    /// <summary>Registered business or workplace.</summary>
    Business = 2
}
