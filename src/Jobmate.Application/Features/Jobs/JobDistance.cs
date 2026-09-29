using System.Linq.Expressions;
using System.Reflection;
using Jobmate.Domain.Entities;
using Jobmate.Domain.ValueObjects;

namespace Jobmate.Application.Features.Jobs;

/// <summary>
/// Builds the great-circle distance between a caller's coordinates and a job's as a
/// LINQ expression EF Core translates to SQL.
///
/// The maths runs in the database rather than in memory so a radius filter and a
/// "nearest first" sort can use an index and still return correct pages, instead of
/// loading every active job and discarding most of them. The expression yields
/// <see cref="double"/>? — null for a job with no coordinates, which the projection
/// turns into the 0 km the Flutter job card shows.
/// </summary>
public static class JobDistance
{
    private const double DegreesToRadians = Math.PI / 180.0;

    private static readonly MethodInfo Sin = typeof(Math).GetMethod(nameof(Math.Sin), [typeof(double)])!;

    private static readonly MethodInfo Cos = typeof(Math).GetMethod(nameof(Math.Cos), [typeof(double)])!;

    private static readonly MethodInfo Sqrt = typeof(Math).GetMethod(nameof(Math.Sqrt), [typeof(double)])!;

    private static readonly MethodInfo Atan2 = typeof(Math).GetMethod(nameof(Math.Atan2), [typeof(double), typeof(double)])!;

    /// <summary>
    /// Distance in kilometres from (<paramref name="latitude"/>, <paramref name="longitude"/>),
    /// or null for a job with no coordinates.
    /// </summary>
    public static Expression<Func<Job, double?>> ExpressionFrom(double latitude, double longitude)
    {
        var job = Expression.Parameter(typeof(Job), "j");

        // j.Latitude and j.Longitude are nullable; coalescing them keeps the whole
        // expression non-nullable so Math.* can be used. The guard below decides
        // whether the computed value is kept at all.
        var jobLatitude = CoalesceZero(Expression.Property(job, nameof(Job.Latitude)));
        var jobLongitude = CoalesceZero(Expression.Property(job, nameof(Job.Longitude)));

        var viewerLatitude = Expression.Constant(latitude);
        var viewerLongitude = Expression.Constant(longitude);

        var toRadians = Expression.Constant(DegreesToRadians);
        var half = Expression.Constant(0.5);
        var one = Expression.Constant(1.0);
        var diameter = Expression.Constant(2.0 * GeoDistance.EarthRadiusKm);

        Expression DeltaRadians(Expression coordinate, Expression viewer) =>
            Expression.Multiply(Expression.Subtract(coordinate, viewer), toRadians);

        // sin²(d / 2) written as a multiply, because PostgreSQL's power() is numeric-only.
        Expression HalfSineSquared(Expression deltaRadians)
        {
            var sine = Expression.Call(Sin, Expression.Multiply(deltaRadians, half));

            return Expression.Multiply(sine, sine);
        }

        // a = sin²(Δlat/2) + cos(viewerLat) · cos(jobLat) · sin²(Δlon/2)
        var haversine = Expression.Add(
            HalfSineSquared(DeltaRadians(jobLatitude, viewerLatitude)),
            Expression.Multiply(
                Expression.Multiply(
                    Expression.Call(Cos, Expression.Multiply(viewerLatitude, toRadians)),
                    Expression.Call(Cos, Expression.Multiply(jobLatitude, toRadians))),
                HalfSineSquared(DeltaRadians(jobLongitude, viewerLongitude))));

        // distance = 2R · atan2(√a, √(1 − a))
        var distance = Expression.Multiply(
            diameter,
            Expression.Call(
                Atan2,
                Expression.Call(Sqrt, haversine),
                Expression.Call(Sqrt, Expression.Subtract(one, haversine))));

        var hasCoordinates = Expression.AndAlso(
            Expression.NotEqual(Expression.Property(job, nameof(Job.Latitude)), Expression.Constant(null, typeof(double?))),
            Expression.NotEqual(Expression.Property(job, nameof(Job.Longitude)), Expression.Constant(null, typeof(double?))));

        return Expression.Lambda<Func<Job, double?>>(
            Expression.Condition(
                hasCoordinates,
                Expression.Convert(distance, typeof(double?)),
                Expression.Constant(null, typeof(double?))),
            job);
    }

    /// <summary>
    /// <c>true</c> when the job is within <paramref name="radiusKm"/> of the caller, and
    /// has coordinates to measure from. Expressed as a predicate so the comparison happens
    /// in SQL alongside the bounding box.
    /// </summary>
    public static Expression<Func<Job, bool>> WithinRadiusExpression(
        double latitude,
        double longitude,
        double radiusKm)
    {
        var distance = ExpressionFrom(latitude, longitude);
        var job = distance.Parameters[0];

        // The distance is double?, so the radius is lifted to match and a job without
        // coordinates compares as null, which is not <= the radius and is therefore
        // excluded.
        var withinRadius = Expression.LessThanOrEqual(
            distance.Body,
            Expression.Constant(radiusKm, typeof(double?)));

        return Expression.Lambda<Func<Job, bool>>(withinRadius, job);
    }

    /// <summary>
    /// Bounding box that fully contains <paramref name="radiusKm"/> around the caller.
    /// Filtering on it before the exact distance is what keeps the area/date indexes
    /// usable.
    /// </summary>
    public static (double MinLat, double MaxLat, double MinLng, double MaxLng) BoundingBox(
        double latitude,
        double longitude,
        double radiusKm)
    {
        // A degree of latitude is a constant; a degree of longitude shrinks towards
        // the poles, so the cos() of the caller's latitude is the correction factor.
        var latitudeDelta = radiusKm / GeoDistance.EarthRadiusKm * DegreesToRadians;

        var safeLatitude = Math.Clamp(latitude, -89.9, 89.9);

        var longitudeDelta =
            radiusKm / (GeoDistance.EarthRadiusKm * DegreesToRadians * Math.Cos(safeLatitude * DegreesToRadians));

        return (
            Math.Max(latitude - latitudeDelta, -90),
            Math.Min(latitude + latitudeDelta, 90),
            Math.Max(longitude - longitudeDelta, -180),
            Math.Min(longitude + longitudeDelta, 180));
    }

    private static Expression CoalesceZero(Expression nullable) =>
        Expression.Coalesce(nullable, Expression.Constant(0d));
}
