using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// طرق الدفع — كانت للقراءة بس من <c>api/options/payment-methods</c>.
/// </summary>
/// <remarks>
/// <para>🔴 <b>مافيش عمود `IsDeleted` في الجدول</b> — فالحذف معناه
/// <b>تعطيل</b> (`IsActive = 0`). الجدول مستخدم بـ FK من
/// <c>Payments</c> · <c>Expenses</c> · <c>SupplierPayments</c> · <c>CashTransactions</c>،
/// فالحذف الحقيقي هيوقع قيود.</para>
///
/// <para>🔴 <b>`IsCashBased` هو اللي بيقرر هل الحركة تدخل الخزينة ولا لأ.</b>
/// لو غيّرته لطريقة مستخدمة، الحركات القديمة هتفضل في الخزينة والجديدة لأ —
/// فأرقام الدرج مش هتطابق. لذلك <b>مقفل</b> لأي طريقة عليها حركات.</para>
/// </remarks>
[ApiController]
[Route("api/master/payment-methods")]
[Authorize]
public class PaymentMethodsController : ControllerBase
{
    private readonly FastComDbContext _db;
    public PaymentMethodsController(FastComDbContext db) { _db = db; }

    // ═══════════════ DTOs ═══════════════

    public record ListItem(int PaymentMethodId, string Code, string NameAr, string? NameEn,
        bool RequiresChequeNo, bool IsCashBased, int SortOrder, bool IsActive, int UsageCount);

