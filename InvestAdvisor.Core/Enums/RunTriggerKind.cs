namespace InvestAdvisor.Core.Enums;

public enum RunTriggerKind
{
    Scheduled = 0,
    DriftThreshold = 1,
    BigMove = 2,
    PriceTarget = 3,
    Manual = 4,
}
