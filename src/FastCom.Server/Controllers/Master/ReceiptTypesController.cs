using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// أنواع الإيصالات — جدول قابل للتوسيع (كاشير، رسم إفراج، أرضيات، غرامة…).
/// المستخدم بيضيف أنواع جديدة بنفسه من غير ما يدخل قاعدة البيانات.
/// نفس نمط <see cref="ExpenseTypesController"/> — حذف ناعم ومنع حذف نوع مستخدم.
/// </summary>
[ApiController]
[Route("api/receipt-types")]
[Authorize]
public class ReceiptTypesController : ControllerBase
{
    private readonly FastComDbContext _db;
    public ReceiptTypesController(FastComDbContext db) { _db = db; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    // ═══════════════ DTOs ═══════════════

    public record ListItem(int ReceiptTypeId, string Code, string NameAr, string? NameEn,
        bool IsActive, int ExpenseCount);

    public record Detail(int ReceiptTypeId, string Code, string NameAr, string? NameEn, bool IsActive);

    public record Upsert(string Code, string NameAr, string? NameEn, bool IsActive);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> List([FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var q = _db.ReceiptTypes.AsNoTracking().Where(t => !t.IsDeleted);
        if (activeOnly == true) q = q.Where(t => t.IsActive);

        /* عدد المصروفات على كل نوع — قبل التعديل أو التعطيل المستخدم يعرف هو مستخدم ولا لأ */
        var counts = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.ReceiptTypeId != null)
            .GroupBy(e => e.ReceiptTypeId!.Value)
            .Select(g => new { TypeId = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var map = counts.ToDictionary(x => x.TypeId, x => x.N);

        var rows = await q.OrderBy(t => t.Code)
            .Select(t => new ListItem(t.ReceiptTypeId, t.Code, t.NameAr, t.NameEn, t.IsActive, 0))
            .ToListAsync(ct);

        return Ok(rows.Select(r => r with
        {
            ExpenseCount = map.TryGetValue(r.ReceiptTypeId, out var n) ? n : 0
        }).ToList());
    }

    // ═══════════════ GET ═══════════════

    [HttpGet("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var t = await _db.ReceiptTypes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ReceiptTypeId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "نوع الإيصال غير موجود" });

        return Ok(new Detail(t.ReceiptTypeId, t.Code, t.NameAr, t.NameEn, t.IsActive));
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Create([FromBody] Upsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var t = new ReceiptType
        {
            Code      = req.Code.Trim().ToUpperInvariant(),
            NameAr    = req.NameAr.Trim(),
            NameEn    = req.NameEn?.Trim(),
            IsActive  = req.IsActive,
            CreatedBy = CurrentUserId()
        };

        _db.ReceiptTypes.Add(t);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = t.ReceiptTypeId, message = $"اتضاف نوع الإيصال «{t.NameAr}»" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Update(int id, [FromBody] Upsert req, CancellationToken ct)
    {
        var t = await _db.ReceiptTypes.FirstOrDefaultAsync(x => x.ReceiptTypeId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "نوع الإيصال غير موجود" });

        var err = await ValidateAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        var usedBy = await _db.Expenses.CountAsync(e => e.ReceiptTypeId == id && !e.IsDeleted, ct);

        /* 🔴 نوع عليه مصروفات — الكود مقفول (زي أنواع المصروفات بالظبط) */
        if (usedBy > 0 && !string.Equals(t.Code, req.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = $"الكود مستخدم في {usedBy} مصروف — مايتغيرش. اعمل نوع جديد" });

        t.Code      = req.Code.Trim().ToUpperInvariant();
        t.NameAr    = req.NameAr.Trim();
        t.NameEn    = req.NameEn?.Trim();
        t.IsActive  = req.IsActive;
        t.UpdatedAt = DateTime.UtcNow;
        t.UpdatedBy = CurrentUserId();

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"اتعدّل نوع الإيصال «{t.NameAr}»" });
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var t = await _db.ReceiptTypes.FirstOrDefaultAsync(x => x.ReceiptTypeId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "نوع الإيصال غير موجود" });

        var usedBy = await _db.Expenses.CountAsync(e => e.ReceiptTypeId == id && !e.IsDeleted, ct);
        if (usedBy > 0)
            return BadRequest(new { message = $"النوع ده عليه {usedBy} مصروف — ماتحذفوش. عطّله بدل الحذف" });

        t.IsDeleted  = true;
        t.UpdatedAt  = DateTime.UtcNow;
        t.UpdatedBy  = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"اتحذف نوع الإيصال «{t.NameAr}»" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(Upsert? r, CancellationToken ct, int? currentId = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (string.IsNullOrWhiteSpace(r.Code))   return "الكود مطلوب";
        if (string.IsNullOrWhiteSpace(r.NameAr)) return "الاسم بالعربي مطلوب";

        var code = r.Code.Trim();
        if (code.Length > 30) return "الكود أطول من 30 حرف";
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z0-9_-]+$"))
            return "الكود بالحروف الإنجليزية والأرقام بس (زي CASHIER أو DEMURRAGE)";

        var dup = await _db.ReceiptTypes.AnyAsync(t =>
            !t.IsDeleted &&
            t.Code.ToLower() == code.ToLower() &&
            (currentId == null || t.ReceiptTypeId != currentId), ct);
        if (dup) return $"الكود «{code.ToUpperInvariant()}» موجود أصلًا";

        return null;
    }
}
