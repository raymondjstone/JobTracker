using JobTracker.Models;
using Xunit;

namespace JobTracker.Tests;

public class HistoryActionTypeTests
{
    [Fact]
    public void ContactDiscussion_EnumValueExists()
    {
        // Verify the enum value exists (this will fail to compile if it doesn't)
        var actionType = HistoryActionType.ContactDiscussion;
        Assert.Equal("ContactDiscussion", actionType.ToString());
    }

    [Fact]
    public void AllHistoryActionTypes_CanBeEnumerated()
    {
        var allTypes = Enum.GetValues<HistoryActionType>();
        
        // Verify we have all expected types
        Assert.Contains(HistoryActionType.JobAdded, allTypes);
        Assert.Contains(HistoryActionType.JobDeleted, allTypes);
        Assert.Contains(HistoryActionType.AppliedStatusChanged, allTypes);
        Assert.Contains(HistoryActionType.ApplicationStageChanged, allTypes);
        Assert.Contains(HistoryActionType.InterestChanged, allTypes);
        Assert.Contains(HistoryActionType.SuitabilityChanged, allTypes);
        Assert.Contains(HistoryActionType.ContactAdded, allTypes);
        Assert.Contains(HistoryActionType.ContactRemoved, allTypes);
        Assert.Contains(HistoryActionType.InteractionAdded, allTypes);
        Assert.Contains(HistoryActionType.ContactDiscussion, allTypes);
    }

    [Fact]
    public void ContactDiscussion_IsNotSameAsOtherContactTypes()
    {
        Assert.NotEqual(HistoryActionType.ContactDiscussion, HistoryActionType.ContactAdded);
        Assert.NotEqual(HistoryActionType.ContactDiscussion, HistoryActionType.ContactRemoved);
        Assert.NotEqual(HistoryActionType.ContactDiscussion, HistoryActionType.InteractionAdded);
    }
}
