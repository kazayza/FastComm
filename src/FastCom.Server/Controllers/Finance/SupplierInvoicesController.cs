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
/// 📥 <b>الذمم الدائنة — فواتير الموردين</b> (<c>SupplierInvoices</c>).
///
/// <para>
/// الجداول دي كانت موجودة من أول الـ schema ومستخدمة **صفر** —
/// يعني الموردين كانوا مجرد «أسماء في دروبداون» من غير أي حسابات.
/// </para>
///
/// <para>
/// 🔐 <b>الصلاحيات:</b> <c>SUPPLIER.VIEW / CREATE / EDIT / DELETE</c> الموجودة —
/// فاتورة المورد جزء من إدارة المورد. <b>مافيش كود صلاحية جديد ⇒ مافيش SQL.</b>
/// </para>
///
/// <para>
/// 🔢 الترقيم مزروع أصلًا في <c>FastCom-schema.sql:2244</c>:
/// <c>('SUPPLIER_INVOICE','SINV-')</c> و<c>('SUPPLIER_PAYMENT','SPAY-')</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/supplier-invoices")]
[Authorize]
public class SupplierInvoicesController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly IAuditService _audit;
    private readonly INumberingService _num;
    private readonly ILogger<SupplierInvoicesController> _log;

    public SupplierInvoicesController(
        FastComDbContext db, INumberingService num, ILogger<SupplierInvoicesController> log, IAuditService audit)
    { _db = db; _audit = audit;
        _num = num;
        _log = log;
    }

    /* ═══════════════ DTOs ═══════════════ */

    public record LineDto(long? SupplierInvoiceItemId, long? TripId, long? OperationId,
        int? ExpenseTypeId, string Description, decimal Quantity, decimal UnitPrice,
        decimal TaxRate, decimal LineSubtotal, decimal LineTax, decimal LineTotal);

    public record ItemDto(long SupplierInvoiceId, string SupplierInvoiceNumber,
        string? SupplierRefNumber, int SupplierId, string SupplierName,
        string InvoiceDate, string? DueDate, decimal SubTotal, decimal TaxTotal,
        decimal GrandTotal, decimal PaidAmount, decimal Balance,
        string Status, string PaymentStatus, string? Notes, int LineCount);

    public record DetailDto(long SupplierInvoiceId, string SupplierInvoiceNumber,
        string? SupplierRefNumber, int SupplierId, string SupplierName, int BranchId,
        string InvoiceDate, string? DueDate, string CurrencyCode,
        decimal SubTotal, decimal TaxTotal, decimal GrandTotal, decimal PaidAmount,
        string Status, string PaymentStatus, string? Notes, List<LineDto> Lines);

    public record LineReq(long? TripId, long? OperationId, int? ExpenseTypeId,
        string? Description, decimal Quantity, decimal UnitPrice, decimal TaxRate);

    public record UpsertReq(int SupplierId, int BranchId, string? InvoiceDate,
        string? DueDate, string? SupplierRefNumber, string? Notes, List<LineReq>? Lines);

    /* ═══════════════ LIST ═══════════════ */

    [HttpGet]
    [Authorize(Policy = "PERM:SUPPLIER.VIEW")]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] int? supplierId, [FromQuery] string? status,
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        var query = _db.SupplierInvoices.AsNoTracking().Where(x => !x.IsDeleted);

        if (supplierId is > 0) query = query.Where(x => x.SupplierId == supplierId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);

        if (DateOnly.TryParse(from, out var f)) query = query.Where(x => x.InvoiceDate >= f);
        if (DateOnly.TryParse(to,   out var t)) query = query.Where(x => x.InvoiceDate <= t);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(x => x.SupplierInvoiceNumber.Contains(s)
                                     || (x.SupplierRefNumber != null && x.SupplierRefNumber.Contains(s))
                                     || x.Supplier.NameAr.Contains(s));
        }

        var rows = await query
            .OrderByDescending(x => x.SupplierInvoiceId)
            .Take(500)
            .Select(x => new
            {
                x.SupplierInvoiceId, x.SupplierInvoiceNumber, x.SupplierRefNumber,
                x.SupplierId, SupplierName = x.Supplier.NameAr,
                x.InvoiceDate, x.DueDate, x.SubTotal, x.TaxTotal,
                x.GrandTotal, x.PaidAmount, x.Status, x.PaymentStatus, x.Notes
            })
            .ToListAsync(ct);

        /* 🔴 عدد البنود باستعلام منفصل — GroupBy + Take مش بيتترجموا مع بعض في EF8 */
        var ids = rows.Select(r => r.SupplierInvoiceId).ToList();
        var counts = ids.Count == 0
            ? new Dictionary<long, int>()
            : (await _db.SupplierInvoiceItems.AsNoTracking()
                    .Where(i => ids.Contains(i.SupplierInvoiceId))
                    .GroupBy(i => i.SupplierInvoiceId)
                    .Select(g => new { Id = g.Key, N = g.Count() })
                    .ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.N);

        return Ok(rows.Select(r => new ItemDto(
            r.SupplierInvoiceId, r.SupplierInvoiceNumber, r.SupplierRefNumber,
            r.SupplierId, r.SupplierName,
            Iso(r.InvoiceDate), Iso(r.DueDate),
            r.SubTotal, r.TaxTotal, r.GrandTotal, r.PaidAmount,
            r.GrandTotal - r.PaidAmount,
            r.Status, r.PaymentStatus, r.Notes,
            counts.TryGetValue(r.SupplierInvoiceId, out var n) ? n : 0)).ToList());
    }

    /* ═══════════════ DETAIL ═══════════════ */

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:SUPPLIER.VIEW")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct)
    {
        var inv = await _db.SupplierInvoices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SupplierInvoiceId == id && !x.IsDeleted, ct);
        if (inv is null) return NotFound(new { message = "الفاتورة مش موجودة" });

        var sup = await _db.Suppliers.AsNoTracking()
            .Where(s => s.SupplierId == inv.SupplierId)
            .Select(s => s.NameAr).FirstOrDefaultAsync(ct) ?? "—";

        var lines = await _db.SupplierInvoiceItems.AsNoTracking()
            .Where(i => i.SupplierInvoiceId == id)
            .OrderBy(i => i.SupplierInvoiceItemId)
            .Select(i => new LineDto(i.SupplierInvoiceItemId, i.TripId, i.OperationId,
                i.ExpenseTypeId, i.Description, i.Quantity, i.UnitPrice, i.TaxRate,
                i.LineSubtotal ?? 0m, i.LineTax ?? 0m, i.LineTotal ?? 0m))
            .ToListAsync(ct);

        return Ok(new DetailDto(
            inv.SupplierInvoiceId, inv.SupplierInvoiceNumber, inv.SupplierRefNumber,
            inv.SupplierId, sup, inv.BranchId,
            Iso(inv.InvoiceDate), Iso(inv.DueDate), inv.CurrencyCode,
            inv.SubTotal, inv.TaxTotal, inv.GrandTotal, inv.PaidAmount,
            inv.Status, inv.PaymentStatus, inv.Notes, lines));
    }

    /* ═══════════════ CREATE ═══════════════ */

    [HttpPost]
    [Authorize(Policy = "PERM:SUPPLIER.CREATE")]
    public async Task<IActionResult> Create([FromBody] UpsertReq req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var number = await _num.NextAsync("SUPPLIER_INVOICE", ct);
        var (sub, tax, lines) = Build(req.Lines!);

        var inv = new SupplierInvoice
        {
            SupplierInvoiceNumber = number,
            SupplierRefNumber     = B(req.SupplierRefNumber),
            BranchId              = req.BranchId,
            SupplierId            = req.SupplierId,
            InvoiceDate           = ParseDate(req.InvoiceDate) ?? DateOnly.FromDateTime(DateTime.Today),
            DueDate               = ParseDate(req.DueDate),
            CurrencyCode          = "EGP",
            SubTotal              = sub,
            TaxTotal              = tax,
            GrandTotal            = sub + tax,
            PaidAmount            = 0m,
            Status                = "Draft",
            PaymentStatus         = "Unpaid",
            Notes                 = B(req.Notes),
            IsDeleted             = false,
            CreatedAt             = DateTime.UtcNow,
            CreatedBy             = UserId()
        };

        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.SupplierInvoices.Add(inv);
            await _db.SaveChangesAsync(ct);

            foreach (var l in lines)
            {
                l.SupplierInvoiceId = inv.SupplierInvoiceId;
                _db.SupplierInvoiceItems.Add(l);
            }
            await _db.SaveChangesAsync(ct);

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception ex)
        {
            await _db.Database.RollbackTransactionAsync(ct);
            _log.LogError(ex, "فشل إنشاء فاتورة مورد");
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Create, "SupplierInvoice", null, description: "إنشاء فاتورة مورد", ct: ct);
            return BadRequest(new { message = "فشل الحفظ: " + ex.Message });
        }

        return Ok(new { message = $"✅ اتسجلت الفاتورة {number}", id = inv.SupplierInvoiceId });
    }

    /* ═══════════════ UPDATE ═══════════════ */

    [HttpPut("{id:long}")]
    [Authorize(Policy = "PERM:SUPPLIER.EDIT")]
    public async Task<IActionResult> Update(long id, [FromBody] UpsertReq req, CancellationToken ct)
    {
        var inv = await _db.SupplierInvoices
            .FirstOrDefaultAsync(x => x.SupplierInvoiceId == id && !x.IsDeleted, ct);
        if (inv is null) return NotFound(new { message = "الفاتورة مش موجودة" });

        /* 🔴 المعتمدة ما تتعدّلش — الأرقام اتبنت عليها دفعات */
        if (inv.Status != "Draft")
            {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Update, "SupplierInvoice", null, description: "تعديل فاتورة مورد", ct: ct);
            
            }

        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var (sub, tax, lines) = Build(req.Lines!);

        inv.SupplierRefNumber = B(req.SupplierRefNumber);
        inv.SupplierId        = req.SupplierId;
        inv.BranchId          = req.BranchId;
        inv.InvoiceDate       = ParseDate(req.InvoiceDate) ?? inv.InvoiceDate;
        inv.DueDate           = ParseDate(req.DueDate);
        inv.SubTotal          = sub;
        inv.TaxTotal          = tax;
        inv.GrandTotal        = sub + tax;
        inv.Notes             = B(req.Notes);
        inv.UpdatedAt         = DateTime.UtcNow;
        inv.UpdatedBy         = UserId();

        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var old = await _db.SupplierInvoiceItems
                .Where(i => i.SupplierInvoiceId == id).ToListAsync(ct);
            _db.SupplierInvoiceItems.RemoveRange(old);

            foreach (var l in lines)
            {
                l.SupplierInvoiceItemId = 0;
                l.SupplierInvoiceId     = id;
                _db.SupplierInvoiceItems.Add(l);
            }

            await _db.SaveChangesAsync(ct);
            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception ex)
        {
            await _db.Database.RollbackTransactionAsync(ct);
            _log.LogError(ex, "فشل تعديل فاتورة مورد {Id}", id);
            return BadRequest(new { message = "فشل الحفظ: " + ex.Message });
        }

        return Ok(new { message = "✅ اتحدّثت الفاتورة" });
    }

    /* ═══════════════ APPROVE ═══════════════ */

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = "PERM:SUPPLIER.EDIT")]
    public async Task<IActionResult> Approve(long id, CancellationToken ct)
    {
        var inv = await _db.SupplierInvoices
            .FirstOrDefaultAsync(x => x.SupplierInvoiceId == id && !x.IsDeleted, ct);
        if (inv is null) return NotFound(new { message = "الفاتورة مش موجودة" });

        if (inv.Status != "Draft")
            {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Approve, "SupplierInvoice", null, description: "اعتماد فاتورة مورد", ct: ct);
            
            }
        if (inv.GrandTotal <= 0)
            return BadRequest(new { message = "الفاتورة فاضية — ضيف بند واحد على الأقل" });

        inv.Status      = "Approved";
        inv.ApprovedAt  = DateTime.UtcNow;
        inv.ApprovedBy  = UserId();
        inv.UpdatedAt   = DateTime.UtcNow;
        inv.UpdatedBy   = UserId();

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتعتمدت الفاتورة" });
    }

    /* ═══════════════ CANCEL ═══════════════ */

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = "PERM:SUPPLIER.EDIT")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        var inv = await _db.SupplierInvoices
            .FirstOrDefaultAsync(x => x.SupplierInvoiceId == id && !x.IsDeleted, ct);
        if (inv is null) return NotFound(new { message = "الفاتورة مش موجودة" });

        if (inv.Status is "Cancelled")
            {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Cancel, "SupplierInvoice", null, description: "إلغاء فاتورة مورد", ct: ct);
            
            }

        /* 🔴 المدفوعة ما تتلغاش — فيه فلوس اتحركت */
        if (inv.PaidAmount > 0)
            return BadRequest(new
            {
                message = $"اتدفع منها {inv.PaidAmount:N2} — ابطل الدفعات الأول"
            });

        inv.Status    = "Cancelled";
        inv.UpdatedAt = DateTime.UtcNow;
        inv.UpdatedBy = UserId();

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتلغت الفاتورة" });
    }

    /* ═══════════════ DELETE (ناعم — مسودة بس) ═══════════════ */

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:SUPPLIER.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var inv = await _db.SupplierInvoices
            .FirstOrDefaultAsync(x => x.SupplierInvoiceId == id && !x.IsDeleted, ct);
        if (inv is null) return NotFound(new { message = "الفاتورة مش موجودة" });

        if (inv.Status != "Draft")
            {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Delete, "SupplierInvoice", null, description: "حذف فاتورة مورد", ct: ct);
            
            }

        inv.IsDeleted = true;
        inv.DeletedAt = DateTime.UtcNow;
        inv.DeletedBy = UserId();

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحذفت الفاتورة" });
    }

    /* ═══════════════ helpers ═══════════════ */

    /// <summary>بيحسب البنود — <c>decimal?</c> في الـ schema فبنحط قيمة دايمًا.</summary>
    private static (decimal Sub, decimal Tax, List<SupplierInvoiceItem> Lines) Build(List<LineReq> req)
    {
        decimal sub = 0m, tax = 0m;
        var list = new List<SupplierInvoiceItem>(req.Count);

        foreach (var r in req)
        {
            var lineSub = Math.Round(r.Quantity * r.UnitPrice, 4);
            var lineTax = Math.Round(lineSub * r.TaxRate / 100m, 4);

            sub += lineSub;
            tax += lineTax;

            list.Add(new SupplierInvoiceItem
            {
                TripId        = r.TripId,
                OperationId   = r.OperationId,
                ExpenseTypeId = r.ExpenseTypeId,
                Description   = B(r.Description) ?? "—",
                Quantity      = r.Quantity,
                UnitPrice     = r.UnitPrice,
                TaxRate       = r.TaxRate,
                LineSubtotal  = lineSub,
                LineTax       = lineTax,
                LineTotal     = lineSub + lineTax
            });
        }

        return (sub, tax, list);
    }

    private async Task<string?> ValidateAsync(UpsertReq r, CancellationToken ct)
    {
        if (r.SupplierId <= 0) return "لازم تختار المورد";
        if (r.BranchId <= 0)   return "لازم تختار الفرع";

        if (!await _db.Suppliers.AnyAsync(s => s.SupplierId == r.SupplierId && !s.IsDeleted, ct))
            return "المورد مش موجود";
        if (!await _db.Branches.AnyAsync(b => b.BranchId == r.BranchId && !b.IsDeleted, ct))
            return "الفرع مش موجود";

        if (r.Lines is null || r.Lines.Count == 0) return "لازم بند واحد على الأقل";

        foreach (var l in r.Lines)
        {
            if (l.Quantity <= 0)  return "الكمية لازم تكون أكبر من صفر";
            if (l.UnitPrice < 0)  return "سعر الوحدة ماينفعش يكون سالب";
            if (l.TaxRate < 0)    return "نسبة الضريبة ماينفعش تكون سالبة";
            if (l.TaxRate > 100)  return "نسبة الضريبة ماينفعش تعدى 100%";
        }

        var d = ParseDate(r.DueDate);
        var i = ParseDate(r.InvoiceDate);
        if (d is not null && i is not null && d < i)
            return "تاريخ الاستحقاق قبل تاريخ الفاتورة";

        return null;
    }

    /// <summary>Empty → <c>null</c> (الأعمدة nullable).</summary>
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
        "Draft"         => "مسودة",
        "Approved"      => "معتمدة",
        "PartiallyPaid" => "مدفوعة جزئيًا",
        "Paid"          => "مدفوعة",
        "Cancelled"     => "ملغاة",
        _               => s
    };

    private int? UserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(v, out var id) ? id : null;
    }
}
