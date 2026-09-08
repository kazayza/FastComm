using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Auth;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Finance;

/// <summary>
/// الخزينة — الصناديق النقدية وحركاتها.
/// <para>🔴 <c>CashBoxes.CurrentBalance</c> بيحسبه <c>trg_CashTransactions_BalanceSync</c>
/// من الحركات <c>Posted</c> وغير المحذوفة — ماتكتبهوش من الكود.</para>
/// <para>التحويل بين خزينتين = حركتين (<c>TransferOut</c> + <c>TransferIn</c>) مربوطتين بـ
/// <c>TransferGroupId</c> — والإلغاء بيلغيهم مع بعض.</para>
/// </summary>
[ApiController]
[Route("api/treasury")]
[Authorize]
public class TreasuryController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly IPermissionService _perms;

    public TreasuryController(FastComDbContext db, IPermissionService perms)
    { _db = db; _perms = perms; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static DateTime Dt(string? s) => DateTime.TryParse(s, out var d) ? d : DateTime.UtcNow;
    private static DateOnly? Dn(string? s) => DateOnly.TryParse(s, out var d) ? d : null;

    private async Task<int?> ResolveBranchIdAsync(CancellationToken ct)
    {
        if (int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0) return b;
        return await _db.Branches.AsNoTracking()
            .OrderBy(x => x.BranchId).Select(x => (int?)x.BranchId).FirstOrDefaultAsync(ct);
    }

    // ═══════════════ DTOs ═══════════════

    public record TxUpsert(string? Type, int CashBoxId, string? TxDate, decimal Amount,
        int? PaymentMethodId, int? CustomerId, int? SupplierId, long? InvoiceId,
        long? CustodyId, string? ChequeNumber, string? ChequeDate, string? BankAccount,
        string? ReferenceNumber, string? Description);

    public record TransferRequest(int FromCashBoxId, int ToCashBoxId, string? TxDate,
        decimal Amount, string? Description);

    public record BoxOut(int CashBoxId, string Code, string NameAr, decimal OpeningBalance,
        decimal CurrentBalance, string CurrencyCode, int TxCount);

    public record TxOut(long CashTransactionId, DateTime TransactionDate, string TransactionType,
        int CashBoxId, string CashBoxName, decimal Amount, string? MethodName,
        string? CustomerName, string? SupplierName, string? InvoiceNumber, string? CustodyNumber,
        string? CounterpartName, string? ChequeNumber, string? ReferenceNumber,
        string? Description, string Status);

    public record BoxOpt(int Id, string Label, string Code, decimal CurrentBalance);

    // ── كشف حساب العميل ──

    public record StmtLine(DateTime Date, string Kind, string Ref, string? Description,
        decimal Debit, decimal Credit, decimal Balance);

    public record StatementOut(string CustomerName, decimal Opening, List<StmtLine> Lines,
        decimal TotalDebit, decimal TotalCredit, decimal Closing);

    // ═══════════════ الصناديق ═══════════════

    [HttpGet("boxes")]
    [Authorize(Policy = "PERM:TREASURY.VIEW")]
    public async Task<IActionResult> Boxes(CancellationToken ct)
    {
        var boxes = await _db.CashBoxes.AsNoTracking()
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.CashBoxId)
            .Select(b => new BoxOut(b.CashBoxId, b.Code, b.NameAr, b.OpeningBalance,
                b.CurrentBalance, b.CurrencyCode,
                _db.CashTransactions.Count(t => t.CashBoxId == b.CashBoxId &&
                                                t.Status == "Posted" && !t.IsDeleted)))
            .ToListAsync(ct);

        return Ok(boxes);
    }

    /// <summary>عملاء — لدروبداون القبض.</summary>
    [HttpGet("customers")]
    [Authorize(Policy = "PERM:TREASURY.VIEW")]
    public async Task<IActionResult> Customers(CancellationToken ct) =>
        Ok(await _db.Customers.AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.NameAr)
            .Select(c => new CustOpt(c.CustomerId, c.NameAr, c.CustomerCode))
            .ToListAsync(ct));

    public record CustOpt(int Id, string Label, string Code);

    /// <summary>الصناديق النشطة — للدروبداون.</summary>
    [HttpGet("box-options")]
    [Authorize(Policy = "PERM:TREASURY.VIEW")]
    public async Task<IActionResult> BoxOptions(CancellationToken ct) =>
        Ok(await _db.CashBoxes.AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderBy(b => b.CashBoxId)
            .Select(b => new BoxOpt(b.CashBoxId, b.NameAr, b.Code, b.CurrentBalance))
            .ToListAsync(ct));

    // ═══════════════ دفتر الحركات ═══════════════

    [HttpGet("transactions")]
    [Authorize(Policy = "PERM:TREASURY.VIEW")]
    public async Task<IActionResult> Transactions(int? cashBoxId, string? type, string? status,
        DateTime? from, DateTime? to, string? q, int take = 300, CancellationToken ct = default)
    {
        if (take <= 0 || take > 1000) take = 300;

        var query = _db.CashTransactions.AsNoTracking().Where(t => !t.IsDeleted);

        if (cashBoxId is not null) query = query.Where(t => t.CashBoxId == cashBoxId);
        if (!string.IsNullOrWhiteSpace(type))   query = query.Where(t => t.TransactionType == type);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(t => t.Status == status);
        if (from is not null) query = query.Where(t => t.TransactionDate >= from);
        if (to   is not null) query = query.Where(t => t.TransactionDate < to.Value.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(t =>
                (t.Description != null && t.Description.Contains(s)) ||
                (t.ReferenceNumber != null && t.ReferenceNumber.Contains(s)) ||
                (t.ChequeNumber != null && t.ChequeNumber.Contains(s)));
        }

        var rows = await query
            .OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.CashTransactionId)
            .Take(take)
            .Select(t => new TxOut(
                t.CashTransactionId, t.TransactionDate, t.TransactionType,
                t.CashBoxId, t.CashBox.NameAr, t.Amount,
                t.PaymentMethod != null ? t.PaymentMethod.NameAr : null,
                t.Customer != null ? t.Customer.NameAr : null,
                t.Supplier != null ? t.Supplier.NameAr : null,
                t.Invoice != null ? t.Invoice.InvoiceNumber : null,
                t.Custody != null ? t.Custody.CustodyNumber : null,
                t.CounterpartCashBox != null ? t.CounterpartCashBox.NameAr : null,
                t.ChequeNumber, t.ReferenceNumber, t.Description, t.Status))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ تسجيل حركة ═══════════════

    [HttpPost("transactions")]
    [Authorize(Policy = "PERM:TREASURY.POST")]
    public async Task<IActionResult> Post([FromBody] TxUpsert req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { message = "البيانات مش كاملة" });
        if (req.Type is not ("Receipt" or "Payment"))
            return BadRequest(new { message = "نوع الحركة لازم يكون «قبض» أو «صرف»" });
        if (req.Amount <= 0) return BadRequest(new { message = "المبلغ لازم يكون أكتر من صفر" });
        if (req.Amount > 100_000_000m) return BadRequest(new { message = "المبلغ كبير بشكل غير منطقي" });

        var box = await _db.CashBoxes.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CashBoxId == req.CashBoxId && !b.IsDeleted, ct);
        if (box is null) return BadRequest(new { message = "الخزينة مش موجودة" });
        if (!box.IsActive) return BadRequest(new { message = "الخزينة دي متوقفة" });

        if (req.Type == "Receipt" && req.CustomerId is null)
            return BadRequest(new { message = "القبض لازم يكون من عميل" });

        if (req.PaymentMethodId is not null &&
            !await _db.PaymentMethods.AnyAsync(m => m.PaymentMethodId == req.PaymentMethodId && m.IsActive, ct))
            return BadRequest(new { message = "طريقة الدفع مش موجودة" });

        if (req.CustomerId is not null &&
            !await _db.Customers.AnyAsync(c => c.CustomerId == req.CustomerId && !c.IsDeleted, ct))
            return BadRequest(new { message = "العميل مش موجود" });

        if (req.SupplierId is not null &&
            !await _db.Suppliers.AnyAsync(s => s.SupplierId == req.SupplierId && !s.IsDeleted, ct))
            return BadRequest(new { message = "المورد مش موجود" });

        if (req.InvoiceId is not null)
        {
            var inv = await _db.Invoices.AsNoTracking()
                .FirstOrDefaultAsync(i => i.InvoiceId == req.InvoiceId && !i.IsDeleted, ct);
            if (inv is null) return BadRequest(new { message = "الفاتورة مش موجودة" });
            if (req.CustomerId is not null && inv.CustomerId != req.CustomerId)
                return BadRequest(new { message = "الفاتورة دي لعميل تاني" });
        }

        if (req.CustodyId is not null &&
            !await _db.DriverCustodies.AnyAsync(c => c.CustodyId == req.CustodyId && !c.IsDeleted, ct))
            return BadRequest(new { message = "العهدة مش موجودة" });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var t = new CashTransaction
        {
            BranchId        = branchId.Value,
            CashBoxId       = req.CashBoxId,
            TransactionDate = Dt(req.TxDate),
            TransactionType = req.Type,
            Amount          = req.Amount,
            PaymentMethodId = req.PaymentMethodId,
            CustomerId      = req.CustomerId,
            SupplierId      = req.SupplierId,
            InvoiceId       = req.InvoiceId,
            CustodyId       = req.CustodyId,
            ChequeNumber    = B(req.ChequeNumber),
            ChequeDate      = Dn(req.ChequeDate),
            BankAccount     = B(req.BankAccount),
            ReferenceNumber = B(req.ReferenceNumber),
            Description     = B(req.Description),
            Status          = "Posted",
            CreatedBy       = CurrentUserId()
        };
        _db.CashTransactions.Add(t);
        await _db.SaveChangesAsync(ct);

        var now = await _db.CashBoxes.AsNoTracking()
            .Where(b => b.CashBoxId == req.CashBoxId)
            .Select(b => b.CurrentBalance).FirstAsync(ct);

        return Ok(new
        {
            id = t.CashTransactionId,
            balance = now,
            message = req.Type == "Receipt"
                ? $"✅ اتسجّل القبض — رصيد {box.NameAr} بقى {now:N2}"
                : $"✅ اتسجّل الصرف — رصيد {box.NameAr} بقى {now:N2}"
        });
    }

    // ═══════════════ تحويل بين خزينتين ═══════════════

    [HttpPost("transfer")]
    [Authorize(Policy = "PERM:TREASURY.TRANSFER")]
    public async Task<IActionResult> Transfer([FromBody] TransferRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { message = "البيانات مش كاملة" });
        if (req.Amount <= 0) return BadRequest(new { message = "المبلغ لازم يكون أكتر من صفر" });
        if (req.FromCashBoxId == req.ToCashBoxId)
            return BadRequest(new { message = "اختار خزينة تانية للتحويل" });

        var from = await _db.CashBoxes.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CashBoxId == req.FromCashBoxId && !b.IsDeleted, ct);
        if (from is null) return BadRequest(new { message = "الخزينة المصروفة مش موجودة" });
        if (!from.IsActive) return BadRequest(new { message = $"«{from.NameAr}» متوقفة" });

        var to = await _db.CashBoxes.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CashBoxId == req.ToCashBoxId && !b.IsDeleted, ct);
        if (to is null) return BadRequest(new { message = "الخزينة المستلمة مش موجودة" });
        if (!to.IsActive) return BadRequest(new { message = $"«{to.NameAr}» متوقفة" });

        if (req.Amount > from.CurrentBalance)
            return BadRequest(new { message = $"رصيد «{from.NameAr}» {from.CurrentBalance:N2} — مش كفاية" });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var group = Guid.NewGuid();
        var when  = Dt(req.TxDate);
        var desc  = B(req.Description) ?? $"تحويل من {from.NameAr} إلى {to.NameAr}";
        var uid   = CurrentUserId();

        _db.CashTransactions.AddRange(
            new CashTransaction
            {
                BranchId = branchId.Value, CashBoxId = from.CashBoxId, TransactionDate = when,
                TransactionType = "TransferOut", Amount = req.Amount,
                TransferGroupId = group, CounterpartCashBoxId = to.CashBoxId,
                Description = desc, Status = "Posted", CreatedBy = uid
            },
            new CashTransaction
            {
                BranchId = branchId.Value, CashBoxId = to.CashBoxId, TransactionDate = when,
                TransactionType = "TransferIn", Amount = req.Amount,
                TransferGroupId = group, CounterpartCashBoxId = from.CashBoxId,
                Description = desc, Status = "Posted", CreatedBy = uid
            });
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"✅ اتحوّل {req.Amount:N2} من {from.NameAr} إلى {to.NameAr}" });
    }

    // ═══════════════ إلغاء حركة ═══════════════

    [HttpPost("transactions/{id:long}/void")]
    [Authorize(Policy = "PERM:TREASURY.VOID")]
    public async Task<IActionResult> Void(long id, CancellationToken ct)
    {
        var t = await _db.CashTransactions.FirstOrDefaultAsync(x => x.CashTransactionId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "الحركة مش موجودة" });
        if (t.Status == "Void") return BadRequest(new { message = "الحركة ملغية أصلًا" });

        // التحويل = حركتين — بيتلغوا مع بعض
        var targets = new List<CashTransaction> { t };
        if (t.TransferGroupId is not null)
        {
            var group = t.TransferGroupId.Value;
            targets = await _db.CashTransactions
                .Where(x => x.TransferGroupId == group && !x.IsDeleted && x.Status == "Posted")
                .ToListAsync(ct);
        }

        foreach (var x in targets) x.Status = "Void";
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            message = targets.Count > 1
                ? $"❌ اتلغى التحويل ({targets.Count} حركات) والأرصدة اتظبطت"
                : "❌ اتلغت الحركة والرصيد اتظبط"
        });
    }

    // ═══════════════ كشف حساب العميل ═══════════════

    [HttpGet("customer-statement")]
    [Authorize]
    public async Task<IActionResult> CustomerStatement(int customerId, DateTime? from,
        DateTime? to, CancellationToken ct)
    {
        var uid = CurrentUserId();
        if (!await _perms.HasAsync(uid, "CUSTOMER.STATEMENT", ct) &&
            !await _perms.HasAsync(uid, "TREASURY.VIEW", ct))
            return Forbid();
        if (customerId <= 0) return BadRequest(new { message = "اختار العميل" });

        var cust = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId && !c.IsDeleted, ct);
        if (cust is null) return NotFound(new { message = "العميل مش موجود" });

        var end = to ?? DateTime.UtcNow.Date.AddDays(1);

        // رصيد أول المدة = كل اللي فات قبل «من»
        var openingInvoices = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.CustomerId == customerId && i.Status != "Cancelled" &&
                        i.Status != "Draft" && i.InvoiceDate < DateOnly.FromDateTime(from ?? DateTime.MinValue))
            .SumAsync(i => (decimal?)i.GrandTotal, ct) ?? 0;

        var openingPayments = await _db.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && p.CustomerId == customerId && p.Status == "Posted" &&
                        p.PaymentDate < DateOnly.FromDateTime(from ?? DateTime.MinValue))
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;

        var opening = openingInvoices - openingPayments;

        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.CustomerId == customerId &&
                        i.Status != "Cancelled" && i.Status != "Draft" &&
                        i.InvoiceDate >= DateOnly.FromDateTime(from ?? DateTime.MinValue) &&
                        i.InvoiceDate < DateOnly.FromDateTime(end))
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceId)
            .Select(i => new { i.InvoiceDate, i.InvoiceNumber, i.DocumentTypeCode, i.GrandTotal, i.Notes })
            .ToListAsync(ct);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && p.CustomerId == customerId && p.Status == "Posted" &&
                        p.PaymentDate >= DateOnly.FromDateTime(from ?? DateTime.MinValue) &&
                        p.PaymentDate < DateOnly.FromDateTime(end))
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.PaymentId)
            .Select(p => new { p.PaymentDate, p.PaymentNumber, p.Amount, p.ChequeNumber, p.Notes })
            .ToListAsync(ct);

        var lines = invoices
            .Select(i => new StmtLine(i.InvoiceDate.ToDateTime(TimeOnly.MinValue),
                i.DocumentTypeCode == "CreditNote" ? "CreditNote" : "Invoice",
                i.InvoiceNumber, i.Notes, i.GrandTotal, 0, 0))
            .Concat(payments.Select(p => new StmtLine(p.PaymentDate.ToDateTime(TimeOnly.MinValue),
                "Payment", p.PaymentNumber,
                p.ChequeNumber != null ? "شيك " + p.ChequeNumber : p.Notes, 0, p.Amount, 0)))
            .OrderBy(l => l.Date)
            .ToList();

        var bal = opening;
        for (var i = 0; i < lines.Count; i++)
        {
            bal += lines[i].Debit - lines[i].Credit;
            lines[i] = lines[i] with { Balance = bal };
        }

        return Ok(new StatementOut(cust.NameAr, opening, lines,
            lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), bal));
    }
}
