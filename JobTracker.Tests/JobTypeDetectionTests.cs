using JobTracker.Models;
using JobTracker.Services;
using Xunit;

namespace JobTracker.Tests;

public class JobTypeDetectionTests
{
    // --- Tier 1: Structured labels ---

    [Fact]
    public void DetectsContract_FromEmploymentTypeLabel()
    {
        var text = "Seniority level  Mid-Senior level  Employment type  Contract  Job function  Information Technology";
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DetectsContract_FromEmploymentTypeLabel_WithNewlines()
    {
        var text = "Employment type\n\nContract\n\nJob function";
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DetectsContract_FromHtmlWithTags()
    {
        var html = "<h3>Employment type</h3><span>Contract</span>";
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(html));
    }

    [Fact]
    public void DetectsPartTime_FromEmploymentTypeLabel()
    {
        var text = "Employment type  Part-time  Job function  Marketing";
        Assert.Equal(JobType.PartTime, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DetectsInternship_FromEmploymentTypeLabel()
    {
        var text = "Employment type  Internship  Job function  Engineering";
        Assert.Equal(JobType.Internship, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DetectsTemporary_FromEmploymentTypeLabel()
    {
        var text = "Employment type  Temporary  Job function  Admin";
        Assert.Equal(JobType.Temporary, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DetectsFullTime_FromEmploymentTypeLabel()
    {
        var text = "Employment type  Full-time  Job function  Engineering";
        Assert.Equal(JobType.FullTime, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DetectsContract_LabelTakesPriorityOverFullTimeKeyword()
    {
        var text = "This is a full-time team. Employment type  Contract  We offer great benefits.";
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DetectsContract_InLinkedInHtml()
    {
        var html = @"
            <div class=""description__job-criteria-list"">
                <li><h3>Seniority level</h3><span>Mid-Senior level</span></li>
                <li><h3>Employment type</h3><span>Contract</span></li>
                <li><h3>Job function</h3><span>Information Technology</span></li>
            </div>
            <div class=""description"">Looking for a full-time commitment to this contract.</div>";
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(html));
    }

    [Fact]
    public void DetectsFreelance_AsContract_FromLabel()
    {
        var text = "Employment type: Freelance";
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(text));
    }

    // --- Tier 2: Unambiguous contract indicators (work on any text size) ---

    [Theory]
    [InlineData("This role is outside IR35")]
    [InlineData("Inside IR35 contract opportunity")]
    [InlineData("IR35 status: outside")]
    public void DetectsContract_FromIR35(string text)
    {
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(text));
    }

    [Theory]
    [InlineData("12 month contract starting ASAP")]
    [InlineData("6 months contract with possible extension")]
    [InlineData("This is a 3 month contract")]
    [InlineData("2 year contract")]
    [InlineData("Initial 6 week contract")]
    public void DetectsContract_FromDurationContract(string text)
    {
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(text));
    }

    [Theory]
    [InlineData("Day rate: £500-£600")]
    [InlineData("Competitive daily rate offered")]
    public void DetectsContract_FromDayRate(string text)
    {
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(text));
    }

    [Theory]
    [InlineData("Fixed-term contract for 12 months")]
    [InlineData("This is a fixed term contract")]
    public void DetectsContract_FromFixedTermContract(string text)
    {
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(text));
    }

    [Theory]
    [InlineData("FTC starting in March")]
    [InlineData("freelance developer needed")]
    [InlineData("umbrella company or limited company")]
    [InlineData("contractor role in fintech")]
    [InlineData("contract position available now")]
    public void DetectsContract_FromOtherUnambiguousIndicators(string text)
    {
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DetectsContract_FromIR35_EvenInLargeText()
    {
        // Simulate a large HTML page (>15k chars) — tier 2 should still work
        var largeText = new string('x', 20000) + " This role is outside IR35. " + new string('y', 20000);
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(largeText));
    }

    [Fact]
    public void DetectsContract_FromMonthContract_EvenInLargeText()
    {
        var largeText = new string('x', 20000) + " 12 month contract " + new string('y', 20000);
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(largeText));
    }

    // --- Tier 3: Generic keywords (short text only) ---

    [Fact]
    public void DetectsContract_FromDescription()
    {
        var text = "We are looking for a C# developer for a 12 month contract role.";
        Assert.Equal(JobType.Contract, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DefaultsToFullTime_WhenNoIndicators()
    {
        var text = "We are looking for a software developer to join our team.";
        Assert.Equal(JobType.FullTime, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DoesNotFalsePositive_ContractManagement()
    {
        var text = "Experience in contract management and vendor negotiations required.";
        Assert.Equal(JobType.FullTime, LinkedInJobExtractor.DetectJobType(text));
    }

    [Fact]
    public void DoesNotFalsePositive_ContractWithClient()
    {
        var text = "You will manage the contract with our key clients.";
        Assert.Equal(JobType.FullTime, LinkedInJobExtractor.DetectJobType(text));
    }
}
