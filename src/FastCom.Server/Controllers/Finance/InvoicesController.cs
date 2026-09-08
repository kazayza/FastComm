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
/// فواتير العملاء — بتتبنى من بنود إيرادات العمليات.
/// <para>🔴 <c>LineSubtotal / LineTax / LineTotal</c> أعمدة محسوبة في SQL Server — ماتكتبهاش.</para>
/// <para>🔴 <c>PaidAmount / PaymentStatus</c> بيحسبهم <c>trg_PaymentAllocations_InvoiceSync</c>.</para>
/// <para>الحالة: <c>Draft → Approved → Issued → Sent</c> (+ <c>Cancelled</c> / <c>Returned</c>).</para>
/// </summary>
[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly INumberingService _numbers;
    private readonly IPermissionService _perms;

    public InvoicesController(FastComDbContext db, INumberingService numbers, IPermissionService perms)
    { _db = db; _numbers = numbers; _perms = perms; }

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

    public record LineIn(long? OperationId, int? ServiceId, long? PriceRuleId, string? Description,
        decimal Quantity, decimal UnitPrice, decimal Discount, int? TaxRateId);

    public record InvoiceUpsert(int CustomerId, string? InvoiceDate, string? DueDate,
        string? Notes, List<LineIn>? Items, List<long>? OperationIds);

    public record CreditNoteRequest(long OriginalInvoiceId, string? ReasonCode,
        string? InvoiceDate, string? Notes, List<LineIn>? Items);

    public record StatusRequest(string? To);

    /// <summary>قنوات الإرسال المسموحة — <c>Email</c> مقفولة لحد ما SMTP يتوصّل.</summary>
    private static readonly string[] Channels = { "WhatsApp", "Portal", "Fax", "Manual" };

    public record SendRequest(string? Channel, string? Recipient, string? AttachmentPath);

    public record ListItem(long InvoiceId, string InvoiceNumber, string DocumentTypeCode,
        string CustomerName, DateOnly InvoiceDate, DateOnly? DueDate, decimal SubTotal,
        decimal TaxTotal, decimal GrandTotal, decimal PaidAmount, decimal Due,
        string Status, string PaymentStatus, int ItemsCount, int OperationsCount);

    public record LineOut(long InvoiceItemId, long? OperationId, string? OperationNumber,
        int? ServiceId, string Description, decimal Quantity, decimal UnitPrice, decimal Discount,
        int? TaxRateId, decimal TaxRate, decimal LineSubtotal, decimal LineTax, decimal LineTotal);

    public record Detail(long InvoiceId, string InvoiceNumber, string DocumentTypeCode,
        string InvoiceType, int CustomerId, string CustomerName, string? CustomerTaxNumber,
        string? InvoiceDate, string? DueDate, decimal SubTotal, decimal DiscountTotal,
        decimal TaxTotal, decimal GrandTotal, decimal PaidAmount, decimal Due,
        string Status, string PaymentStatus, string? Notes, string? OriginalInvoiceNumber,
        DateTime? IssuedAt, string CurrencyCode);

    public record OpRef(long OperationId, string OperationNumber, string CustomerName,
        decimal RevenueNet, string? InvoiceNumber);

    public record InvOpt(long InvoiceId, string InvoiceNumber, decimal GrandTotal,
        decimal PaidAmount, decimal Due);

    public record OpLine(long? OperationId, string? OperationNumber, int? ServiceId,
        long? PriceRuleId, string Description, decimal Quantity, decimal UnitPrice,
        decimal Discount, int? TaxRateId);

    public record AllocOut(long PaymentId, string PaymentNumber, decimal AllocatedAmount);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:INVOICE.VIEW")]
    public async Task<IActionResult> List(string? status, string? payStatus, int? customerId,
        string? docType, string? q, int take = 300, CancellationToken ct = default)
    {
        if (take <= 0 || take > 1000) take = 300;

        var query = _db.Invoices.AsNoTracking().Where(i => !i.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status))   query = query.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(payStatus)) query = query.Where(i => i.PaymentStatus == payStatus);
        if (customerId is not null)               query = query.Where(i => i.CustomerId == customerId);
        if (!string.IsNullOrWhiteSpace(docType))  query = query.Where(i => i.DocumentTypeCode == docType);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(i => i.InvoiceNumber.Contains(s) || i.Customer.NameAr.Contains(s));
        }

        var rows = await query
            .OrderByDescending(i => i.InvoiceId)
            .Take(take)
            .Select(i => new ListItem(
                i.InvoiceId, i.InvoiceNumber, i.DocumentTypeCode, i.Customer.NameAr,
                i.InvoiceDate, i.DueDate, i.SubTotal, i.TaxTotal, i.GrandTotal, i.PaidAmount,
                i.GrandTotal - i.PaidAmount, i.Status, i.PaymentStatus,
                _db.InvoiceItems.Count(x => x.InvoiceId == i.InvoiceId),
                _db.InvoiceOperations.Count(x => x.InvoiceId == i.InvoiceId)))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ONE ═══════════════

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:INVOICE.VIEW")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var i = await _db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.InvoiceId == id && !x.IsDeleted, ct);
        if (i is null) return NotFound(new { message = "الفاتورة مش موجودة" });

        var cust = await _db.Customers.AsNoTracking()
            .Where(c => c.CustomerId == i.CustomerId)
            .Select(c => new { c.NameAr, c.TaxNumber })
            .FirstOrDefaultAsync(ct);

        var items = await _db.InvoiceItems.AsNoTracking()
            .Where(x => x.InvoiceId == id)
            .OrderBy(x => x.InvoiceItemId)
            .Select(x => new LineOut(x.InvoiceItemId, x.OperationId,
                x.Operation != null ? x.Operation.OperationNumber : null,
                x.ServiceId, x.Description, x.Quantity, x.UnitPrice, x.Discount,
                x.TaxRateId, x.TaxRate,
                x.LineSubtotal ?? 0, x.LineTax ?? 0, x.LineTotal ?? 0))
            .ToListAsync(ct);

        var ops = await _db.InvoiceOperations.AsNoTracking()
            .Where(x => x.InvoiceId == id)
            .Select(x => new { x.OperationId, x.Operation.OperationNumber })
            .ToListAsync(ct);

        string? originalNo = null;
        if (i.OriginalInvoiceId is not null)
            originalNo = await _db.Invoices.AsNoTracking()
                .Where(o => o.InvoiceId == i.OriginalInvoiceId)
                .Select(o => o.InvoiceNumber).FirstOrDefaultAsync(ct);

        var detail = new Detail(i.InvoiceId, i.InvoiceNumber, i.DocumentTypeCode, i.InvoiceType,
            i.CustomerId, cust?.NameAr ?? "—", cust?.TaxNumber,
            Iso(i.InvoiceDate), IsoN(i.DueDate), i.SubTotal, i.DiscountTotal, i.TaxTotal,
            i.GrandTotal, i.PaidAmount, i.GrandTotal - i.PaidAmount,
            i.Status, i.PaymentStatus, i.Notes, originalNo, i.IssuedAt, i.CurrencyCode);

        return Ok(new
        {
            Invoice    = detail,
            Items      = items,
            Operations = ops.Select(o => new { o.OperationId, o.OperationNumber }),
            Allocations = await _db.PaymentAllocations.AsNoTracking()
                .Where(a => a.InvoiceId == id)
                .Select(a => new AllocOut(a.PaymentId, a.Payment.PaymentNumber, a.AllocatedAmount))
                .ToListAsync(ct)
        });
    }

    // ═══════════════ عمليات قابلة للفوترة ═══════════════

    /// <summary>عمليات العميل اللي ليها بنود إيرادات — مع رقم الفاتورة لو اتفوترت.</summary>
    [HttpGet("billable-operations")]
    [Authorize(Policy = "PERM:INVOICE.VIEW")]
    public async Task<IActionResult> BillableOperations(int customerId, CancellationToken ct)
    {
        if (customerId <= 0) return BadRequest(new { message = "اختار العميل" });

        var rows = await _db.Operations.AsNoTracking()
            .Where(o => !o.IsDeleted && o.CustomerId == customerId &&
                        o.Status != "Cancelled" && o.Status != "Pending")
            .OrderByDescending(o => o.OperationId)
            .Take(200)
            .Select(o => new OpRef(o.OperationId, o.OperationNumber, o.Customer.NameAr, o.RevenueNet,
                _db.InvoiceOperations
                    .Where(io => io.OperationId == o.OperationId && !io.Invoice.IsDeleted &&
                                 io.Invoice.Status != "Cancelled")
                    .Select(io => io.Invoice.InvoiceNumber)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        return Ok(rows.Where(r => r.RevenueNet > 0 || r.InvoiceNumber != null));
    }

    /// <summary>بنود إيرادات عملية — عشان نملا سطور الفاتورة منها.</summary>
    [HttpGet("operations/{opId:long}/lines")]
    [Authorize(Policy = "PERM:INVOICE.VIEW")]
    public async Task<IActionResult> OperationLines(long opId, CancellationToken ct)
    {
        var rows = await _db.OperationRevenueItems.AsNoTracking()
            .Where(r => r.OperationId == opId)
            .OrderBy(r => r.OperationRevenueItemId)
            .Select(r => new OpLine(r.OperationId, r.Operation.OperationNumber, r.ServiceId,
                r.PriceRuleId, r.Description ?? r.Service.NameAr,
                r.Quantity, r.UnitPrice, r.Discount, r.TaxRateId))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>عملاء للفوترة — بصلاحية الفواتير (مش CUSTOMER.VIEW).</summary>
    [HttpGet("customers")]
    [Authorize(Policy = "PERM:INVOICE.VIEW")]
    public async Task<IActionResult> Customers(CancellationToken ct) =>
        Ok(await _db.Customers.AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.NameAr)
            .Select(c => new CustOpt(c.CustomerId, c.NameAr, c.CustomerCode, c.PaymentTermId))
            .ToListAsync(ct));

    public record CustOpt(int Id, string Label, string Code, int? PaymentTermId);

    /// <summary>الخدمات — للسطور اليدوية.</summary>
    [HttpGet("services")]
    [Authorize(Policy = "PERM:INVOICE.VIEW")]
    public async Task<IActionResult> Services(CancellationToken ct) =>
        Ok(await _db.Services.AsNoTracking()
            .OrderBy(s => s.ServiceCode)
            .Select(s => new SvcOpt(s.ServiceId, s.NameAr, s.Unit, s.TaxRateId))
            .ToListAsync(ct));

    public record SvcOpt(int Id, string Label, string Unit, int? TaxRateId);

    /// <summary>نسب الضريبة — بنسخ منها الـ Snapshot على السطر.</summary>
    [HttpGet("tax-rates")]
    [Authorize(Policy = "PERM:INVOICE.VIEW")]
    public async Task<IActionResult> TaxRates(CancellationToken ct) =>
        Ok(await _db.TaxRates.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.TaxRateId)
            .Select(t => new TxOpt(t.TaxRateId, t.Code + " — " + t.Rate.ToString("0.##") + "%", t.Rate))
            .ToListAsync(ct));

    public record TxOpt(int Id, string Label, decimal Rate);

    /// <summary>فواتير العميل المفتوحة — بتظهر في شاشة التحصيل.</summary>
    [HttpGet("open")]
    [Authorize(Policy = "PERM:INVOICE.VIEW")]
    public async Task<IActionResult> Open(int customerId, CancellationToken ct)
    {
        if (customerId <= 0) return BadRequest(new { message = "اختار العميل" });

        var rows = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.CustomerId == customerId &&
                        i.Status != "Cancelled" && i.Status != "Draft" &&
                        i.PaymentStatus != "Paid")
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceId)
            .Select(i => new InvOpt(i.InvoiceId, i.InvoiceNumber, i.GrandTotal,
                                    i.PaidAmount, i.GrandTotal - i.PaidAmount))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:INVOICE.CREATE")]
    public async Task<IActionResult> Create([FromBody] InvoiceUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, isCredit: false, excludeInvoiceId: null, ct);
        if (err is not null) return BadRequest(new { message = err });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var cust = await _db.Customers.AsNoTracking()
            .FirstAsync(c => c.CustomerId == req.CustomerId, ct);

        var invDate = D(req.InvoiceDate);
        var due = Dn(req.DueDate) ?? await DefaultDueDateAsync(cust.PaymentTermId, invDate, ct);

        var inv = new Invoice
        {
            InvoiceNumber    = await _numbers.NextAsync("INVOICE", ct),
            BranchId         = branchId.Value,
            CustomerId       = req.CustomerId,
            InvoiceDate      = invDate,
            DueDate          = due,
            DocumentTypeCode = "Invoice",
            // 🔴 InvoiceType العمود الوحيد NOT NULL **من غير DEFAULT** في Invoices،
            //    وكان بيتحط بعد أول SaveChanges → SQL Error 515 (NULL).
            //    بنحط NonTax مبدئيًا وبنعدّله بعد ما البنود تتحسب.
            InvoiceType      = "NonTax",
            Status           = "Draft",
            PaymentStatus    = "Unpaid",
            CurrencyCode     = "EGP",
            Notes            = B(req.Notes),
            CreatedBy        = CurrentUserId()
        };
        _db.Invoices.Add(inv);
        await _db.SaveChangesAsync(ct);

        var rates = await RatesAsync(req.Items!, ct);
        AddLines(inv, req.Items!, rates);
        await AddOperationLinksAsync(inv, req, ct);
        RecalcTotals(inv, req.Items!, rates);
        inv.InvoiceType = inv.TaxTotal > 0 ? "Tax" : "NonTax";
        inv.UpdatedAt   = DateTime.UtcNow;
        inv.UpdatedBy   = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = inv.InvoiceId, number = inv.InvoiceNumber,
            message = $"✅ اتحفظت الفاتورة برقم {inv.InvoiceNumber}" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:long}")]
    [Authorize(Policy = "PERM:INVOICE.EDIT")]
    public async Task<IActionResult> Update(long id, [FromBody] InvoiceUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, isCredit: false, excludeInvoiceId: id, ct);
        if (err is not null) return BadRequest(new { message = err });

        var inv = await _db.Invoices.FirstOrDefaultAsync(x => x.InvoiceId == id && !x.IsDeleted, ct);
        if (inv is null) return NotFound(new { message = "الفاتورة مش موجودة" });
        if (inv.Status is not ("Draft" or "Approved"))
            return BadRequest(new { message = "الفاتورة اتصدرت — مافيش تعديل" });
        if (await _db.PaymentAllocations.AnyAsync(a => a.InvoiceId == id, ct))
            return BadRequest(new { message = "في دفعات متربطة بالفاتورة — الغِ الدفعات الأول" });

        var old = await _db.InvoiceItems.Where(x => x.InvoiceId == id).ToListAsync(ct);
        _db.InvoiceItems.RemoveRange(old);
        var oldOps = await _db.InvoiceOperations.Where(x => x.InvoiceId == id).ToListAsync(ct);
        _db.InvoiceOperations.RemoveRange(oldOps);
        await _db.SaveChangesAsync(ct);

        inv.CustomerId  = req.CustomerId;
        inv.InvoiceDate = D(req.InvoiceDate);
        inv.DueDate     = Dn(req.DueDate) ?? inv.DueDate;
        inv.Notes       = B(req.Notes);

        var rates = await RatesAsync(req.Items!, ct);
        AddLines(inv, req.Items!, rates);
        await AddOperationLinksAsync(inv, req, ct);
        RecalcTotals(inv, req.Items!, rates);
        inv.InvoiceType = inv.TaxTotal > 0 ? "Tax" : "NonTax";
        inv.UpdatedAt   = DateTime.UtcNow;
        inv.UpdatedBy   = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اتعدّلت الفاتورة" });
    }

    // ═══════════════ إشعار دائن ═══════════════

    [HttpPost("credit-note")]
    [Authorize(Policy = "PERM:INVOICE.CREDITNOTE")]
    public async Task<IActionResult> CreditNote([FromBody] CreditNoteRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { message = "البيانات مش كاملة" });

        var original = await _db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.InvoiceId == req.OriginalInvoiceId && !i.IsDeleted, ct);
        if (original is null) return BadRequest(new { message = "الفاتورة الأصلية مش موجودة" });
        if (original.Status is "Draft" or "Cancelled")
            return BadRequest(new { message = "الفاتورة الأصلية مش مصدّرة — مايطلعش عليها إشعار دائن" });
        if (original.DocumentTypeCode == "CreditNote")
            return BadRequest(new { message = "الإشعار الدائن مايطلعش على إشعار دائن" });

        var lines = req.Items is { Count: > 0 } ? req.Items : await OriginalLinesNegatedAsync(original.InvoiceId, ct);
        if (lines.Count == 0) return BadRequest(new { message = "مافيش بنود للإشعار الدائن" });

        foreach (var l in lines)
            if (l.Quantity >= 0)
                return BadRequest(new { message = "بنود الإشعار الدائن لازم تكون بكمية سالبة" });

        var rates = await RatesAsync(lines, ct);

        // CK_Invoices_Sign — نتحقق قبل ما نلمس الداتابيز
        var creditTotal = lines.Sum(l => (l.Quantity * l.UnitPrice - l.Discount) *
            (l.TaxRateId is not null && rates.TryGetValue(l.TaxRateId.Value, out var r) ? r : 0m) / 100m
            + l.Quantity * l.UnitPrice - l.Discount);
        if (creditTotal > 0)
            return BadRequest(new { message = "الإشعار الدائن طلع بمبلغ موجب — راجع البنود" });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var inv = new Invoice
        {
            InvoiceNumber     = await _numbers.NextAsync("CREDITNOTE", ct),
            BranchId          = branchId.Value,
            CustomerId        = original.CustomerId,
            InvoiceDate       = D(req.InvoiceDate),
            DueDate           = null,
            DocumentTypeCode  = "CreditNote",
            OriginalInvoiceId = original.InvoiceId,
            ReasonCode        = B(req.ReasonCode),
            InvoiceType       = "NonTax",   // 🔴 نفس مشكلة Create — NOT NULL من غير DEFAULT
            Status            = "Draft",
            PaymentStatus     = "Unpaid",
            CurrencyCode      = "EGP",
            Notes             = B(req.Notes),
            CreatedBy         = CurrentUserId()
        };
        _db.Invoices.Add(inv);
        await _db.SaveChangesAsync(ct);

        AddLines(inv, lines, rates);
        RecalcTotals(inv, lines, rates);
        inv.InvoiceType = inv.TaxTotal < 0 ? "Tax" : "NonTax";
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = inv.InvoiceId, number = inv.InvoiceNumber,
            message = $"✅ اتعمل إشعار دائن برقم {inv.InvoiceNumber} على {original.InvoiceNumber}" });
    }

    // ═══════════════ تغيير الحالة ═══════════════

    [HttpPost("{id:long}/status")]
    [Authorize]
    public async Task<IActionResult> SetStatus(long id, [FromBody] StatusRequest req, CancellationToken ct)
    {
        var to = req?.To;
        if (to is not ("Approved" or "Issued" or "Sent" or "Cancelled" or "Returned"))
            return BadRequest(new { message = "الحالة المطلوبة مش معروفة" });

        // كل انتقال بصلاحية مستقلة
        var need = to switch
        {
            "Approved"  => "INVOICE.APPROVE",
            "Issued"    => "INVOICE.ISSUE",
            "Sent"      => "INVOICE.SEND",
            "Cancelled" => "INVOICE.CANCEL",
            _           => "INVOICE.EDIT"
        };
        if (!await _perms.HasAsync(CurrentUserId(), need, ct))
            return Forbid();

        var inv = await _db.Invoices.FirstOrDefaultAsync(x => x.InvoiceId == id && !x.IsDeleted, ct);
        if (inv is null) return NotFound(new { message = "الفاتورة مش موجودة" });

        switch (to)
        {
            case "Cancelled":
                if (inv.Status == "Cancelled") return BadRequest(new { message = "الفاتورة ملغية أصلًا" });
                if (await _db.PaymentAllocations.AnyAsync(a => a.InvoiceId == id, ct))
                    return BadRequest(new { message = "في دفعات متربطة بالفاتورة — الغِ الدفعات الأول" });
                break;
            case "Returned":
                if (inv.Status is not ("Issued" or "Sent"))
                    return BadRequest(new { message = "المرتجع بيكون على فاتورة مصدّرة" });
                if (await _db.PaymentAllocations.AnyAsync(a => a.InvoiceId == id, ct))
                    return BadRequest(new { message = "في دفعات متربطة بالفاتورة — الغِ الدفعات الأول" });
                break;
            default:
                if (!CanAdvance(inv.Status, to))
                    return BadRequest(new { message = $"مينفعش تنقل من {Ar(inv.Status)} إلى {Ar(to)}" });
                break;
        }

        if (to == "Issued" && inv.IssuedAt is null)
        { inv.IssuedAt = DateTime.UtcNow; inv.IssuedBy = CurrentUserId(); }

        inv.Status    = to;
        inv.UpdatedAt = DateTime.UtcNow;
        inv.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"✅ بقت {Ar(to)}" });
    }

    // ═══════════════ تسليم الفاتورة للعميل ═══════════════

    /// <summary>
    /// بيسجّل إن الفاتورة اتسلّمت للعميل ويحوّلها «مُرسلة».
    /// <para>🔴 مافيش إرسال إيميل حقيقي لسه — <c>Email</c> مش في القنوات المتاحة
    /// عشان <c>MailKit</c> مش متثبّت. لما يتوصّل SMTP نضيفها.</para>
    /// </summary>
    [HttpPost("{id:long}/send")]
    [Authorize(Policy = "PERM:INVOICE.SEND")]
    public async Task<IActionResult> Send(long id, [FromBody] SendRequest? req, CancellationToken ct)
    {
        var channel = string.IsNullOrWhiteSpace(req?.Channel) ? "Manual" : req!.Channel!.Trim();
        if (!Channels.Contains(channel))
            return BadRequest(new { message = "قناة التسليم لازم تكون واتساب أو بوابة العميل أو فاكس أو تسليم يدوي" });

        var recipient = B(req?.Recipient);
        if (recipient is null) return BadRequest(new { message = "اكتب اسم أو رقم أو إيميل المستلم" });
        if (recipient.Length > 254) return BadRequest(new { message = "اسم المستلم أطول من 254 حرف" });

        var inv = await _db.Invoices.FirstOrDefaultAsync(x => x.InvoiceId == id && !x.IsDeleted, ct);
        if (inv is null) return NotFound(new { message = "الفاتورة مش موجودة" });
        if (inv.Status is "Draft" or "Cancelled")
            return BadRequest(new { message = "الفاتورة لازم تكون معتمدة أو مصدّرة قبل التسليم" });

        _db.InvoiceSendLogs.Add(new InvoiceSendLog
        {
            InvoiceId      = id,
            SentBy         = CurrentUserId(),
            Recipient      = recipient,
            Channel        = channel,
            Status         = "Success",
            AttachmentPath = B(req?.AttachmentPath)
        });

        if (inv.Status != "Sent")
        {
            inv.Status    = "Sent";
            inv.UpdatedAt = DateTime.UtcNow;
            inv.UpdatedBy = CurrentUserId();
            if (inv.IssuedAt is null) { inv.IssuedAt = DateTime.UtcNow; inv.IssuedBy = CurrentUserId(); }
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"✅ اتسلّمت الفاتورة {inv.InvoiceNumber} ({ChannelAr(channel)})" });
    }

    /// <summary>سجل تسليم فاتورة.</summary>
    [HttpGet("{id:long}/send-logs")]
    [Authorize(Policy = "PERM:INVOICE.VIEW")]
    public async Task<IActionResult> SendLogs(long id, CancellationToken ct) =>
        Ok(await _db.InvoiceSendLogs.AsNoTracking()
            .Where(l => l.InvoiceId == id)
            .OrderByDescending(l => l.SentAt)
            .Select(l => new SendLogOut(l.InvoiceSendLogId, l.SentAt, l.Recipient,
                                        l.Channel, l.Status, l.ErrorMessage))
            .ToListAsync(ct));

    public record SendLogOut(long Id, DateTime SentAt, string Recipient,
                             string Channel, string Status, string? ErrorMessage);

    private static string ChannelAr(string c) => c switch
    {
        "WhatsApp" => "واتساب", "Portal" => "بوابة العميل", "Fax" => "فاكس",
        "Manual" => "تسليم يدوي", "Email" => "إيميل", _ => c
    };

    // ═══════════════ حذف ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:INVOICE.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var inv = await _db.Invoices.FirstOrDefaultAsync(x => x.InvoiceId == id && !x.IsDeleted, ct);
        if (inv is null) return NotFound(new { message = "الفاتورة مش موجودة" });
        if (inv.Status != "Draft")
            return BadRequest(new { message = "المسودة بس هي اللي بتتحذف" });
        if (await _db.PaymentAllocations.AnyAsync(a => a.InvoiceId == id, ct))
            return BadRequest(new { message = "في دفعات متربطة بالفاتورة" });

        var items = await _db.InvoiceItems.Where(x => x.InvoiceId == id).ToListAsync(ct);
        _db.InvoiceItems.RemoveRange(items);
        var ops = await _db.InvoiceOperations.Where(x => x.InvoiceId == id).ToListAsync(ct);
        _db.InvoiceOperations.RemoveRange(ops);

        inv.IsDeleted = true;
        inv.DeletedAt = DateTime.UtcNow;
        inv.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "🗑️ اتحذفت الفاتورة" });
    }

    // ═══════════════ Helpers ═══════════════

    private static bool CanAdvance(string from, string to) => (from, to) switch
    {
        ("Draft", "Approved")   => true,
        ("Draft", "Issued")     => true,
        ("Approved", "Issued")  => true,
        ("Issued", "Sent")      => true,
        _                       => false
    };

    private static string Ar(string s) => s switch
    {
        "Draft" => "مسودة", "Approved" => "معتمدة", "Issued" => "مصدّرة", "Sent" => "مُرسلة",
        "Cancelled" => "ملغية", "Returned" => "مرتجعة", _ => s
    };

    private async Task<DateOnly?> DefaultDueDateAsync(int? termId, DateOnly from, CancellationToken ct)
    {
        if (termId is null) return null;
        var days = await _db.PaymentTerms.AsNoTracking()
            .Where(t => t.PaymentTermId == termId)
            .Select(t => (int?)t.DueDays).FirstOrDefaultAsync(ct);
        return days is null ? null : from.AddDays(days.Value);
    }

    private async Task<List<LineIn>> OriginalLinesNegatedAsync(long invoiceId, CancellationToken ct) =>
        await _db.InvoiceItems.AsNoTracking()
            .Where(x => x.InvoiceId == invoiceId)
            .OrderBy(x => x.InvoiceItemId)
            .Select(x => new LineIn(null, x.ServiceId, x.PriceRuleId, "مرتجع: " + x.Description,
                                    -x.Quantity, x.UnitPrice, -x.Discount, x.TaxRateId))
            .ToListAsync(ct);

    /// <summary>بيحط السطور + بينسخ نسبة الضريبة من TaxRates (Snapshot).</summary>
    private void AddLines(Invoice inv, List<LineIn> lines, Dictionary<int, decimal> rates)
    {
        foreach (var l in lines)
        {
            inv.InvoiceItems.Add(new InvoiceItem
            {
                OperationId = l.OperationId,
                ServiceId   = l.ServiceId,
                PriceRuleId = l.PriceRuleId,
                Description = (B(l.Description) ?? "بند")!,
                Quantity    = l.Quantity,
                UnitPrice   = l.UnitPrice,
                Discount    = l.Discount,
                TaxRateId   = l.TaxRateId,
                TaxRate     = l.TaxRateId is not null && rates.TryGetValue(l.TaxRateId.Value, out var r) ? r : 0
            });
        }
    }

    private async Task AddOperationLinksAsync(Invoice inv, InvoiceUpsert req, CancellationToken ct)
    {
        var ids = (req.OperationIds ?? new List<long>())
            .Union(req.Items?.Where(l => l.OperationId is not null)
                            .Select(l => l.OperationId!.Value) ?? Enumerable.Empty<long>())
            .Distinct().ToList();
        if (ids.Count == 0) return;

        var valid = await _db.Operations.AsNoTracking()
            .Where(o => ids.Contains(o.OperationId) && !o.IsDeleted && o.CustomerId == inv.CustomerId)
            .Select(o => o.OperationId).ToListAsync(ct);

        foreach (var oid in valid)
            inv.InvoiceOperations.Add(new InvoiceOperation
            {
                OperationId = oid,
                CreatedAt   = DateTime.UtcNow,
                CreatedBy   = CurrentUserId()
            });
    }

    /// <summary>بيحسب مجاميع الفاتورة من السطور — الأعمدة دي مش محسوبة في SQL Server.</summary>
    private static void RecalcTotals(Invoice inv, List<LineIn> lines, Dictionary<int, decimal> rates)
    {
        decimal Rate(LineIn l) => l.TaxRateId is not null && rates.TryGetValue(l.TaxRateId.Value, out var r) ? r : 0m;

        inv.SubTotal      = lines.Sum(l => l.Quantity * l.UnitPrice - l.Discount);
        inv.DiscountTotal = lines.Sum(l => l.Discount);
        inv.TaxTotal      = lines.Sum(l => (l.Quantity * l.UnitPrice - l.Discount) * Rate(l) / 100m);
        inv.GrandTotal    = inv.SubTotal + inv.TaxTotal;
    }

    private async Task<Dictionary<int, decimal>> RatesAsync(List<LineIn> lines, CancellationToken ct)
    {
        var ids = lines.Where(l => l.TaxRateId is not null)
                       .Select(l => l.TaxRateId!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, decimal>();
        return await _db.TaxRates.AsNoTracking()
            .Where(t => ids.Contains(t.TaxRateId) && t.IsActive)
            .ToDictionaryAsync(t => t.TaxRateId, t => t.Rate, ct);
    }

    private async Task<string?> ValidateAsync(InvoiceUpsert? r, bool isCredit,
        long? excludeInvoiceId, CancellationToken ct)
    {
        if (r is null) return "البيانات مش كاملة";
        if (r.CustomerId <= 0) return "اختار العميل";
        if (r.Items is null || r.Items.Count == 0) return "الفاتورة لازم يكون فيها بند واحد على الأقل";
        if (r.Items.Count > 200) return "بنود كتير — 200 بند أقصى حد";

        var cust = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == r.CustomerId && !c.IsDeleted, ct);
        if (cust is null) return "العميل مش موجود";

        foreach (var l in r.Items)
        {
            if (string.IsNullOrWhiteSpace(l.Description)) return "وصف البند مطلوب";
            if (l.Quantity == 0) return "الكمية مينفعش تكون صفر";
            if (isCredit && l.Quantity > 0) return "بنود الإشعار الدائن لازم تكون سالبة";
            if (l.UnitPrice < 0) return "سعر الوحدة مينفعش يكون سالب";
            if (isCredit)
            {
                if (l.Discount > 0) return "خصم الإشعار الدائن لازم يكون سالب زي الكمية";
                if (l.Discount < l.Quantity * l.UnitPrice)
                    return "الخصم أكبر من قيمة البند";
            }
            else
            {
                if (l.Discount < 0) return "الخصم مينفعش يكون سالب";
                if (l.Discount > l.Quantity * l.UnitPrice)
                    return "الخصم أكبر من قيمة البند";
            }

            if (l.TaxRateId is not null &&
                !await _db.TaxRates.AnyAsync(t => t.TaxRateId == l.TaxRateId && t.IsActive, ct))
                return "نسبة الضريبة مش موجودة";

            if (l.OperationId is not null)
            {
                var op = await _db.Operations.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OperationId == l.OperationId && !o.IsDeleted, ct);
                if (op is null) return "العملية مش موجودة";
                if (op.CustomerId != r.CustomerId)
                    return $"العملية {op.OperationNumber} لعميل تاني";
            }

            if (l.ServiceId is not null &&
                !await _db.Services.AnyAsync(s => s.ServiceId == l.ServiceId, ct))
                return "الخدمة مش موجودة";
        }

        // مانفوترش نفس العملية مرتين
        var opIds = r.Items.Where(l => l.OperationId is not null)
                           .Select(l => l.OperationId!.Value).Distinct().ToList();
        opIds.AddRange(r.OperationIds ?? new List<long>());
        opIds = opIds.Distinct().ToList();

        if (opIds.Count > 0)
        {
            var taken = await _db.InvoiceOperations.AsNoTracking()
                .Where(io => opIds.Contains(io.OperationId) &&
                             !io.Invoice.IsDeleted && io.Invoice.Status != "Cancelled" &&
                             (excludeInvoiceId == null || io.InvoiceId != excludeInvoiceId))
                .Select(io => io.Operation.OperationNumber).ToListAsync(ct);
            if (taken.Count > 0)
                return $"العمليات دي مفوترة قبل كده: {string.Join("، ", taken.Distinct())}";
        }

        return null;
    }
}
