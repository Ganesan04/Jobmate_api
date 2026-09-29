namespace Jobmate.Application.Common;

/// <summary>
/// The app's named routes, as Flutter declares them in <c>AppRoutes</c>.
///
/// A notification carries a deep link so tapping a row opens the right screen. Those
/// routes are written here, once, instead of as string literals at each call site: a
/// route typed by hand in a handler is a route the app cannot resolve, and the failure
/// only shows up as a tap that does nothing on a real device.
///
/// The id a screen needs is not part of the route. It travels in the notification's
/// <c>referenceId</c> and reaches the app as <c>targetId</c>, which is what
/// <c>AppRouter.push</c> hands to the screen as its route argument. So a row points at
/// <c>/job-details</c> and carries the job id, rather than at <c>/jobs/{id}</c> which
/// matches no named route.
/// </summary>
public static class NotificationRoutes
{
    /// <summary>A job's details, by <c>targetId</c>.</summary>
    public const string JobDetails = "/job-details";

    /// <summary>The employer-side applicant list for a job, by <c>targetId</c>.</summary>
    public const string JobApplications = "/job-applications";

    /// <summary>The employer-side shortlist / select flow for a job, by <c>targetId</c>.</summary>
    public const string SelectWorker = "/select-worker";

    /// <summary>The worker's own list of jobs and applications.</summary>
    public const string Applications = "/applications";

    /// <summary>One application, by <c>targetId</c>.</summary>
    public const string ApplicationDetails = "/application-details";

    /// <summary>A confirmed booking, by <c>targetId</c>.</summary>
    public const string AcceptedJob = "/accepted-job";

    /// <summary>The employer's list of posted jobs.</summary>
    public const string MyJobs = "/my-jobs";

    /// <summary>The worker's earnings and payouts.</summary>
    public const string Earnings = "/earnings";

    /// <summary>Finished shifts for either side.</summary>
    public const string WorkHistory = "/work-history";

    /// <summary>The rate-the-worker screen for a completed job, by <c>targetId</c>.</summary>
    public const string RateWorker = "/rate-worker";

    /// <summary>The conversation list.</summary>
    public const string Messages = "/messages";

    /// <summary>One conversation, by <c>targetId</c>.</summary>
    public const string Chat = "/chat";

    /// <summary>The verification centre.</summary>
    public const string Verification = "/verification";

    /// <summary>Profile and settings.</summary>
    public const string Profile = "/profile";
}
