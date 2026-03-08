using JobTracker.Models;
using JobTracker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JobTracker.Tests;

public class JobRulesValidationTests
{
    private readonly JobRulesService _service;

    public JobRulesValidationTests()
    {
        var userId = Guid.NewGuid();

        var currentUserMock = new Mock<CurrentUserService>(
            MockBehavior.Loose,
            new object[] { null!, null!, null!, null! });
        currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(userId);

        var settingsMock = new Mock<AppSettingsService>(
            MockBehavior.Loose,
            new object[] { null!, null!, null!, null! });
        var settings = new AppSettings();
        settingsMock.Setup(s => s.GetSettings(It.IsAny<Guid?>())).Returns(settings);

        var logger = NullLogger<JobRulesService>.Instance;

        _service = new JobRulesService(
            settingsMock.Object,
            logger,
            currentUserMock.Object);
    }

    [Fact]
    public void AddRule_WithValidRegex_Succeeds()
    {
        var rule = new JobRule
        {
            Name = "Test",
            Operator = RuleOperator.Regex,
            Value = @"\b(Senior|Lead)\b",
            SetInterest = InterestStatus.Interested
        };

        // Should not throw
        _service.AddRule(rule);
    }

    [Fact]
    public void AddRule_WithInvalidRegex_ThrowsArgumentException()
    {
        var rule = new JobRule
        {
            Name = "Bad Regex",
            Operator = RuleOperator.Regex,
            Value = @"[invalid((",
            SetInterest = InterestStatus.Interested
        };

        Assert.Throws<ArgumentException>(() => _service.AddRule(rule));
    }

    [Fact]
    public void AddRule_WithInvalidRegexInCompoundCondition_ThrowsArgumentException()
    {
        var rule = new JobRule
        {
            Name = "Compound Bad Regex",
            Conditions = new List<RuleCondition>
            {
                new() { Field = RuleField.Title, Operator = RuleOperator.Contains, Value = "test" },
                new() { Field = RuleField.Description, Operator = RuleOperator.Regex, Value = @"***bad" }
            },
            Logic = ConditionLogic.And,
            SetInterest = InterestStatus.NotInterested
        };

        Assert.Throws<ArgumentException>(() => _service.AddRule(rule));
    }

    [Fact]
    public void AddRule_NonRegexOperator_SkipsValidation()
    {
        var rule = new JobRule
        {
            Name = "Simple Contains",
            Operator = RuleOperator.Contains,
            Value = "developer",
            SetInterest = InterestStatus.Interested
        };

        // Should not throw even with characters that would be invalid regex
        _service.AddRule(rule);
    }
}
