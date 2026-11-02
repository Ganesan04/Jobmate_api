using FluentAssertions;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Tests.Domain;

/// <summary>
/// One account can work and hire, so the rule that turns an application into a booking
/// has to refuse a giver booking themselves, whatever path the application took.
/// </summary>
public sealed class SelectionRulesTests
{
    [Fact]
    public void A_giver_cannot_hire_themselves_onto_their_own_job()
    {
        var giverId = Guid.NewGuid();

        var job = new Job
        {
            Id = Guid.NewGuid(),
            GiverId = giverId,
            Status = JobStatus.Active,
            WorkersNeeded = 1
        };

        var application = new JobApplication
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            WorkerId = giverId,
            Status = ApplicationStatus.Applied
        };

        var act = () => SelectionRules.CreateAssignment(job, application, 1000, DateTime.UtcNow);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("OwnJob");
    }
}
