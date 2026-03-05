using JobTracker.Models;
using JobTracker.Services;
using Xunit;

namespace JobTracker.Tests;

public class EmailReplyMatcherTests
{
    private readonly EmailReplyMatcher _matcher = new();

    private static JobListing CreateJob(string company, string url = "", ApplicationStage stage = ApplicationStage.Applied, bool hasApplied = true)
    {
        return new JobListing
        {
            Id = Guid.NewGuid(),
            Title = "Software Engineer",
            Company = company,
            Url = url,
            ApplicationStage = stage,
            HasApplied = hasApplied,
            DateApplied = DateTime.Now,
        };
    }

    private static IncomingEmail CreateEmail(string fromAddress, string subject, string body = "")
    {
        return new IncomingEmail
        {
            FromAddress = fromAddress,
            Subject = subject,
            TextBody = body,
        };
    }

    // === Stage Detection ===

    [Fact]
    public void DetectsInterviewInvitation()
    {
        var job = CreateJob("Acme Corp", "https://acme.com/jobs/123");
        var email = CreateEmail("hr@acme.com", "Interview Invitation", "We would like to invite you to an interview for the Software Engineer role.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(ApplicationStage.Interview, result.SuggestedStage);
    }

    [Fact]
    public void DetectsRejection()
    {
        var job = CreateJob("BigCo", "https://bigco.com/careers");
        var email = CreateEmail("careers@bigco.com", "Application Update", "Unfortunately, we regret to inform you that you have not been successful in your application.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(ApplicationStage.Rejected, result.SuggestedStage);
    }

    [Fact]
    public void DetectsTechTest()
    {
        var job = CreateJob("TechFirm", "https://techfirm.com/job/1");
        var email = CreateEmail("hiring@techfirm.com", "Next Steps", "Please complete the following coding challenge on HackerRank.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(ApplicationStage.TechTest, result.SuggestedStage);
    }

    [Fact]
    public void DetectsOffer()
    {
        var job = CreateJob("DreamCo", "https://dreamco.com/role", ApplicationStage.Interview);
        var email = CreateEmail("hr@dreamco.com", "Job Offer", "We are pleased to offer you the position of Software Engineer.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(ApplicationStage.Offer, result.SuggestedStage);
    }

    [Fact]
    public void DetectsApplicationReceived()
    {
        var job = CreateJob("NewCo", "https://newco.com/j/1", ApplicationStage.Applied);
        var email = CreateEmail("noreply@newco.com", "Application Received", "Thank you for applying for the Software Engineer position.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(ApplicationStage.Pending, result.SuggestedStage);
    }

    // === Domain Matching ===

    [Fact]
    public void MatchesByCompanyDomain()
    {
        var job = CreateJob("Acme Corp", "https://acme.com/jobs/123");
        var email = CreateEmail("hr@acme.com", "Interview", "We'd like to schedule an interview.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(job.Id, result.JobId);
    }

    [Fact]
    public void MatchesByCompanyNameWhenNoUrl()
    {
        var job = CreateJob("TechStartup", "");
        var email = CreateEmail("jobs@techstartup.com", "Interview", "We'd like to invite you for an interview.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(job.Id, result.JobId);
    }

    [Fact]
    public void MatchesSubdomainToBaseDomain()
    {
        var job = CreateJob("PayCorp", "https://paycorp.co.uk/careers");
        var email = CreateEmail("recruitment@jobs.paycorp.co.uk", "Interview Invitation", "We invite you to an interview.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
    }

    // === Ignored Senders ===

    [Theory]
    [InlineData("noreply@paypal.com")]
    [InlineData("updates@amazon.co.uk")]
    [InlineData("notifications@facebook.com")]
    [InlineData("receipt@stripe.com")]
    public void IgnoresKnownTransactionalSenders(string sender)
    {
        var job = CreateJob("SomeCo", "https://someco.com");
        var email = CreateEmail(sender, "Your Application", "We invite you to an interview.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.False(result.Matched);
    }

    [Theory]
    [InlineData("recruiter@gmail.com")]
    [InlineData("hr@hotmail.com")]
    [InlineData("jobs@outlook.com")]
    public void DoesNotMatchGenericEmailProviders(string sender)
    {
        var job = CreateJob("Gmail", "https://gmail.com"); // Even if company name matches
        var email = CreateEmail(sender, "Interview", "We'd like to schedule an interview.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.False(result.Matched);
    }

    // === Stage Progression Guard ===

    [Fact]
    public void DoesNotRegressFromInterviewToPending()
    {
        var job = CreateJob("AdvancedCo", "https://advancedco.com", ApplicationStage.Interview);
        var email = CreateEmail("hr@advancedco.com", "Thanks", "Thank you for your application. We have received your application.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        // Pending is lower than Interview, so should not match
        Assert.False(result.Matched);
    }

    [Fact]
    public void AllowsRejectionFromAnyStage()
    {
        var job = CreateJob("RejectCo", "https://rejectco.com", ApplicationStage.Interview);
        var email = CreateEmail("hr@rejectco.com", "Update", "Unfortunately, we have decided to proceed with other candidates.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(ApplicationStage.Rejected, result.SuggestedStage);
    }

    [Fact]
    public void DoesNotDoubleReject()
    {
        var job = CreateJob("AlreadyRejected", "https://alreadyrejected.com", ApplicationStage.Rejected);
        var email = CreateEmail("hr@alreadyrejected.com", "Update", "Unfortunately, we have decided to proceed with other candidates.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.False(result.Matched);
    }

    // === Contact Matching ===

    [Fact]
    public void MatchesByInlineContact()
    {
        var job = CreateJob("SecretCo", "https://example.com/job/1");
        job.Contacts = new List<ContactEntry>
        {
            new ContactEntry { Email = "john@secretco.com", Name = "John" }
        };

        var email = CreateEmail("john@secretco.com", "Interview", "We'd like to schedule an interview.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(job.Id, result.JobId);
    }

    // === Edge Cases ===

    [Fact]
    public void EmptySenderReturnsNoMatch()
    {
        var job = CreateJob("Anything", "https://anything.com");
        var email = CreateEmail("", "Interview", "We'd like to schedule an interview.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.False(result.Matched);
    }

    [Fact]
    public void NoJobsReturnsNoMatch()
    {
        var email = CreateEmail("hr@someco.com", "Interview", "We'd like to schedule an interview.");
        var result = _matcher.Match(email, new List<JobListing>(), new List<Contact>());

        Assert.False(result.Matched);
    }

    [Fact]
    public void NoStageKeywordsReturnsNoMatch()
    {
        var job = CreateJob("Acme", "https://acme.com");
        var email = CreateEmail("hr@acme.com", "Hello", "Just wanted to follow up on some internal matters.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.False(result.Matched);
    }

    [Fact]
    public void PrefersAppliedJobOverNonApplied()
    {
        var notApplied = CreateJob("SharedCo", "https://sharedco.com/job/1", ApplicationStage.None, hasApplied: false);
        var applied = CreateJob("SharedCo", "https://sharedco.com/job/2", ApplicationStage.Applied, hasApplied: true);

        var email = CreateEmail("hr@sharedco.com", "Interview", "We'd like to invite you to an interview.");
        var result = _matcher.Match(email, new List<JobListing> { notApplied, applied }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(applied.Id, result.JobId);
    }

    [Fact]
    public void MatchesPhoneScreenAsInterview()
    {
        var job = CreateJob("PhoneCo", "https://phoneco.com");
        var email = CreateEmail("hr@phoneco.com", "Next Steps", "We'd like to arrange a phone screen with you.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(ApplicationStage.Interview, result.SuggestedStage);
    }

    [Fact]
    public void DetectsPositionFilled()
    {
        var job = CreateJob("FilledCo", "https://filledco.com");
        var email = CreateEmail("hr@filledco.com", "Update", "We wanted to let you know that the position has been filled.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(ApplicationStage.Rejected, result.SuggestedStage);
    }

    [Fact]
    public void DetectsTakeHomeTest()
    {
        var job = CreateJob("TestCo", "https://testco.com");
        var email = CreateEmail("hr@testco.com", "Assessment", "Please complete the following take-home assignment.");
        var result = _matcher.Match(email, new List<JobListing> { job }, new List<Contact>());

        Assert.True(result.Matched);
        Assert.Equal(ApplicationStage.TechTest, result.SuggestedStage);
    }
}
