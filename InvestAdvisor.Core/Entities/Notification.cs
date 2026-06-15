using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

/// <summary>
/// A persisted, per-tenant entry in the in-app notification bell. Written when a manual run
/// (dashboard/swing) finishes and when the worker raises an automatic alert. Survives restarts.
/// </summary>
public class Notification
{
    public long Id { get; set; }
    public int TenantId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    public DateTime CreatedUtc { get; set; }

    /// <summary>Null while unread; set the moment the tenant opens/clicks it.</summary>
    public DateTime? ReadUtc { get; set; }

    /// <summary>Optional in-app route to deep-link to (e.g. "/", "/swing", "/advice").</summary>
    public string? LinkUrl { get; set; }

    /// <summary>Set for advice-driven alerts so the UI can open the matching advice entry.</summary>
    public long? AdviceLogId { get; set; }
}
