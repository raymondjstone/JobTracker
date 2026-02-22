namespace JobTracker.Models;

public class JobRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }  // Foreign key to User
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } = 0; // Higher = checked first

    // Simple condition (for backwards compatibility and simple rules)
    public RuleField Field { get; set; } = RuleField.Title;
    public RuleOperator Operator { get; set; } = RuleOperator.Contains;
    public string Value { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; } = false;

    // Compound conditions (optional - if set, these are used instead of simple condition)
    public List<RuleCondition> Conditions { get; set; } = new();
    public ConditionLogic Logic { get; set; } = ConditionLogic.And;

    // Action
    public InterestStatus? SetInterest { get; set; }
    public SuitabilityStatus? SetSuitability { get; set; }
    public bool? SetIsRemote { get; set; } // null = no change, true = remote, false = onsite

    // Stats
    public int TimesTriggered { get; set; } = 0;
    public DateTime? LastTriggered { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.Now;

    // Helper to check if using compound conditions
    public bool HasCompoundConditions => Conditions.Count > 0;
}

public class RuleCondition
{
    public RuleField Field { get; set; } = RuleField.Title;
    public RuleOperator Operator { get; set; } = RuleOperator.Contains;
    public string Value { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; } = false;
}

public enum RuleField
{
    Title,
    Description,
    Company,
    Location,
    Salary,
    Source,
    Skills,
    IsRemote,
    SuitabilityScore,  // ML-based score (0-100)
    Any
}

public enum RuleOperator
{
    Contains,
    NotContains,
    Equals,
    NotEquals,
    StartsWith,
    EndsWith,
    Regex,
    IsTrue,
    IsFalse,
    GreaterThan,        // For numeric comparisons (score, salary)
    GreaterThanOrEqual, // For numeric comparisons
    LessThan,           // For numeric comparisons
    LessThanOrEqual     // For numeric comparisons
}

public enum ConditionLogic
{
    And,  // All conditions must match
    Or    // Any condition must match
}
