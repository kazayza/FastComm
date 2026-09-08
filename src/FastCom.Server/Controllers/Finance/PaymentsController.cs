using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Finance;

/// <summary>
/// تحصيل العملاء — دفعة واحدة ممكن تتوزّع على كذا فاتورة.
/// <para>🔴 <c>Invoices.PaidAmount / PaymentStatus</c> بيحسبهم <c>trg_PaymentAllocations_InvoiceSync</c>
/// من <c>PaymentAllocations</c> — مش من الكود.</para>
/// <para>🔴 <c>CK_Payments_Amount</c> بيشترط <c>Amount &gt; 0</c> — فالإلغاء بيحوّل الحالة
/// لـ <c>Cancelled</c> ويشيل التوزيع، مش بيكتب دفعة سالبة.</para>
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly INumberingService _numbers;

    public PaymentsController(FastComDbContext db, INumberingService numbers)
    { _db = db; _numbers = numbers; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateOnly D(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateOnly? Dn(string? s) => DateOnly.TryParse(s, out var d) ? d : null;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static string? IsoN(DateOnly? d) => d?.ToString("yyyy-MM-dd");

    private async Task<int?> ResolveBranchIdAsync(CancellationToken ct)
    {
        if (int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0) return b;
        return await _db.Branches.AsNoTracking()
            .OrderBy(x => x.BranchId).Select(x => (int?)x.BranchId).FirstOrDefaultAsync(ct);
    }

    // ═══════════════ DTOs ═══════════════

    public record AllocIn(long InvoiceId, decimal Amount);

    public record PaymentUpsert(int CustomerId, string? PaymentDate, int PaymentMethodId,
        decimal Amount, string? ChequeNumber, string? ChequeDate, string? BankAccount,
        string? Notes, List<AllocIn>? Allocations);

    public record PaymentPatch(string? ChequeNumber, string? ChequeDate,
        string? BankAccount, string? Notes);

    public record ListItem(long PaymentId, string PaymentNumber, DateOnly PaymentDate,
        string CustomerName, string MethodName, decimal Amount, decimal Allocated,
        decimal Unallocated, string Status, string? ChequeNumber, int AllocationsCount);

    public record Detail(long PaymentId, string PaymentNumber, string? PaymentDate,
        int CustomerId, string CustomerName, int PaymentMethodId, string MethodName,
        decimal Amount, string? ChequeNumber, string? ChequeDate, string? BankAccount,
        string? Notes, string Status, decimal Allocated, decimal Unallocated);

    public record AllocOut(long PaymentAllocationId, long InvoiceId, string InvoiceNumber,
        decimal AllocatedAmount, decimal InvoiceTotal, decimal InvoicePaid);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:PAYMENT.VIEW")]
    public async Task<IActionResult> List(string? status, int? customerId, string? q,
        int take = 300, CancellationToken ct = default)
    {
        if (take <= 0 || take > 1000) take = 300;

        var query = _db.Payments.AsNoTracking().Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status);
        if (customerId is not null)             query = query.Where(p => p.CustomerId == customerId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p => p.PaymentNumber.Contains(s) ||
                                     p.Customer.NameAr.Contains(s) ||
                                     (p.ChequeNumber != null && p.ChequeNumber.Contains(s)));
        }

        var rows = await query
            .OrderByDescending(p => p.PaymentId)
            .Take(take)
            .Select(p => new ListItem(
                p.PaymentId, p.PaymentNumber, p.PaymentDate, p.Customer.NameAr,
                p.PaymentMethod.NameAr, p.Amount,
                _db.PaymentAllocations.Where(a => a.PaymentId == p.PaymentId)
                                      .Sum(a => (decimal?)a.AllocatedAmount) ?? 0,
                p.Amount - (_db.PaymentAllocations.Where(a => a.PaymentId == p.PaymentId)
                                      .Sum(a => (decimal?)a.AllocatedAmount) ?? 0),
                p.Status, p.ChequeNumber,
                _db.PaymentAllocations.Count(a => a.PaymentId == p.PaymentId)))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ONE ═══════════════

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:PAYMENT.VIEW")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var p = await _db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PaymentId == id && !x.IsDeleted, ct);
        if (p is null) return NotFound(new { message = "الدفعة مش موجودة" });

        var allocated = await _db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == id)
            .SumAsync(a => (decimal?)a.AllocatedAmount, ct) ?? 0;

        var allocs = await _db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == id)
            .Select(a => new AllocOut(a.PaymentAllocationId, a.InvoiceId, a.Invoice.InvoiceNumber,
                a.AllocatedAmount, a.Invoice.GrandTotal, a.Invoice.PaidAmount))
            .ToListAsync(ct);

        var cust = await _db.Customers.AsNoTracking()
            .Where(c => c.CustomerId == p.CustomerId)
            .Select(c => c.NameAr).FirstOrDefaultAsync(ct);

        var method = await _db.PaymentMethods.AsNoTracking()
            .Where(m => m.PaymentMethodId == p.PaymentMethodId)
            .Select(m => m.NameAr).FirstOrDefaultAsync(ct);

        var detail = new Detail(p.PaymentId, p.PaymentNumber, Iso(p.PaymentDate),
            p.CustomerId, cust ?? "—", p.PaymentMethodId, method ?? "—", p.Amount,
            p.ChequeNumber, IsoN(p.ChequeDate), p.BankAccount, p.Notes, p.Status,
            allocated, p.Amount - allocated);

        return Ok(new { Payment = detail, Allocations = allocs });
    }

    /// <summary>عملاء التحصيل — بصلاحية المدفوعات.</summary>
    [HttpGet("customers")]
    [Authorize(Policy = "PERM:PAYMENT.VIEW")]
    public async Task<IActionResult> Customers(CancellationToken ct) =>
        Ok(await _db.Customers.AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.NameAr)
            .Select(c => new CustOpt(c.CustomerId, c.NameAr, c.CustomerCode))
            .ToListAsync(ct));

    public record CustOpt(int Id, string Label, string Code);

    /// <summary>فواتير العميل المفتوحة — اللي الدفعة هتتوزّع عليها.</summary>
    [HttpGet("open-invoices")]
    [Authorize(Policy = "PERM:PAYMENT.VIEW")]
    public async Task<IActionResult> OpenInvoices(int customerId, CancellationToken ct)
    {
        if (customerId <= 0) return BadRequest(new { message = "اختار العميل" });

        var rows = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.CustomerId == customerId &&
                        i.Status != "Cancelled" && i.Status != "Draft" &&
                        i.PaymentStatus != "Paid")
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceId)
            .Select(i => new OpenInv(i.InvoiceId, i.InvoiceNumber, i.InvoiceDate, i.GrandTotal,
                                     i.PaidAmount, i.GrandTotal - i.PaidAmount))
            .ToListAsync(ct);

        return Ok(rows.Where(r => r.Due != 0));
    }

    public record OpenInv(long InvoiceId, string InvoiceNumber, DateOnly InvoiceDate,
                          decimal GrandTotal, decimal PaidAmount, decimal Due);

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:PAYMENT.CREATE")]
    public async Task<IActionResult> Create([FromBody] PaymentUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var p = new Payment
        {
            PaymentNumber   = await _numbers.NextAsync("PAYMENT", ct),
            BranchId        = branchId.Value,
            CustomerId      = req.CustomerId,
            PaymentDate     = D(req.PaymentDate),
            PaymentMethodId = req.PaymentMethodId,
            Amount          = req.Amount,
            ChequeNumber    = B(req.ChequeNumber),
            ChequeDate      = Dn(req.ChequeDate),
            BankAccount     = B(req.BankAccount),
            Status          = "Posted",
            Notes           = B(req.Notes),
            CreatedBy       = CurrentUserId()
        };
        _db.Payments.Add(p);
        await _db.SaveChangesAsync(ct);

        var allocated = 0m;
        foreach (var a in req.Allocations!)
        {
            _db.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId       = p.PaymentId,
                InvoiceId       = a.InvoiceId,
                AllocatedAmount = a.Amount,
                CreatedAt       = DateTime.UtcNow,
                CreatedBy       = CurrentUserId()
            });
            allocated += a.Amount;
        }
        await _db.SaveChangesAsync(ct);

        var left = req.Amount - allocated;
        return Ok(new
        {
            id = p.PaymentId,
            number = p.PaymentNumber,
            message = left > 0
                ? $"✅ اتسجّلت الدفعة {p.PaymentNumber} — فاضل {left:N2} مش متوزّع على فواتير"
                : $"✅ اتسجّلت الدفعة {p.PaymentNumber}"
        });
    }

    // ═══════════════ UPDATE — بيانات الدفعة بس ═══════════════

    [HttpPut("{id:long}")]
    [Authorize(Policy = "PERM:PAYMENT.EDIT")]
    public async Task<IActionResult> Update(long id, [FromBody] PaymentPatch req, CancellationToken ct)
    {
        var p = await _db.Payments.FirstOrDefaultAsync(x => x.PaymentId == id && !x.IsDeleted, ct);
        if (p is null) return NotFound(new { message = "الدفعة مش موجودة" });
        if (p.Status != "Posted") return BadRequest(new { message = "الدفعة ملغية — مافيش تعديل" });

        var method = await _db.PaymentMethods.AsNoTracking()
            .FirstAsync(m => m.PaymentMethodId == p.PaymentMethodId, ct);

        p.ChequeNumber = B(req.ChequeNumber);
        p.ChequeDate   = Dn(req.ChequeDate);
        p.BankAccount  = B(req.BankAccount);
        p.Notes        = B(req.Notes);

        // CK_Payments_ChequeNeedsNo
        if (p.ChequeDate is not null && p.ChequeNumber is null)
            return BadRequest(new { message = "الشيك لازم يكون له رقم" });
        if (method.RequiresChequeNo && p.ChequeNumber is null)
            return BadRequest(new { message = $"طريقة «{method.NameAr}» محتاجة رقم شيك" });

        p.UpdatedAt = DateTime.UtcNow;
        p.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اتعدّلت الدفعة" });
    }

    // ═══════════════ إلغاء دفعة ═══════════════

    [HttpPost("{id:long}/void")]
    [Authorize(Policy = "PERM:PAYMENT.VOID")]
    public async Task<IActionResult> Void(long id, CancellationToken ct)
    {
        var p = await _db.Payments.FirstOrDefaultAsync(x => x.PaymentId == id && !x.IsDeleted, ct);
        if (p is null) return NotFound(new { message = "الدفعة مش موجودة" });
        if (p.Status == "Cancelled") return BadRequest(new { message = "الدفعة ملغية أصلًا" });

        // لازم التوزيع يتشال — عشان الترِجر يعيد حساب المديونية على الفواتير
        var allocs = await _db.PaymentAllocations
            .Where(a => a.PaymentId == id).ToListAsync(ct);
        _db.PaymentAllocations.RemoveRange(allocs);

        p.Status    = "Cancelled";
        p.UpdatedAt = DateTime.UtcNow;
        p.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"❌ اتلغت الدفعة {p.PaymentNumber} ورجعت المديونية على الفواتير" });
    }

    // ═══════════════ Helpers ═══════════════

    private async Task<string?> ValidateAsync(PaymentUpsert? r, CancellationToken ct)
    {
        if (r is null) return "البيانات مش كاملة";
        if (r.CustomerId <= 0) return "اختار العميل";
        if (r.Amount <= 0) return "المبلغ لازم يكون أكتر من صفر";
        if (r.Amount > 100_000_000m) return "المبلغ كبير بشكل غير منطقي";

        var cust = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == r.CustomerId && !c.IsDeleted, ct);
        if (cust is null) return "العميل مش موجود";

        var method = await _db.PaymentMethods.AsNoTracking()
            .FirstOrDefaultAsync(m => m.PaymentMethodId == r.PaymentMethodId && m.IsActive, ct);
        if (method is null) return "طريقة الدفع مش موجودة";

        // CK_Payments_ChequeNeedsNo
        if (r.ChequeDate is not null && string.IsNullOrWhiteSpace(r.ChequeNumber))
            return "الشيك لازم يكون له رقم";
        if (method.RequiresChequeNo && string.IsNullOrWhiteSpace(r.ChequeNumber))
            return $"طريقة «{method.NameAr}» محتاجة رقم شيك";

        if (r.Allocations is { Count: > 0 })
        {
            var total = r.Allocations.Sum(a => a.Amount);
            if (total > r.Amount)
                return $"الموزّع {total:N2} أكتر من مبلغ الدفعة {r.Amount:N2}";

            foreach (var a in r.Allocations)
            {
                if (a.Amount <= 0) return "مبلغ التوزيع لازم يكون أكتر من صفر";

                var inv = await _db.Invoices.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.InvoiceId == a.InvoiceId && !i.IsDeleted, ct);
                if (inv is null) return "في فاتورة مش موجودة";
                if (inv.CustomerId != r.CustomerId)
                    return $"الفاتورة {inv.InvoiceNumber} لعميل تاني";
                if (inv.Status == "Cancelled")
                    return $"الفاتورة {inv.InvoiceNumber} ملغية";
                if (inv.Status == "Draft")
                    return $"الفاتورة {inv.InvoiceNumber} لسه مسودة — صدّرها الأول";

                var paid = await _db.PaymentAllocations.AsNoTracking()
                    .Where(x => x.InvoiceId == inv.InvoiceId && x.Payment.Status == "Posted" &&
                                !x.Payment.IsDeleted)
                    .SumAsync(x => (decimal?)x.AllocatedAmount, ct) ?? 0;

                if (paid + a.Amount > inv.GrandTotal)
                    return $"{inv.InvoiceNumber}: المتبقي {inv.GrandTotal - paid:N2} بس";
            }

            var dup = r.Allocations.GroupBy(a => a.InvoiceId).Any(g => g.Count() > 1);
            if (dup) return "في فاتورة مكررة في التوزيع";
        }

        return null;
    }
}
