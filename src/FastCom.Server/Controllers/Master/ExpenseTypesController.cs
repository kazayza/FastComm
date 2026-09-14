using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// أنواع المصروفات — كانت **للقراءة بس** من `api/options/expense-types`.
/// المستخدم محتاج يضيف نوع جديد ويعدّل في الموجود من غير ما يدخل قاعدة البيانات.
/// </summary>
/// <remarks>
/// <para><b>ليه الصلاحيات `MASTERDATA` مش `EXPENSE`؟</b>
/// أنواع المصروفات **بيانات أساسية** زي أنواع الحاويات — مش عملية مالية.
/// ولو ربطناها بـ `EXPENSE.CREATE` أي موظف بيسجّل مصروف هيقدر يغيّر التصنيف،
/// وده بيبوّظ التقارير.</para>
///
/// <para><b>الحذف ناعم</b> (`IsDeleted = 1`) — عشان المصروفات القديمة
/// تفضل شايفة نوعها. وممنوع حذف نوع مستخدم أصلًا.</para>
/// </remarks>
[ApiController]
[Route("api/expense-types")]
[Authorize]
public class ExpenseTypesController : ControllerBase
{
    private readonly FastComDbContext _db;
    public ExpenseTypesController(FastComDbContext db) { _db = db; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    // ═══════════════ DTOs ═══════════════

    public record ListItem(int ExpenseTypeId, string Code, string NameAr, string? NameEn,
        bool IsCustodyAllowed, bool IsOperationCost, bool IsTaxDeductible,
        bool IsActive, int ExpenseCount);

    public record Detail(int ExpenseTypeId, string Code, string NameAr, string? NameEn,
        bool IsCustodyAllowed, bool IsOperationCost, bool IsTaxDeductible, bool IsActive);

    public record Upsert(string Code, string NameAr, string? NameEn,
        bool IsCustodyAllowed, bool IsOperationCost, bool IsTaxDeductible, bool IsActive);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> List([FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var q = _db.ExpenseTypes.AsNoTracking().Where(t => !t.IsDeleted);
        if (activeOnly == true) q = q.Where(t => t.IsActive);

        /* 🔴 عدد المصروفات على كل نوع — عشان المستخدم يعرف
           هل النوع ده مستخدم ولا لأ قبل ما يعدّل أو يعطّل. */
        var counts = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted)
            .GroupBy(e => e.ExpenseTypeId)
            .Select(g => new { TypeId = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var map = counts.ToDictionary(x => x.TypeId, x => x.N);

        var rows = await q.OrderBy(t => t.Code)
            .Select(t => new ListItem(t.ExpenseTypeId, t.Code, t.NameAr, t.NameEn,
                t.IsCustodyAllowed, t.IsOperationCost, t.IsTaxDeductible, t.IsActive, 0))
            .ToListAsync(ct);

        return Ok(rows.Select(r => r with
        {
            ExpenseCount = map.TryGetValue(r.ExpenseTypeId, out var n) ? n : 0
        }).ToList());
    }

    // ═══════════════ GET ═══════════════

    [HttpGet("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var t = await _db.ExpenseTypes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExpenseTypeId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "نوع المصروف غير موجود" });

        return Ok(new Detail(t.ExpenseTypeId, t.Code, t.NameAr, t.NameEn,
            t.IsCustodyAllowed, t.IsOperationCost, t.IsTaxDeductible, t.IsActive));
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Create([FromBody] Upsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var t = new ExpenseType
        {
            Code             = req.Code.Trim().ToUpperInvariant(),
            NameAr           = req.NameAr.Trim(),
            NameEn           = req.NameEn?.Trim(),
            IsCustodyAllowed = req.IsCustodyAllowed,
            IsOperationCost  = req.IsOperationCost,
            IsTaxDeductible  = req.IsTaxDeductible,
            IsActive         = req.IsActive
        };

        _db.ExpenseTypes.Add(t);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = t.ExpenseTypeId, message = $"✅ اتضاف نوع المصروف «{t.NameAr}»" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Update(int id, [FromBody] Upsert req, CancellationToken ct)
    {
        var t = await _db.ExpenseTypes.FirstOrDefaultAsync(x => x.ExpenseTypeId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "نوع المصروف غير موجود" });

        var err = await ValidateAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        var usedBy = await _db.Expenses.CountAsync(e => e.ExpenseTypeId == id && !e.IsDeleted, ct);

        /* 🔴 نوع عليه مصروفات — الكود و«تشغيلي/إداري» مقفولين.
           تغييرهم هيقلب تصنيف مصروفات موجودة ويبوّظ قائمة الدخل. */
        if (usedBy > 0)
        {
            if (!string.Equals(t.Code, req.Code.Trim(), StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = $"الكود مستخدم في {usedBy} مصروف — مايتغيرش. اعمل نوع جديد" });
            if (t.IsOperationCost != req.IsOperationCost)
                return BadRequest(new { message = $"«{t.NameAr}» عليه {usedBy} مصروف — التصنيف (تشغيلي/إداري) مايتغيرش عشان مايبوّظش التقارير" });
        }

        t.Code             = req.Code.Trim().ToUpperInvariant();
        t.NameAr           = req.NameAr.Trim();
        t.NameEn           = req.NameEn?.Trim();
        t.IsCustodyAllowed = req.IsCustodyAllowed;
        t.IsOperationCost  = req.IsOperationCost;
        t.IsTaxDeductible  = req.IsTaxDeductible;
        t.IsActive         = req.IsActive;

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعدّل نوع المصروف «{t.NameAr}»" });
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var t = await _db.ExpenseTypes.FirstOrDefaultAsync(x => x.ExpenseTypeId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "نوع المصروف غير موجود" });

        var usedBy = await _db.Expenses.CountAsync(e => e.ExpenseTypeId == id && !e.IsDeleted, ct);
        if (usedBy > 0)
            return BadRequest(new { message = $"النوع ده عليه {usedBy} مصروف — ماتحذفوش. عطّله بدل الحذف" });

        t.IsDeleted = true;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"✅ اتحذف نوع المصروف «{t.NameAr}»" });
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
            return "الكود بالحروف الإنجليزية والأرقام بس (زي FUEL أو RENT-2026)";

        var dup = await _db.ExpenseTypes.AnyAsync(t =>
            !t.IsDeleted &&
            t.Code.ToLower() == code.ToLower() &&
            (currentId == null || t.ExpenseTypeId != currentId), ct);
        if (dup) return $"الكود «{code.ToUpperInvariant()}» موجود أصلًا";

        return null;
    }
}
