using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Admin;

/// <summary>
/// الإشعارات — <c>UserId = NULL</c> معناه إشعام عام لكل المستخدمين.
/// <para>مافيش <c>NOTIFICATION.VIEW</c> في الـ schema — فالقراءة متاحة لأي مستخدم مسجّل
/// (بيشوف بتاعه + العام)، والإنشاء والحذف بصلاحية <c>NOTIFICATION.MANAGE</c>.</para>
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private static readonly string[] Priorities = { "Low", "Normal", "High", "Critical" };

    private readonly FastComDbContext _db;
    private readonly IPermissionService _perms;

    public NotificationsController(FastComDbContext db, IPermissionService perms)
    { _db = db; _perms = perms; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ═══════════════ DTOs ═══════════════

    public record Item(long NotificationId, string NotificationType, string Title, string Message,
        string? EntityType, long? EntityId, string Priority, bool IsRead, DateTime CreatedAt,
        DateTime? ReadAt, bool IsGlobal);

    public record CreateRequest(string? NotificationType, string? Title, string? Message,
        string? Priority, int? UserId, string? EntityType, long? EntityId);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    public async Task<IActionResult> List(bool unreadOnly = false, int take = 100,
        CancellationToken ct = default)
    {
        if (take <= 0 || take > 500) take = 100;
        var uid = CurrentUserId();

        var query = _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == uid || n.UserId == null);

        if (unreadOnly) query = query.Where(n => !n.IsRead);

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new Item(n.NotificationId, n.NotificationType, n.Title, n.Message,
                n.EntityType, n.EntityId, n.Priority, n.IsRead, n.CreatedAt, n.ReadAt,
                n.UserId == null))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>عدد اللي لسه مااتقراش — للجرس في الـ Header.</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        var uid = CurrentUserId();
        var n = await _db.Notifications.AsNoTracking()
            .CountAsync(x => (x.UserId == uid || x.UserId == null) && !x.IsRead, ct);
        return Ok(new { count = n });
    }

    // ═══════════════ تعليم كمقروء ═══════════════

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> Read(long id, CancellationToken ct)
    {
        var uid = CurrentUserId();
        var n = await _db.Notifications
            .FirstOrDefaultAsync(x => x.NotificationId == id &&
                                      (x.UserId == uid || x.UserId == null), ct);
        if (n is null) return NotFound(new { message = "الإشعار مش موجود" });
        if (n.IsRead) return Ok(new { message = "مقروء أصلًا" });

        // الإشعار العام بيتعلّم مقروء للمستخدم ده بنسخة خاصة — عشان مايتخفيش عن الباقين
        if (n.UserId is null)
        {
            _db.Notifications.Add(new Notification
            {
                UserId           = uid,
                BranchId         = n.BranchId,
                NotificationType = n.NotificationType,
                Title            = n.Title,
                Message          = n.Message,
                EntityType       = n.EntityType,
                EntityId         = n.EntityId,
                Priority         = n.Priority,
                IsRead           = true,
                ReadAt           = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "✅ اتعلّم مقروء" });
        }

        n.IsRead = true;
        n.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اتعلّم مقروء" });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var uid = CurrentUserId();

        var mine = await _db.Notifications
            .Where(n => n.UserId == uid && !n.IsRead).ToListAsync(ct);
        foreach (var n in mine) { n.IsRead = true; n.ReadAt = DateTime.UtcNow; }

        // العام — نسخ مقروءة للمستخدم ده
        var globals = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == null && !n.IsRead).ToListAsync(ct);
        foreach (var g in globals)
        {
            _db.Notifications.Add(new Notification
            {
                UserId           = uid,
                BranchId         = g.BranchId,
                NotificationType = g.NotificationType,
                Title            = g.Title,
                Message          = g.Message,
                EntityType       = g.EntityType,
                EntityId         = g.EntityId,
                Priority         = g.Priority,
                IsRead           = true,
                ReadAt           = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعلّم {mine.Count + globals.Count} إشعار كمقروء" });
    }

    // ═══════════════ إنشاء ═══════════════

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (!await _perms.HasAsync(CurrentUserId(), "NOTIFICATION.MANAGE", ct))
            return Forbid();
        if (req is null) return BadRequest(new { message = "البيانات مش كاملة" });
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { message = "العنوان مطلوب" });
        if (string.IsNullOrWhiteSpace(req.Message)) return BadRequest(new { message = "نص الإشعار مطلوب" });
        if (req.Title.Length > 200) return BadRequest(new { message = "العنوان أطول من 200 حرف" });
        if (req.Message.Length > 1000) return BadRequest(new { message = "النص أطول من 1000 حرف" });

        var priority = string.IsNullOrWhiteSpace(req.Priority) ? "Normal" : req.Priority.Trim();
        if (!Priorities.Contains(priority))
            return BadRequest(new { message = "الأولوية لازم تكون Low أو Normal أو High أو Critical" });

        if (req.UserId is not null &&
            !await _db.Users.AnyAsync(u => u.Id == req.UserId, ct))
            return BadRequest(new { message = "المستخدم ده مش موجود — سيبه فاضي عشان يكون إشعار عام" });

        _db.Notifications.Add(new Notification
        {
            UserId           = req.UserId,
            BranchId         = int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null,
            NotificationType = B(req.NotificationType) ?? "General",
            Title            = req.Title.Trim(),
            Message          = req.Message.Trim(),
            EntityType       = B(req.EntityType),
            EntityId         = req.EntityId,
            Priority         = priority,
            IsRead           = false
        });
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = req.UserId is null ? "✅ اتبعت الإشعار لكل المستخدمين" : "✅ اتبعت الإشعار" });
    }

    // ═══════════════ حذف ═══════════════

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var uid = CurrentUserId();
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.NotificationId == id, ct);
        if (n is null) return NotFound(new { message = "الإشعار مش موجود" });

        // بتاعه هو — يحذفه. غير كده لازم صلاحية الإدارة.
        if (n.UserId != uid && !await _perms.HasAsync(uid, "NOTIFICATION.MANAGE", ct))
            return Forbid();

        _db.Notifications.Remove(n);
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "🗑️ اتحذف الإشعار" });
    }
}
