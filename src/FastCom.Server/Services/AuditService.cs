using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;

namespace FastCom.Server.Services;

/// <summary>
/// سجل المراجعة — كان جدول <c>AuditLogs</c> موجود من غير أي كود بيكتب فيه.
/// </summary>
/// <remarks>
/// <para><b>🔴 fail-open.</b> لو الكتابة في السجل فشلت، العملية الأصلية
/// <b>ما تفشلش</b> — بنبلع الاستثناء ونسجّله في الـ logger.
/// السجل أداة مراقبة، مش جزء من صحة البيانات.</para>
///
/// <para><b>`Action` محصور بـ CHECK في قاعدة البيانات:</b>
/// <c>Create Update Delete Restore Approve Reject Cancel Close Reopen Issue
/// Send Settle Login Logout LoginFailed PermissionChange Export Print Download Upload</c>
/// — أي قيمة تانية = <c>SqlException</c>. استخدم الثوابت في <see cref="AuditActions"/>.</para>
///
/// <para><b>الاستخدام:</b> حقن <c>IAuditService</c> ونادِ <c>LogAsync</c> بعد
/// <c>SaveChangesAsync</c> الناجح — مش قبلها.</para>
/// </remarks>
public interface IAuditService
{
    /// <summary>
    /// يسجّل حركة. <paramref name="userId"/> = null ياخد المستخدم الحالي من الـ token.
    /// </summary>
    Task LogAsync(string action, string entityType, string? entityId = null,
        string? oldValues = null, string? newValues = null,
        string? description = null, int? userId = null,
        CancellationToken ct = default);
}

/// <summary>
/// أسماء الحركات المسموحة — مطابقة للـ CHECK في <c>CK_AuditLogs_Action</c>.
/// 🔴 استخدام نص حر = SqlException.
/// </summary>
public static class AuditActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string Restore = "Restore";
    public const string Approve = "Approve";
    public const string Reject = "Reject";
    public const string Cancel = "Cancel";
    public const string Close = "Close";
    public const string Reopen = "Reopen";
    public const string Issue = "Issue";
    public const string Send = "Send";
    public const string Settle = "Settle";
    public const string Login = "Login";
    public const string Logout = "Logout";
    public const string LoginFailed = "LoginFailed";
    public const string PermissionChange = "PermissionChange";
    public const string Export = "Export";
    public const string Print = "Print";
    public const string Download = "Download";
    public const string Upload = "Upload";
}

/// <inheritdoc />
public class AuditService : IAuditService
{
    private readonly FastComDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditService> _logger;

    public AuditService(FastComDbContext db, IHttpContextAccessor http, ILogger<AuditService> logger)
    {
        _db = db; _http = http; _logger = logger;
    }

    public async Task LogAsync(string action, string entityType, string? entityId = null,
        string? oldValues = null, string? newValues = null,
        string? description = null, int? userId = null,
        CancellationToken ct = default)
    {
        try
        {
            var ctx = _http.HttpContext;
            var uid = userId ?? CurrentUserId(ctx);

            _db.AuditLogs.Add(new AuditLog
            {
                UserId      = uid,
                Action      = action,
                EntityType  = entityType,
                EntityId    = Trunc(entityId, 100),
                OldValues   = oldValues,
                NewValues   = newValues,
                Description = Trunc(description, 1000),
                IpAddress   = Trunc(ctx?.Connection.RemoteIpAddress?.ToString(), 64),
                UserAgent   = Trunc(ctx?.Request.Headers.UserAgent.ToString(), 500),
                CreatedAt   = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            /* 🔴 fail-open — السجل ما يوقّفش الشغل */
            _logger.LogWarning(ex, "فشل تسجيل المراجعة: {Action} على {Entity} {Id}",
                action, entityType, entityId);
        }
    }

    private static int? CurrentUserId(HttpContext? ctx)
    {
        var raw = ctx?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    private static string? Trunc(string? s, int max) =>
        string.IsNullOrEmpty(s) ? null : (s.Length <= max ? s : s.Substring(0, max));
}
