using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Services;

namespace FastCom.Server.Controllers.Finance;

/// <summary>
/// 💸 <b>الذمم الدائنة — الدفعات للموردين</b> (<c>SupplierPayments</c>).
///
/// <para>
/// 🔗 كل دفعة بتتوزّع تلقائيًا على فواتير المورد المفتوحة (الأقدم أولًا)،
/// والتوزيع بيتسجّل في <c>SupplierInvoiceAllocations</c>.
/// </para>
///
/// <para>
/// 🔴 <b>الخزينة:</b> الدفعة بتعمل حركة <c>Payment</c> **بس لو طريقة الدفع نقدية</b>
/// (<c>PaymentMethods.IsCashBased = 1</c>) — نفس قاعدة المصروفات،
/// عشان الشيكات والتحويلات مش كاش في الدرج.
/// </para>
///
/// <para>
/// 🔴 <b>مافيش فلوس من غير فاتورة:</b> لو المبلغ أكبر من إجمالي الفواتير المفتوحة
/// الدفع بيترفض — عشان <c>PaidAmount</c> ما يزيدش عن <c>GrandTotal</c>.
/// </para>
///
/// <para>🔐 الصلاحيات: <c>SUPPLIER.*</c> الموجودة — مافيش كود جديد.</para>
/// </summary>
[ApiController]
[Route("api/supplier-payments")]
[Authorize]
public class SupplierPaymentsController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly IAuditService _audit;
    private readonly INumberingService _num;
    private readonly ICashBook _cash;
    private readonly ILogger<SupplierPaymentsController> _log;

    public SupplierPaymentsController(
        FastComDbContext db, INumberingService num, ICashBook cash,
        ILogger<SupplierPaymentsController> log, IAuditService audit)
    { _db = db; _audit = audit;
        _num  = num;
        _cash = cash;
        _log  = log;
    }

    /* ═══════════════ DTOs ═══════════════ */

    public record ItemDto(long SupplierPaymentId, string SupplierPaymentNumber,
        int SupplierId, string SupplierName, string PaymentDate,
        int PaymentMethodId, string MethodName, decimal Amount,
        string? ChequeNumber, string? BankAccount, string Status, string? Notes,
        int InvoiceCount);

    public record AllocDto(long SupplierInvoiceId, string SupplierInvoiceNumber,
        decimal AllocatedAmount);

    public record DetailDto(long SupplierPaymentId, string SupplierPaymentNumber,
        int SupplierId, string SupplierName, string PaymentDate,
        int PaymentMethodId, string MethodName, decimal Amount,
        string? ChequeNumber, string? ChequeDate, string? BankAccount,
        string Status, string? Notes, List<AllocDto> Allocations);

    public record CreateReq(int SupplierId, int BranchId, string? PaymentDate,
        int PaymentMethodId, decimal Amount, string? ChequeNumber, string? ChequeDate,
        string? BankAccount, string? Notes);

    /* ═══════════════ LIST ═══════════════ */

    [HttpGet]
    [Authorize(Policy = "PERM:SUPPLIER.VIEW")]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] int? supplierId, [FromQuery] string? status,
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        var query = _db.SupplierPayments.AsNoTracking().Where(x => !x.IsDeleted);

        if (supplierId is > 0) query = query.Where(x => x.SupplierId == supplierId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (DateOnly.TryParse(from, out var f)) query = query.Where(x => x.PaymentDate >= f);
        if (DateOnly.TryParse(to,   out var t)) query = query.Where(x => x.PaymentDate <= t);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(x => x.SupplierPaymentNumber.Contains(s)
                                     || (x.ChequeNumber != null && x.ChequeNumber.Contains(s))
                                     || x.Supplier.NameAr.Contains(s));
        }

        var rows = await query
            .OrderByDescending(x => x.SupplierPaymentId)
            .Take(500)
            .Select(x => new
            {
                x.SupplierPaymentId, x.SupplierPaymentNumber, x.SupplierId,
                SupplierName = x.Supplier.NameAr, x.PaymentDate,
                x.PaymentMethodId, MethodName = x.PaymentMethod.NameAr,
                x.Amount, x.ChequeNumber, x.BankAccount, x.Status, x.Notes
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.SupplierPaymentId).ToList();
        var counts = ids.Count == 0
            ? new Dictionary<long, int>()
            : (await _db.SupplierInvoiceAllocations.AsNoTracking()
                    .Where(a => ids.Contains(a.SupplierPaymentId))
                    .GroupBy(a => a.SupplierPaymentId)
                    .Select(g => new { Id = g.Key, N = g.Count() })
                    .ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.N);

        return Ok(rows.Select(r => new ItemDto(
            r.SupplierPaymentId, r.SupplierPaymentNumber, r.SupplierId, r.SupplierName,
            Iso(r.PaymentDate), r.PaymentMethodId, r.MethodName, r.Amount,
            r.ChequeNumber, r.BankAccount, r.Status, r.Notes,
            counts.TryGetValue(r.SupplierPaymentId, out var n) ? n : 0)).ToList());
    }

    /* ═══════════════ DETAIL ═══════════════ */

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:SUPPLIER.VIEW")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct)
    {
        var p = await _db.SupplierPayments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SupplierPaymentId == id && !x.IsDeleted, ct);
        if (p is null) return NotFound(new { message = "الدفعة مش موجودة" });

        var sup    = await _db.Suppliers.AsNoTracking()
            .Where(s => s.SupplierId == p.SupplierId).Select(s => s.NameAr)
            .FirstOrDefaultAsync(ct) ?? "—";
        var method = await _db.PaymentMethods.AsNoTracking()
            .Where(m => m.PaymentMethodId == p.PaymentMethodId).Select(m => m.NameAr)
            .FirstOrDefaultAsync(ct) ?? "—";

        var allocs = await _db.SupplierInvoiceAllocations.AsNoTracking()
            .Where(a => a.SupplierPaymentId == id)
            .Select(a => new
            {
                a.SupplierInvoiceId, a.AllocatedAmount,
                No = a.SupplierInvoice.SupplierInvoiceNumber
            })
            .ToListAsync(ct);

        return Ok(new DetailDto(
            p.SupplierPaymentId, p.SupplierPaymentNumber, p.SupplierId, sup,
            Iso(p.PaymentDate), p.PaymentMethodId, method, p.Amount,
            p.ChequeNumber, Iso(p.ChequeDate), p.BankAccount, p.Status, p.Notes,
            allocs.Select(a => new AllocDto(a.SupplierInvoiceId, a.No, a.AllocatedAmount)).ToList()));
    }

    /* ═══════════════ CREATE ═══════════════ */

    [HttpPost]
    [Authorize(Policy = "PERM:SUPPLIER.CREATE")]
    public async Task<IActionResult> Create([FromBody] CreateReq req, CancellationToken ct)
    {
        /* ── التحقق ── */
        if (req.SupplierId <= 0)      return BadRequest(new { message = "لازم تختار المورد" });
        if (req.BranchId <= 0)        return BadRequest(new { message = "لازم تختار الفرع" });
        if (req.PaymentMethodId <= 0) return BadRequest(new { message = "لازم تختار طريقة الدفع" });
        if (req.Amount <= 0)          return BadRequest(new { message = "المبلغ لازم يكون أكبر من صفر" });

        if (!await _db.Suppliers.AnyAsync(s => s.SupplierId == req.SupplierId && !s.IsDeleted, ct))
            {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Create, "SupplierPayment", null, description: "إنشاء دفعة مورد", ct: ct);
            
            }
        if (!await _db.Branches.AnyAsync(b => b.BranchId == req.BranchId && !b.IsDeleted, ct))
            return BadRequest(new { message = "الفرع مش موجود" });

        var method = await _db.PaymentMethods.AsNoTracking()
            /* 🔴 `PaymentMethod` مافيهوش `IsDeleted` — عنده `IsActive` بس */
            .FirstOrDefaultAsync(m => m.PaymentMethodId == req.PaymentMethodId && m.IsActive, ct);
        if (method is null) return BadRequest(new { message = "طريقة الدفع مش موجودة" });

        /* ── الفواتير المفتوحة (الأقدم أولًا) ── */
        var open = await _db.SupplierInvoices
            .Where(i => i.SupplierId == req.SupplierId && !i.IsDeleted
                        && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.PaymentStatus != "Paid")
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.SupplierInvoiceId)
            .ToListAsync(ct);

        if (open.Count == 0)
            return BadRequest(new { message = "مافيش فواتير مفتوحة على المورد ده — اعتمد فاتورة الأول" });

        var openTotal = open.Sum(i => i.GrandTotal - i.PaidAmount);
        if (req.Amount > openTotal)
            return BadRequest(new
            {
                message = $"المبلغ أكبر من المتاح. المفتوح على المورد: {openTotal:N2}"
            });

        /* ── التوزيع ── */
        var remaining = req.Amount;
        var allocs = new List<SupplierInvoiceAllocation>();

        foreach (var inv in open)
        {
            if (remaining <= 0) break;
            var due = inv.GrandTotal - inv.PaidAmount;
            if (due <= 0) continue;

            var take = Math.Min(due, remaining);
            allocs.Add(new SupplierInvoiceAllocation { SupplierInvoiceId = inv.SupplierInvoiceId, AllocatedAmount = take });
            inv.PaidAmount += take;
            remaining      -= take;
        }

        var number = await _num.NextAsync("SUPPLIER_PAYMENT", ct);
        var pay = new SupplierPayment
        {
            SupplierPaymentNumber = number,
            BranchId              = req.BranchId,
            SupplierId            = req.SupplierId,
            PaymentDate           = ParseDate(req.PaymentDate) ?? DateOnly.FromDateTime(DateTime.Today),
            PaymentMethodId       = req.PaymentMethodId,
            Amount                = req.Amount,
            ChequeNumber          = B(req.ChequeNumber),
            ChequeDate            = ParseDate(req.ChequeDate),
            BankAccount           = B(req.BankAccount),
            Status                = "Posted",
            Notes                 = B(req.Notes),
            IsDeleted             = false,
            CreatedAt             = DateTime.UtcNow,
            CreatedBy             = UserId()
        };

        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.SupplierPayments.Add(pay);
            await _db.SaveChangesAsync(ct);

            foreach (var a in allocs)
            {
                a.SupplierPaymentId = pay.SupplierPaymentId;
                a.CreatedAt         = DateTime.UtcNow;
                a.CreatedBy         = UserId();
                _db.SupplierInvoiceAllocations.Add(a);
            }

            /* 🔴 إعادة حساب حالة كل فاتورة اتأثرت */
            foreach (var inv in open) RecalcStatus(inv);

            await _db.SaveChangesAsync(ct);
            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception ex)
        {
            await _db.Database.RollbackTransactionAsync(ct);
            _log.LogError(ex, "فشل تسجيل دفعة مورد");
            return BadRequest(new { message = "فشل الحفظ: " + ex.Message });
        }

        /* 🔴 الخزينة — **بعد الـ Commit** عشان ما تترجعش مع rollback */
        if (method.IsCashBased)
        {
            var ok = await _cash.PaymentAsync(new CashEntry(
                ReferenceNumber : $"SPAY:{number}",
                Amount          : pay.Amount,
                PaymentMethodId : pay.PaymentMethodId,
                SupplierId      : pay.SupplierId,
                Description     : $"دفعة للمورد {number}",
                Date            : pay.PaymentDate), ct);
            if (ok) await _db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            message = $"✅ اتسجلت الدفعة {number} على {allocs.Count} فاتورة",
            id      = pay.SupplierPaymentId
        });
    }

    /* ═══════════════ VOID ═══════════════ */

    [HttpPost("{id:long}/void")]
    [Authorize(Policy = "PERM:SUPPLIER.EDIT")]
    public async Task<IActionResult> Void(long id, CancellationToken ct)
    {
        var p = await _db.SupplierPayments
            .FirstOrDefaultAsync(x => x.SupplierPaymentId == id && !x.IsDeleted, ct);
        if (p is null) return NotFound(new { message = "الدفعة مش موجودة" });

        if (p.Status != "Posted")
            {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Cancel, "SupplierPayment", null, description: "إلغاء دفعة مورد", ct: ct);
            
            }

        var allocs = await _db.SupplierInvoiceAllocations
            .Where(a => a.SupplierPaymentId == id).ToListAsync(ct);

        var invIds = allocs.Select(a => a.SupplierInvoiceId).Distinct().ToList();
        var invs = invIds.Count == 0
            ? new List<SupplierInvoice>()
            : await _db.SupplierInvoices
                .Where(i => invIds.Contains(i.SupplierInvoiceId)).ToListAsync(ct);

        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            /* 🔴 نرجّع المبلغ للفواتير */
            foreach (var a in allocs)
            {
                var inv = invs.FirstOrDefault(i => i.SupplierInvoiceId == a.SupplierInvoiceId);
                if (inv is null) continue;
                inv.PaidAmount = Math.Max(0m, inv.PaidAmount - a.AllocatedAmount);
                RecalcStatus(inv);
            }

            _db.SupplierInvoiceAllocations.RemoveRange(allocs);

            p.Status    = "Cancelled";
            p.UpdatedAt = DateTime.UtcNow;
            p.UpdatedBy = UserId();

            await _db.SaveChangesAsync(ct);
            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception ex)
        {
            await _db.Database.RollbackTransactionAsync(ct);
            _log.LogError(ex, "فشل إبطال دفعة مورد {Id}", id);
            return BadRequest(new { message = "فشل الإبطال: " + ex.Message });
        }

        /* 🔴 إلغاء حركة الخزينة — بعد الـ Commit */
        await _cash.VoidByReferenceAsync($"SPAY:{p.SupplierPaymentNumber}", ct);

        return Ok(new { message = "✅ اتبطلت الدفعة" });
    }

    /* ═══════════════ helpers ═══════════════ */

    /// <summary>
    /// 🔴 يعيد حساب <c>Status</c> و<c>PaymentStatus</c> من <c>PaidAmount</c> مقابل <c>GrandTotal</c>.
    /// الـ CHECK بيسمح بـ: Status ∈ Draft|Approved|PartiallyPaid|Paid|Cancelled
    ///                      PaymentStatus ∈ Unpaid|PartiallyPaid|Paid
    /// </summary>
    private static void RecalcStatus(SupplierInvoice inv)
    {
        if (inv.Status is "Draft" or "Cancelled") return;

        if (inv.PaidAmount <= 0m)
        {
            inv.PaymentStatus = "Unpaid";
            inv.Status        = "Approved";
        }
        else if (inv.PaidAmount >= inv.GrandTotal)
        {
            inv.PaymentStatus = "Paid";
            inv.Status        = "Paid";
        }
        else
        {
            inv.PaymentStatus = "PartiallyPaid";
            inv.Status        = "PartiallyPaid";
        }
    }

    private static string? B(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : null;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Iso(DateOnly? d) =>
        d is null ? null : d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Ar(string s) => s switch
    {
        "Posted"    => "مسجلة",
        "Cancelled" => "مبطلة",
        "Returned"  => "مرتجعة",
        _           => s
    };

    private int? UserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(v, out var id) ? id : null;
    }
}
