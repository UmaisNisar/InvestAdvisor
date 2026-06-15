using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// The in-app notification "bell" store. Singleton: the worker (background scope) and every Blazor
/// circuit share one instance. Writes are persisted per tenant; <see cref="NotificationAdded"/> lets
/// open bells live-update their unread badge.
/// </summary>
public interface INotificationCenter
{
    /// <summary>
    /// Raised whenever a tenant's notifications change (added, read, or all-read) so every open bell —
    /// across circuits and both responsive placements — can re-pull its list and unread count. The
    /// payload is the affected tenant id, so a circuit ignores other tenants' changes.
    /// </summary>
    event EventHandler<int>? NotificationsChanged;

    Task<Notification> AddAsync(int tenantId, NotificationDraft draft, CancellationToken ct = default);

    Task<IReadOnlyList<Notification>> ListRecentAsync(int tenantId, int take = 20, CancellationToken ct = default);

    Task<int> UnreadCountAsync(int tenantId, CancellationToken ct = default);

    Task MarkReadAsync(int tenantId, long id, CancellationToken ct = default);

    Task MarkAllReadAsync(int tenantId, CancellationToken ct = default);
}

/// <summary>The fields a caller supplies to raise a notification; timestamps are stamped by the store.</summary>
public sealed record NotificationDraft(
    string Title,
    string Body,
    NotificationSeverity Severity = NotificationSeverity.Info,
    string? LinkUrl = null,
    long? AdviceLogId = null);