    public record Upsert(string Code, string NameAr, string? NameEn,
        bool RequiresChequeNo, bool IsCashBased, int SortOrder, bool IsActive);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> List([FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var q = _db.PaymentMethods.AsNoTracking().AsQueryable();
        if (activeOnly == true) q = q.Where(m => m.IsActive);

        /* 🔴 عدد الاستخدامات — عشان المستخدم يعرف الطريقة مستخدمة ولا لأ
           قبل ما يفكّر يغيّر `IsCashBased`. */
        var ids = await q.Select(m => m.PaymentMethodId).ToListAsync(ct);

        var pay = await _db.Payments.AsNoTracking().Where(p => !p.IsDeleted)
            .GroupBy(p => p.PaymentMethodId).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var exp = await _db.Expenses.AsNoTracking().Where(e => !e.IsDeleted)
            .Where(e => e.PaymentMethodId != null)
            .GroupBy(e => e.PaymentMethodId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var sup = await _db.SupplierPayments.AsNoTracking().Where(s => !s.IsDeleted)
            .GroupBy(s => s.PaymentMethodId).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);

        var map = new Dictionary<int, int>();
        foreach (var g in pay.Concat(exp).Concat(sup))
            map[g.Id] = map.TryGetValue(g.Id, out var n) ? n + g.N : g.N;

        var rows = await q.OrderBy(m => m.SortOrder).ThenBy(m => m.Code)
            .Select(m => new ListItem(m.PaymentMethodId, m.Code, m.NameAr, m.NameEn,
                m.RequiresChequeNo, m.IsCashBased, m.SortOrder, m.IsActive, 0))
            .ToListAsync(ct);

        return Ok(rows.Select(r => r with
        {
            UsageCount = map.TryGetValue(r.PaymentMethodId, out var n) ? n : 0
        }).ToList());
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Create([FromBody] Upsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var m = new PaymentMethod
        {
            Code             = req.Code.Trim().ToUpperInvariant(),
            NameAr           = req.NameAr.Trim(),
            NameEn           = req.NameEn?.Trim(),
            RequiresChequeNo = req.RequiresChequeNo,
            IsCashBased      = req.IsCashBased,
            SortOrder        = req.SortOrder,
            IsActive         = req.IsActive
        };

        _db.PaymentMethods.Add(m);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = m.PaymentMethodId, message = $"✅ اتضافت طريقة الدفع «{m.NameAr}»" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Update(int id, [FromBody] Upsert req, CancellationToken ct)
    {
        var m = await _db.PaymentMethods.FirstOrDefaultAsync(x => x.PaymentMethodId == id, ct);
        if (m is null) return NotFound(new { message = "طريقة الدفع غير موجودة" });

        var err = await ValidateAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        var used = await UsageAsync(id, ct);

        /* 🔴 طريقة عليها حركات — الكود و`IsCashBased` مقفولين.
           تغيير `IsCashBased` هيخلي الحركات القديمة في الخزينة والجديدة براها،
           فرصيد الدرج مش هيطابق. */
        if (used > 0)
        {
            if (!string.Equals(m.Code, req.Code.Trim(), StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = $"الكود مستخدم في {used} حركة — مايتغيرش. اعمل طريقة جديدة" });
            if (m.IsCashBased != req.IsCashBased)
                return BadRequest(new
                {
                    message = $"«{m.NameAr}» عليها {used} حركة — تصنيف «تدخل الخزينة» مايتغيرش، " +
                              "وإلا رصيد الدرج مش هيطابق الحركات القديمة. اعمل طريقة جديدة بدلها."
                });
        }

        m.Code             = req.Code.Trim().ToUpperInvariant();
        m.NameAr           = req.NameAr.Trim();
        m.NameEn           = req.NameEn?.Trim();
        m.RequiresChequeNo = req.RequiresChequeNo;
        m.IsCashBased      = req.IsCashBased;
        m.SortOrder        = req.SortOrder;
        m.IsActive         = req.IsActive;

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعدّلت طريقة الدفع «{m.NameAr}»" });
    }

    // ═══════════════ تعطيل (مافيش IsDeleted في الجدول) ═══════════════

    [HttpPost("{id:int}/deactivate")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var m = await _db.PaymentMethods.FirstOrDefaultAsync(x => x.PaymentMethodId == id, ct);
        if (m is null) return NotFound(new { message = "طريقة الدفع غير موجودة" });
        if (!m.IsActive) return BadRequest(new { message = "الطريقة معطّلة بالفعل" });

        m.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعطّلت طريقة الدفع «{m.NameAr}» — الحركات القديمة زي ما هي" });
    }

    [HttpPost("{id:int}/activate")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Activate(int id, CancellationToken ct)
    {
        var m = await _db.PaymentMethods.FirstOrDefaultAsync(x => x.PaymentMethodId == id, ct);
        if (m is null) return NotFound(new { message = "طريقة الدفع غير موجودة" });
        if (m.IsActive) return BadRequest(new { message = "الطريقة نشطة بالفعل" });

        m.IsActive = true;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتفعّلت طريقة الدفع «{m.NameAr}»" });
    }

    // ═══════════════ helpers ═══════════════

    private async Task<int> UsageAsync(int id, CancellationToken ct)
    {
        var a = await _db.Payments.CountAsync(p => p.PaymentMethodId == id && !p.IsDeleted, ct);
        var b = await _db.Expenses.CountAsync(e => e.PaymentMethodId == id && !e.IsDeleted, ct);
        var c = await _db.SupplierPayments.CountAsync(s => s.PaymentMethodId == id && !s.IsDeleted, ct);
        return a + b + c;
    }

    private async Task<string?> ValidateAsync(Upsert? r, CancellationToken ct, int? currentId = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (string.IsNullOrWhiteSpace(r.Code))   return "الكود مطلوب";
        if (string.IsNullOrWhiteSpace(r.NameAr)) return "الاسم بالعربي مطلوب";
        if (r.Code.Trim().Length > 30)   return "الكود أطول من 30 حرف";
        if (r.NameAr.Trim().Length > 100) return "الاسم أطول من 100 حرف";
        if (!System.Text.RegularExpressions.Regex.IsMatch(r.Code.Trim(), "^[A-Za-z0-9_-]+$"))
            return "الكود بالحروف الإنجليزية والأرقام بس (زي CASH أو VODAFONE-CASH)";

        var dup = await _db.PaymentMethods.AnyAsync(m =>
            m.Code.ToLower() == r.Code.Trim().ToLower() &&
            (currentId == null || m.PaymentMethodId != currentId), ct);
        if (dup) return $"الكود «{r.Code.Trim().ToUpperInvariant()}» موجود أصلًا";

        return null;
    }
}
