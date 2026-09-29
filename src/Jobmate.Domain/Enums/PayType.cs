namespace Jobmate.Domain.Enums;

/// <summary>How the advertised pay is interpreted. Matches Flutter <c>PayType</c>.</summary>
public enum PayType
{
    Fixed = 1,
    Hourly = 2,
    Daily = 3,
    PerPiece = 4
}

/// <summary>Where the work happens. Matches Flutter <c>WorkplaceType</c>.</summary>
public enum WorkplaceType
{
    OnSite = 1,
    Remote = 2,
    Hybrid = 3
}

/// <summary>One-off or repeating work. Matches Flutter <c>JobType</c>.</summary>
public enum JobType
{
    OneTime = 1,
    Recurring = 2
}

/// <summary>Payment rail. Matches Flutter <c>PaymentMethod</c>.</summary>
public enum PaymentMethod
{
    Upi = 1,
    Bank = 2,
    Cash = 3
}
