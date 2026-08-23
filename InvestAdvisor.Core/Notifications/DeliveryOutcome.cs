using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Notifications;

/// <summary>What one <see cref="Abstractions.INotificationChannel"/> did with one advice run. Logged, not persisted.</summary>
public sealed record DeliveryOutcome(string Channel, DeliveryStatus Status, string? ErrorMessage = null);
