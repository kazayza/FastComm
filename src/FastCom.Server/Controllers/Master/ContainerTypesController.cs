using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// أنواع الحاويات (عادية/ثلاجة/مولد) — كانت **للقراءة بس** من `api/options/container-types`.
/// أهم إضافة: <b>مبلغ الضمان</b> لكل نوع — بيستخدمه حساب الضمان التلقائي على العمليات
/// (<c>api/operations/guarantee-estimate</c>).
/// نفس نمط <see cref="ExpenseTypesController"/> — حذف ناعم ومنع حذف نوع مستخدم.
/// </summary>
[ApiController]
[Route("api/container-types")]
[Authorize]
public class ContainerTypesController : ControllerBase
{
    private readonly FastComDbContext _db;
    public ContainerTypesController(FastComDbContext db) { _db = db; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    // ═══════════════ DTOs ═══════════════

    public record ListItem(int ContainerTypeId, string Code, string NameAr, string? NameEn,
        byte SizeFeet, bool IsReefer, decimal? GuaranteeAmount, bool IsActive, int UsageCount);

    public record Detail(int ContainerTypeId, string Code, string NameAr, string? NameEn,
        byte SizeFeet, bool IsReefer, decimal? GuaranteeAmount, bool IsActive);

    public record Upsert(string Code, string NameAr, string? NameEn,
        byte SizeFeet, bool IsReefer, decimal? GuaranteeAmount, bool IsActive);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> List([FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var q = _db.ContainerTypes.AsNoTracking().Where(t => !t.IsDeleted);
        if (activeOnly == true) q = q.Where(t => t.IsActive);

        /* الاستخدام = سطور حجوزات + حاويات مسجلة — قبل التعطيل/الحذف المستخدم يعرف */
        var lineCounts = await _db.BookingContainerLines.AsNoTracking()
            .GroupBy(l => l.ContainerTypeId)
            .Select(g => new { TypeId = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var boxCounts = await _db.Containers.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .GroupBy(c => c.ContainerTypeId)
            .Select(g => new { TypeId = g.Key, N = g.Count() })
            .ToListAsync(ct);

        var map = lineCounts.Concat(boxCounts)
            .GroupBy(x => x.TypeId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.N));

        var rows = await q.OrderBy(t => t.SizeFeet).ThenBy(t => t.Code)
            .Select(t => new ListItem(t.ContainerTypeId, t.Code, t.NameAr, t.NameEn,
                t.SizeFeet, t.IsReefer, t.GuaranteeAmount, t.IsActive, 0))
            .ToListAsync(ct);

        return Ok(rows.Select(r => r with
        {
            UsageCount = map.TryGetValue(r.ContainerTypeId, out var n) ? n : 0
        }).ToList());
    }

    // ═══════════════ GET ═══════════════

    [HttpGet("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var t = await _db.ContainerTypes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ContainerTypeId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "نوع الحاوية غير موجود" });

        return Ok(new Detail(t.ContainerTypeId, t.Code, t.NameAr, t.NameEn,
            t.SizeFeet, t.IsReefer, t.GuaranteeAmount, t.IsActive));
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Create([FromBody] Upsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var t = new ContainerType
        {
            Code            = req.Code.Trim().ToUpperInvariant(),
            NameAr          = req.NameAr.Trim(),
            NameEn          = req.NameEn?.Trim(),
            SizeFeet        = req.SizeFeet,
            IsReefer        = req.IsReefer,
            GuaranteeAmount = req.GuaranteeAmount is > 0 ? req.GuaranteeAmount : null,
            IsActive        = req.IsActive
        };

        _db.ContainerTypes.Add(t);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = t.ContainerTypeId, message = $"اتضاف نوع الحاوية «{t.NameAr}»" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Update(int id, [FromBody] Upsert req, CancellationToken ct)
    {
        var t = await _db.ContainerTypes.FirstOrDefaultAsync(x => x.ContainerTypeId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "نوع الحاوية غير موجود" });

        var err = await ValidateAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        var usedBy = await _db.BookingContainerLines.CountAsync(l => l.ContainerTypeId == id, ct);

        /* 🔴 نوع مستخدم في حجوزات — الكود مقفول. مبلغ الضمان يفضل قابل للتعديل
           (سعر الضمان بيتغير مع الوقت — ده مقصود). */
        if (usedBy > 0 && !string.Equals(t.Code, req.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = $"الكود مستخدم في {usedBy} سطر حجز — مايتغيرش. اعمل نوع جديد" });

        t.Code            = req.Code.Trim().ToUpperInvariant();
        t.NameAr          = req.NameAr.Trim();
        t.NameEn          = req.NameEn?.Trim();
        t.SizeFeet        = req.SizeFeet;
        t.IsReefer        = req.IsReefer;
        t.GuaranteeAmount = req.GuaranteeAmount is > 0 ? req.GuaranteeAmount : null;
        t.IsActive        = req.IsActive;

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"اتعدّل نوع الحاوية «{t.NameAr}»" });
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var t = await _db.ContainerTypes.FirstOrDefaultAsync(x => x.ContainerTypeId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "نوع الحاوية غير موجود" });

        var usedBy = await _db.BookingContainerLines.CountAsync(l => l.ContainerTypeId == id, ct);
        var boxes  = await _db.Containers.CountAsync(c => c.ContainerTypeId == id && !c.IsDeleted, ct);
        if (usedBy > 0 || boxes > 0)
            return BadRequest(new { message = $"النوع ده مستخدم ({usedBy} سطر حجز، {boxes} حاوية) — ماتحذفوش. عطّله بدل الحذف" });

        t.IsDeleted = true;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"اتحذف نوع الحاوية «{t.NameAr}»" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(Upsert? r, CancellationToken ct, int? currentId = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (string.IsNullOrWhiteSpace(r.Code))   return "الكود مطلوب";
        if (string.IsNullOrWhiteSpace(r.NameAr)) return "الاسم بالعربي مطلوب";
        if (r.SizeFeet is 0 or > 60)             return "المقاس بالقدم من 1 لـ 60";
        if (r.GuaranteeAmount is < 0)            return "مبلغ الضمان ماينفعش يكون بالسالب";

        var code = r.Code.Trim();
        if (code.Length > 30) return "الكود أطول من 30 حرف";
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z0-9_-]+$"))
            return "الكود بالحروف الإنجليزية والأرقام بس (زي DRY-40 أو REEFER-20)";

        var dup = await _db.ContainerTypes.AnyAsync(t =>
            !t.IsDeleted &&
            t.Code.ToLower() == code.ToLower() &&
            (currentId == null || t.ContainerTypeId != currentId), ct);
        if (dup) return $"الكود «{code.ToUpperInvariant()}» موجود أصلًا";

        return null;
    }
}
