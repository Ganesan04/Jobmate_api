namespace Jobmate.Domain.Enums;

/// <summary>
/// Controlled job category set. Serialised as camelCase and matches the Flutter
/// <c>JobCategory</c> enum exactly, including <c>itSupport</c>.
/// </summary>
public enum JobCategory
{
    Retail = 1,
    Events = 2,
    Delivery = 3,
    Cleaning = 4,
    Security = 5,
    Office = 6,
    Hospitality = 7,
    Construction = 8,
    Teaching = 9,
    Healthcare = 10,
    ItSupport = 11,
    Other = 12
}
