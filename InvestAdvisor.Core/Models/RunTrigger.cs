using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Models;

public sealed record RunTrigger(RunTriggerKind Kind, string Detail);
