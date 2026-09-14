using System.Globalization;
using System.Security.Claims;
using ClosedXML.Excel;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Admin;

/// <summary>
/// سجل المراجعة — قراءة بس. الكتابة من <c>IAuditService</c>.
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize]
public class AuditController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly FastCom.Server.Services.IAuditService _audit;
    private readonly ILogger<AuditController> _logger;

    public AuditController(FastComDbContext db,
        FastCom.Server.Services.IAuditService audit,
        ILogger<AuditController> logger)
    { _db = db; _audit = audit; _logger = logger; }

    /// <summary>🔴 مدة الاحتفاظ بالسجل قبل التصدير والحذف — 6 شهور.</summary>
    private const int RetentionMonths = 6;

    public record Item(long AuditLogId, int? UserId, string? UserName,
        string Action, string EntityType, string? EntityId,
        string? Description, string? IpAddress, DateTime CreatedAt);

    public record Detail(long AuditLogId, int? UserId, string? UserName,
        string Action, string EntityType, string? EntityId,
        string? OldValues, string? NewValues, string? Description,
        string? IpAddress, string? UserAgent, DateTime CreatedAt);

    /* 🔴 نفس قائمة الـ CHECK في CK_AuditLogs_Action — للفلتر */
    private static readonly string[] Actions =
    {
        "Create","Update","Delete","Restore","Approve","Reject","Cancel",
        "Close","Reopen","Issue","Send","Settle","Login","Logout",
        "LoginFailed","PermissionChange","Export","Print","Download","Upload"
    };

    [HttpGet]
    [Authorize(Policy = "PERM:AUDIT.VIEW")]
    public async Task<IActionResult> List(
        [FromQuery] string? action, [FromQuery] string? entityType,
        [FromQuery] int? userId, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int page = 1, [FromQuery] int size = 50,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (size is < 1 or > 200) size = 50;

        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(action) && Actions.Contains(action))
            q = q.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(entityType))
            q = q.Where(a => a.EntityType == entityType);
        if (userId is > 0)
            q = q.Where(a => a.UserId == userId);
        if (DateOnly.TryParse(from, out var f))
        {
            var fd = f.ToDateTime(TimeOnly.MinValue);
            q = q.Where(a => a.CreatedAt >= fd);
        }
        if (DateOnly.TryParse(to, out var t))
        {
            /* «لغاية» يشمل اليوم كله */
            var td = t.ToDateTime(TimeOnly.MinValue).AddDays(1);
            q = q.Where(a => a.CreatedAt < td);
        }

        var total = await q.CountAsync(ct);

        var rows = await q.OrderByDescending(a => a.AuditLogId)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);

        /* 🔴 أسماء المستخدمين باستعلام منفصل — مافيش navigation property */
        var ids = rows.Where(a => a.UserId is not null).Select(a => a.UserId!.Value).Distinct().ToList();
        var userNames = (await _db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.UserName, u.FullName }).ToListAsync(ct))
            /* 🔴 CS8620: `UserName` هو `string?` في IdentityUser — فـ `?? ""` يضمن non-null */
            .ToDictionary(x => x.Id,
                          x => string.IsNullOrWhiteSpace(x.FullName) ? (x.UserName ?? "") : x.FullName);

        return Ok(new
        {
            total,
            page,
            size,
            items = rows.Select(a => new Item(
                a.AuditLogId, a.UserId,
                a.UserId is not null && userNames.TryGetValue(a.UserId.Value, out var un) ? un : null,
                a.Action, a.EntityType, a.EntityId, a.Description, a.IpAddress, a.CreatedAt)).ToList()
        });
    }

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:AUDIT.VIEW")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var a = await _db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.AuditLogId == id, ct);
        if (a is null) return NotFound(new { message = "السجل غير موجود" });

        string? name = null;
        if (a.UserId is not null)
        {
            var u = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == a.UserId, ct);
            name = u is null ? null : (string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName);
        }

        return Ok(new Detail(a.AuditLogId, a.UserId, name, a.Action, a.EntityType, a.EntityId,
            a.OldValues, a.NewValues, a.Description, a.IpAddress, a.UserAgent, a.CreatedAt));
    }

    /// <summary>أنواع الكيانات الموجودة — للفلتر.</summary>
    [HttpGet("entity-types")]
    [Authorize(Policy = "PERM:AUDIT.VIEW")]
    public async Task<IActionResult> EntityTypes(CancellationToken ct) =>
        Ok(await _db.AuditLogs.AsNoTracking()
            .Select(a => a.EntityType).Distinct().OrderBy(e => e).ToListAsync(ct));

    /// <summary>قائمة الحركات — مطابقة للـ CHECK.</summary>
    [HttpGet("actions")]
    [Authorize(Policy = "PERM:AUDIT.VIEW")]
    public IActionResult ActionsList() => Ok(Actions);

    // ══════════════════════════════════════════════════════════════════
    //  🗄️ الأرشفة — تصدير Excel ثم حذف (🆕 2026-09-13)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>صف أرشيف — كل الأعمدة عشان التصدير يبقى كامل.</summary>
    public record ArchiveRow(long AuditLogId, int? UserId, string? UserName,
        string Action, string EntityType, string? EntityId,
        string? OldValues, string? NewValues, string? Description,
        string? IpAddress, string? UserAgent, DateTime CreatedAt);

    /// <summary>
    /// 📥 <c>GET api/audit/archive-preview</c> — بيقول هيتأرشف كام سجل وإمتى.
    /// </summary>
    [HttpGet("archive-preview")]
    [Authorize(Policy = "PERM:AUDIT.VIEW")]
    public async Task<IActionResult> ArchivePreview(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-RetentionMonths);

        var q = _db.AuditLogs.AsNoTracking().Where(a => a.CreatedAt < cutoff);
        var count = await q.CountAsync(ct);
        var oldest = count == 0 ? (DateTime?)null : await q.MinAsync(a => a.CreatedAt, ct);
        var newest = count == 0 ? (DateTime?)null : await q.MaxAsync(a => a.CreatedAt, ct);

        /* تقدير الحجم — عشان المستخدم ياخد قرار وهو شايف */
        var sizes = await q
            .Select(a => (a.OldValues != null ? a.OldValues.Length : 0)
                       + (a.NewValues != null ? a.NewValues.Length : 0))
            .ToListAsync(ct);

        return Ok(new
        {
            retentionMonths = RetentionMonths,
            cutoff = cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            count,
            oldest = oldest?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            newest = newest?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            /* 🔴 تقدير تقريبي — nvarchar = 2 بايت للحرف + ~180 بايت للأعمدة التانية */
            estBytes = sizes.Sum(x => x * 2 + 180L)
        });
    }

    /// <summary>
    /// 📤 <c>GET api/audit/export</c> — تصدير سجل المراجعة لـ Excel.
    /// <para>🔴 <b>بيصدّر كل حاجة</b> (مش المؤرشف بس) إلا لو حددت <c>archivedOnly=true</c>.</para>
    /// </summary>
    [HttpGet("export")]
    [Authorize(Policy = "PERM:AUDIT.VIEW")]
    public async Task<IActionResult> Export(
        [FromQuery] bool archivedOnly = false,
        [FromQuery] string? from = null, [FromQuery] string? to = null,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-RetentionMonths);
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (archivedOnly) q = q.Where(a => a.CreatedAt < cutoff);
        if (DateOnly.TryParse(from, out var f)) q = q.Where(a => a.CreatedAt >= f.ToDateTime(TimeOnly.MinValue));
        if (DateOnly.TryParse(to, out var t))   q = q.Where(a => a.CreatedAt < t.ToDateTime(TimeOnly.MinValue).AddDays(1));

        /* 🔴 سقف أمان — 2 GB DB، ما نصدّرش مليون صف مرة واحدة */
        const int MaxExport = 100_000;
        var total = await q.CountAsync(ct);
        var truncated = total > MaxExport;

        var rows = await q.OrderByDescending(a => a.AuditLogId).Take(MaxExport).ToListAsync(ct);

        var ids = rows.Where(a => a.UserId is not null).Select(a => a.UserId!.Value).Distinct().ToList();
        var userNames = (await _db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.UserName, u.FullName }).ToListAsync(ct))
            /* 🔴 CS8620: `UserName` هو `string?` في IdentityUser — فـ `?? ""` يضمن non-null */
            .ToDictionary(x => x.Id,
                          x => string.IsNullOrWhiteSpace(x.FullName) ? (x.UserName ?? "") : x.FullName);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("سجل المراجعة");
        ws.RightToLeft = true;

        var title = archivedOnly
            ? $"سجل المراجعة — المؤرشف (أقدم من {RetentionMonths} شهور)"
            : "سجل المراجعة — كل السجلات";
        ws.Cell(1, 1).Value = title;
        ws.Cell(2, 1).Value = $"عدد السجلات: {rows.Count:N0}" + (truncated ? $" (من {total:N0} — اتقصّ عند {MaxExport:N0})" : "");
        ws.Cell(3, 1).Value = $"تاريخ التصدير: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(3, 1).Style.Font.Italic = true;

        var cols = new[] { "المعرف", "المستخدم", "الحركة", "الكيان", "رقم الكيان",
                           "القيم القديمة", "القيم الجديدة", "الوصف", "IP", "المتصفح", "التاريخ (UTC)" };
        var hr = 5;
        for (var c = 0; c < cols.Length; c++)
        {
            var cell = ws.Cell(hr, c + 1);
            cell.Value = cols[c];
            cell.Style.Font.SetBold();
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#16233A");
            cell.Style.Font.FontColor = XLColor.FromHtml("#FFFFFF");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var r = hr + 1;
        foreach (var a in rows)
        {
            var uname = a.UserId is not null && userNames.TryGetValue(a.UserId.Value, out var un)
                ? un : null;

            var vals = new object?[]
            {
                a.AuditLogId, uname ?? "—", ActionAr(a.Action), a.EntityType, a.EntityId,
                a.OldValues, a.NewValues, a.Description, a.IpAddress, a.UserAgent,
                a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };

            for (var c = 0; c < vals.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (vals[c])
                {
                    case long l:  cell.SetValue(l); break;
                    case string sx: cell.Value = sx; break;
                    case null:    cell.Value = "—"; break;
                    default:      cell.Value = vals[c]?.ToString() ?? "—"; break;
                }
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (vals[c] is string) cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
            r++;
        }

        if (r == hr + 1) ws.Cell(r, 1).Value = "مافيش سجلات في الفترة دي";

        for (var c = 1; c <= cols.Length; c++)
            ws.Column(c).Width = Math.Max(12, Math.Min(40, cols[c - 1].Length * 2 + 8));
        ws.SheetView.FreezeRows(hr);

        /* 🔴 نسجّل إن التصدير حصل — ده نفسه حدث أمني */
        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Export, "AuditLog", null,
            description: $"تصدير {(archivedOnly ? "السجل المؤرشف" : "سجل المراجعة")} — {rows.Count:N0} سجل",
            ct: ct);

        var name = $"fastcom-audit-{(archivedOnly ? "archive" : "full")}-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    /// <summary>
    /// 🗑️ <c>POST api/audit/archive</c> — **بيصدّر Excel الأول وبعدين يحذف**.
    ///
    /// <para>🔴 <b>مافيش حذف من غير تصدير ناجح.</b> لو الكتابة على الديسك فشلت،
    /// بنرجع 500 وما نحذفش حاجة — البيانات أهم من المساحة.</para>
    /// </summary>
    [HttpPost("archive")]
    [Authorize(Policy = "PERM:AUDIT.VIEW")]
    public async Task<IActionResult> Archive(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-RetentionMonths);

        /* 🔴 حذف بالدفعات — DELETE من غير TOP على جدول كبير بيعمل lock طويل */
        var ids = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.CreatedAt < cutoff)
            .Select(a => a.AuditLogId)
            .ToListAsync(ct);

        if (ids.Count == 0)
            return Ok(new { deleted = 0, message = "مافيش سجلات قديمة تتأرشف" });

        /* 1) نصدّر الأول — نفس منطق Export بس من غير سقف */
        var rows = await _db.AuditLogs.AsNoTracking()
            .Where(a => ids.Contains(a.AuditLogId))
            .OrderByDescending(a => a.AuditLogId)
            .ToListAsync(ct);

        var uids = rows.Where(a => a.UserId is not null).Select(a => a.UserId!.Value).Distinct().ToList();
        /* 🔴 قاموس أسماء جاهز (Id → اسم للعرض) — مش anonymous type */
        var userNames = (await _db.Users.AsNoTracking()
                .Where(u => uids.Contains(u.Id))
                .Select(u => new { u.Id, u.UserName, u.FullName }).ToListAsync(ct))
            /* 🔴 CS8620: `UserName` هو `string?` في IdentityUser — فـ `?? ""` يضمن non-null */
            .ToDictionary(x => x.Id,
                          x => string.IsNullOrWhiteSpace(x.FullName) ? (x.UserName ?? "") : x.FullName);

        byte[] bytes;
        try
        {
            bytes = BuildArchiveWorkbook(rows, userNames, cutoff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل إنشاء ملف الأرشيف — الحذف اتلغى");
            return StatusCode(500, new
            {
                message = "فشل إنشاء ملف الأرشيف — ماحذفناش حاجة. حاول تاني.",
                error = ex.Message
            });
        }

        /* 2) نحذف بالدفعات (1000 كل مرة) — عشان ما نعملش lock طويل على الجدول */
        var deleted = 0;
        const int Batch = 1000;
        for (var i = 0; i < ids.Count; i += Batch)
        {
            var chunk = ids.Skip(i).Take(Batch).ToList();
            deleted += await _db.AuditLogs.Where(a => chunk.Contains(a.AuditLogId))
                .ExecuteDeleteAsync(ct);
        }

        _logger.LogInformation("أُرشفت {Count} سجل مراجعة (أقدم من {Cutoff:yyyy-MM-dd})",
            deleted, cutoff);

        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Export, "AuditLog", null,
            description: $"أرشفة {deleted:N0} سجل (أقدم من {cutoff:yyyy-MM-dd}) + حذفها من الجدول",
            ct: ct);

        var name = $"fastcom-audit-archive-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    /// <summary>بيبنی ملف الأرشيف — منفصل عشان لو فشل ما نحذفش.</summary>
    private static byte[] BuildArchiveWorkbook(List<FastCom.Domain.Entities.AuditLog> rows,
        Dictionary<int, string> userNames, DateTime cutoff)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("الأرشيف");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = "سجل المراجعة — أرشيف محذوف من الجدول";
        ws.Cell(2, 1).Value = $"السجلات الأقدم من: {cutoff:yyyy-MM-dd}";
        ws.Cell(3, 1).Value = $"العدد: {rows.Count:N0} · تاريخ الأرشفة: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(3, 1).Style.Font.Italic = true;

        var cols = new[] { "المعرف", "المستخدم", "الحركة", "الكيان", "رقم الكيان",
                           "القيم القديمة", "القيم الجديدة", "الوصف", "IP", "المتصفح", "التاريخ (UTC)" };
        var hr = 5;
        for (var c = 0; c < cols.Length; c++)
        {
            var cell = ws.Cell(hr, c + 1);
            cell.Value = cols[c];
            cell.Style.Font.SetBold();
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#7A1F1F");
            cell.Style.Font.FontColor = XLColor.FromHtml("#FFFFFF");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var r = hr + 1;
        foreach (var a in rows)
        {
            var uname = a.UserId is not null && userNames.TryGetValue(a.UserId.Value, out var un)
                ? un : null;

            var vals = new object?[]
            {
                a.AuditLogId, uname ?? "—", ActionAr(a.Action), a.EntityType, a.EntityId,
                a.OldValues, a.NewValues, a.Description, a.IpAddress, a.UserAgent,
                a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };

            for (var c = 0; c < vals.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (vals[c])
                {
                    case long l:  cell.SetValue(l); break;
                    case string sx: cell.Value = sx; break;
                    case null:    cell.Value = "—"; break;
                    default:      cell.Value = vals[c]?.ToString() ?? "—"; break;
                }
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (vals[c] is string) cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
            r++;
        }

        for (var c = 1; c <= cols.Length; c++)
            ws.Column(c).Width = Math.Max(12, Math.Min(40, cols[c - 1].Length * 2 + 8));
        ws.SheetView.FreezeRows(hr);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>🔴 ترجمة الحركة — مافيش مصطلح إنجليزي للمستخدم (§59/§66).</summary>
    private static string ActionAr(string a) => a switch
    {
        "Create"           => "إنشاء",
        "Update"           => "تعديل",
        "Delete"           => "حذف",
        "Restore"          => "استعادة",
        "Approve"          => "اعتماد",
        "Reject"           => "رفض",
        "Cancel"           => "إلغاء",
        "Close"            => "إقفال",
        "Reopen"           => "إعادة فتح",
        "Issue"            => "إصدار",
        "Send"             => "إرسال",
        "Settle"           => "تسوية",
        "Login"            => "تسجيل دخول",
        "Logout"           => "تسجيل خروج",
        "LoginFailed"      => "محاولة دخول فاشلة",
        "PermissionChange" => "تغيير صلاحية",
        "Export"           => "تصدير",
        "Print"            => "طباعة",
        "Download"         => "تحميل",
        "Upload"           => "رفع ملف",
        _                  => a
    };
}
