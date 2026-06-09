namespace InvestAdvisor.Core.Enums;

/// <summary>The agent's suggested action for one held position. Decision support, not advice.</summary>
public enum PositionStance
{
    Hold,
    Add,
    Trim,
    Sell,
}

/// <summary>How strongly the data supports the stance, so the UI can surface the strongest calls first.</summary>
public enum PositionConviction
{
    Low,
    Medium,
    High,
}
