using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Notifications;

/// <summary>
/// DB-backed implementation of the notification bell. Registered as a singleton and uses
/// <see cref="IDbContextFactory{TContext}"/> so it is safe to call from the worker, the run manager,
/// and concurrent Blazor circuits.
/// </summary>
public sealed class NotificationCenter(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    ISystemClock clock) : INotificationCenter
{
    public event EventHandler<int>? NotificationsChanged;

    public async Task<Notification> AddAsync(int tenantId, NotificationDraft draft, CancellationToken ct = default)
    {
        var notification = new Notification
        {
            TenantId = tenantId,
            Title = draft.Title,
            Body = draft.Body,
            Severity = draft.Severity,
            LinkUrl = draft.LinkUrl,
            AdviceLogId = draft.AdviceLogId,
            CreatedUtc = clock.UtcNow,
        };

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct); // assigns Id
        }

        NotificationsChanged?.Invoke(this, tenantId);
        return notification;
    }

    public async Task<IReadOnlyList<Notification>> ListRecentAsync(int tenantId, int take = 20, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications
            .AsNoTracking()
            .Where(n => n.TenantId == tenantId)
            .OrderByDescending(n => n.CreatedUtc)
            .ThenByDescending(n => n.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> UnreadCountAsync(int tenantId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications.CountAsync(n => n.TenantId == tenantId && n.ReadUtc == null, ct);
    }

    public async Task MarkReadAsync(int tenantId, long id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Notifications.FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Id == id, ct);
        if (row is null || row.ReadUtc is not null) return;
        row.ReadUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        NotificationsChanged?.Invoke(this, tenantId);
    }

    public async Task MarkAllReadAsync(int tenantId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unread = await db.Notifications
            .Where(n => n.TenantId == tenantId && n.ReadUtc == null)
            .ToListAsync(ct);
        if (unread.Count == 0) return;
        var now = clock.UtcNow;
        foreach (var n in unread) n.ReadUtc = now;
        await db.SaveChangesAsync(ct);
        NotificationsChanged?.Invoke(this, tenantId);
    }
}
