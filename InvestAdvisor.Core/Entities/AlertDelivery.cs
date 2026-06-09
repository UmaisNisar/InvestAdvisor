using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

public class AlertDelivery
{
    public long Id { get; set; }
    public long AdviceLogId { get; set; }
    public AdviceLog? AdviceLog { get; set; }
    public string Channel { get; set; } = "Email";
    public DateTime? DeliveredAtUtc { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
}
