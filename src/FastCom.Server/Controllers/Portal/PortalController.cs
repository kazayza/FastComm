using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Portal;

/// <summary>
/// 🔗 بوابة العميل — العميل بيفتح فواتيره وإيصالاته من رابط من غير ما يسجّل دخول.
///
/// <para>الأمان: الرابط فيه <c>token</c> عشوائي 256-بت، واللي بيتخزّن في
/// <c>CustomerPortalTokens.TokenHash</c> هو <b>SHA-256</b> بتاعه — فلو القاعدة
/// اتسرقت محدش يقدر يبني رابط صالح.</para>
///
/// <para>كل نقطة عامة بتتحقق من التوكن من جديد ( Stateless)، وبتسجّل في
/// <c>PortalAccessLogs</c>.</para>
/// </summary>
[ApiController]
[Route("api/portal")]
public class PortalController : ControllerBase
{
    private readonly FastComDbContext _db;

    public PortalController(FastComDbContext db) => _db = db;

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return int.TryParse(raw, out var v) ? v : null;
    }

    private static string Sha256(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static string B(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null! : s.Trim();

    /// <summary>Base64 عادي بيحتوي <c>+ / =</c> — مش صالح في URL.</summary>
    private static string UrlSafe(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ══════════════════════════════════════════════════════════════════
    //  🔓 نقاط عامة (بالرابط)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// بيتحقق من الرابط ويرجّع بيانات العميل.
    /// <para>بيزود <c>UsedCount</c> ويختم <c>LastUsedAt</c> — أول مرة بس،
    /// وباقي الطلبات في نفس الجلسة بتعدّي من غير عدّ.</para>
    /// </summary>
    [HttpGet("verify")]
    [AllowAnonymous]
    public async Task<IActionResult> Verify([FromQuery] string? token, CancellationToken ct)
    {
        var (t, cust, err) = await ResolveAsync(token, ct);
        if (t is null) return BadRequest(new { message = err });
        if (cust is null) return BadRequest(new { message = err });

        // أول استخدام للتوكن ده هو اللي بيُعدّ
        if (t.UsedCount == 0)
        {
            t.UsedCount  = 1;
            t.LastUsedAt = DateTime.UtcNow;
            await LogAsync(t, cust.CustomerId, "Login", null, null, ct);
            await _db.SaveChangesAsync(ct);
        }

        var open = await _db.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == cust.CustomerId && !i.IsDeleted
                        && i.Status != "Draft" && i.Status != "Cancelled")
            .SumAsync(i => (decimal?)i.GrandTotal - i.PaidAmount, ct) ?? 0m;

        return Ok(new VerifyOut(
            cust.CustomerId, cust.NameAr, cust.CustomerCode,
            cust.Phone, t.Purpose, t.ExpiresAt,
            open < 0 ? 0m : open));
    }

    /// <summary>فاتورة واحدة بتفاصيلها.</summary>
    [HttpGet("invoice")]
    [AllowAnonymous]
    public async Task<IActionResult> Invoice([FromQuery] string? token, [FromQuery] long invoiceId,
                                             CancellationToken ct)
    {
        var (t, cust, err) = await ResolveAsync(token, ct);
        if (t is null) return BadRequest(new { message = err });
        if (cust is null) return BadRequest(new { message = err });
        if (t.Purpose == "InvoiceView" && t.InvoiceId != invoiceId)
            return Forbid();

        var inv = await _db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId && i.CustomerId == cust.CustomerId
                                      && !i.IsDeleted && i.Status != "Draft", ct);
        if (inv is null) return NotFound(new { message = "الفاتورة مش موجودة" });

        var items = await _db.InvoiceItems.AsNoTracking()
            .Where(l => l.InvoiceId == invoiceId)
            .OrderBy(l => l.InvoiceItemId)
            .Select(l => new PortalLine(l.InvoiceItemId, l.Description, l.Quantity, l.UnitPrice,
                                        l.Discount, l.TaxRate, l.LineSubtotal ?? 0m,
                                        l.LineTax ?? 0m, l.LineTotal ?? 0m))
            .ToListAsync(ct);

        var allocs = await _db.PaymentAllocations.AsNoTracking()
            .Where(a => a.InvoiceId == invoiceId && a.Payment!.Status != "Cancelled")
            .OrderBy(a => a.Payment!.PaymentDate)
            .Select(a => new PortalAlloc(a.Payment!.PaymentNumber, a.Payment.PaymentDate,
                                         a.Payment.PaymentMethod!.NameAr, a.AllocatedAmount))
            .ToListAsync(ct);

        await LogAsync(t, cust.CustomerId, "View", "Invoice", invoiceId, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new PortalInvoice(
            inv.InvoiceNumber, inv.DocumentTypeCode, inv.InvoiceType,
            inv.InvoiceDate, inv.DueDate, inv.Status, inv.PaymentStatus,
            inv.SubTotal, inv.DiscountTotal, inv.TaxTotal, inv.GrandTotal,
            inv.PaidAmount, inv.GrandTotal - inv.PaidAmount, inv.CurrencyCode, inv.Notes,
            cust.NameAr, cust.CustomerCode, cust.Phone, items, allocs));
    }

    /// <summary>قائمة فواتير العميل — المُصدّرة والمُرسلة بس.</summary>
    [HttpGet("invoices")]
    [AllowAnonymous]
    public async Task<IActionResult> Invoices([FromQuery] string? token,
                                              [FromQuery] bool unpaidOnly = false,
                                              CancellationToken ct = default)
    {
        var (t, cust, err) = await ResolveAsync(token, ct);
        if (t is null) return BadRequest(new { message = err });
        if (cust is null) return BadRequest(new { message = err });

        var q = _db.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == cust.CustomerId && !i.IsDeleted
                        && (i.Status == "Issued" || i.Status == "Sent" || i.Status == "Approved"
                            || i.Status == "Returned"));
        if (unpaidOnly) q = q.Where(i => i.GrandTotal - i.PaidAmount > 0);

        var list = await q.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.InvoiceId)
            .Take(200)
            .Select(i => new PortalInvoiceRow(
                i.InvoiceId, i.InvoiceNumber, i.DocumentTypeCode, i.InvoiceDate, i.DueDate,
                i.GrandTotal, i.PaidAmount, i.GrandTotal - i.PaidAmount,
                i.Status, i.PaymentStatus, i.CurrencyCode))
            .ToListAsync(ct);

        await LogAsync(t, cust.CustomerId, "View", "InvoiceList", null, ct);
        await _db.SaveChangesAsync(ct);
        return Ok(list);
    }

    /// <summary>كشف حساب العميل من خلال البوابة.</summary>
    [HttpGet("statement")]
    [AllowAnonymous]
    public async Task<IActionResult> Statement([FromQuery] string? token,
                                               [FromQuery] string? from, [FromQuery] string? to,
                                               CancellationToken ct)
    {
        var (t, cust, err) = await ResolveAsync(token, ct);
        if (t is null) return BadRequest(new { message = err });
        if (cust is null) return BadRequest(new { message = err });

        var f = DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today.AddMonths(-1));
        var tt = DateOnly.TryParse(to, out var td) ? td : DateOnly.FromDateTime(DateTime.Today);
        if (tt < f) (f, tt) = (tt, f);

        var id = cust.CustomerId;
        var rows = new List<PortalStmtLine>();

        // رصيد افتتاحي = كل اللي قبل from
        var openInv = await _db.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == id && !i.IsDeleted && i.InvoiceDate < f
                        && i.Status != "Draft" && i.Status != "Cancelled")
            .SumAsync(i => (decimal?)i.GrandTotal, ct) ?? 0m;
        var openPay = await _db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == id && p.Status == "Posted" && p.PaymentDate < f)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var opening = openInv - openPay;

        rows.AddRange(await _db.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == id && !i.IsDeleted && i.InvoiceDate >= f && i.InvoiceDate <= tt
                        && i.Status != "Draft" && i.Status != "Cancelled")
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceId)
            .Select(i => new PortalStmtLine(i.InvoiceDate, i.DocumentTypeCode == "CreditNote" ? "CreditNote" : "Invoice",
                                            i.InvoiceNumber, i.Notes ?? "", i.GrandTotal, 0m, 0m))
            .ToListAsync(ct));

        rows.AddRange(await _db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == id && p.Status == "Posted" && p.PaymentDate >= f && p.PaymentDate <= tt)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.PaymentId)
            .Select(p => new PortalStmtLine(p.PaymentDate, "Payment", p.PaymentNumber,
                                            p.Notes ?? "", 0m, p.Amount, 0m))
            .ToListAsync(ct));

        rows = rows.OrderBy(r => r.Date).ThenBy(r => r.Ref).ToList();
        var bal = opening;
        var outRows = new List<PortalStmtLine>(rows.Count);
        foreach (var r in rows)
        {
            bal += r.Debit - r.Credit;
            outRows.Add(r with { Balance = bal });
        }

        await LogAsync(t, id, "View", "Statement", null, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new PortalStatement(cust.NameAr, opening, outRows,
            outRows.Sum(r => r.Debit), outRows.Sum(r => r.Credit), bal));
    }

    // ══════════════════════════════════════════════════════════════════
    //  🔒 إدارة الروابط (من داخل النظام)
    // ══════════════════════════════════════════════════════════════════

    public record CreateTokenRequest(int? CustomerId, string? Purpose, long? InvoiceId,
                                     int? ExpiresInDays, int? MaxUses);

    /// <summary>
    /// بينشئ رابط للعميل. 🔴 التوكن الخام بيُرجع <b>مرة واحدة</b> هنا بس —
    /// اللي بيتخزّن في القاعدة هو الـ hash.
    /// </summary>
    [HttpPost("tokens")]
    [Authorize(Policy = "PERM:CUSTOMER.PORTAL_TOKEN")]
    public async Task<IActionResult> CreateToken([FromBody] CreateTokenRequest? req, CancellationToken ct)
    {
        var customerId = req?.CustomerId ?? 0;
        if (customerId <= 0) return BadRequest(new { message = "اختر العميل" });

        var cust = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId && !c.IsDeleted, ct);
        if (cust is null) return NotFound(new { message = "العميل مش موجود" });
        if (!cust.PortalEnabled)
            return BadRequest(new { message = $"البوابة مش مفعّلة للعميل {cust.NameAr} — فعّلها الأول من بيانات العميل" });

        var purpose = B(req?.Purpose) ?? "Portal";
        if (purpose is not ("Portal" or "InvoiceView" or "Statement"))
            return BadRequest(new { message = "الغرض لازم يكون Portal أو InvoiceView أو Statement" });

        long? invoiceId = req?.InvoiceId;
        if (purpose == "InvoiceView")
        {
            if (invoiceId is null) return BadRequest(new { message = "اختر الفاتورة" });
            var inv = await _db.Invoices.FirstOrDefaultAsync(
                i => i.InvoiceId == invoiceId && i.CustomerId == customerId && !i.IsDeleted, ct);
            if (inv is null) return BadRequest(new { message = "الفاتورة مش بتاعت العميل ده" });
        }
        else invoiceId = null;

        var days = req?.ExpiresInDays ?? 7;
        if (days is < 1 or > 365) return BadRequest(new { message = "مدة الصلاحية من يوم لـ 365 يوم" });

        var maxUses = req?.MaxUses;
        if (maxUses is < 1) return BadRequest(new { message = "عدد مرات الاستخدام لازم يكون 1 أو أكتر" });
        if (maxUses > 1000) return BadRequest(new { message = "عدد مرات الاستخدام ميتعداش 1000" });

        // توليد التوكن
        var raw = UrlSafe(RandomNumberGenerator.GetBytes(32));
        _db.CustomerPortalTokens.Add(new CustomerPortalToken
        {
            CustomerId = customerId,
            TokenHash  = Sha256(raw),
            Purpose    = purpose,
            InvoiceId  = invoiceId,
            ExpiresAt  = DateTime.UtcNow.AddDays(days),
            MaxUses    = maxUses,
            UsedCount  = 0,
            IsActive   = true,
            CreatedAt  = DateTime.UtcNow,
            CreatedBy  = CurrentUserId()
        });
        await _db.SaveChangesAsync(ct);
        var saved = await _db.CustomerPortalTokens.OrderByDescending(x => x.PortalTokenId).FirstAsync(ct);

        return Ok(new NewToken(saved.PortalTokenId, raw, $"/portal/{raw}", saved.ExpiresAt));
    }

    /// <summary>روابط العميل.</summary>
    [HttpGet("tokens")]
    [Authorize(Policy = "PERM:CUSTOMER.PORTAL_TOKEN")]
    public async Task<IActionResult> Tokens([FromQuery] int customerId, CancellationToken ct)
    {
        if (customerId <= 0) return BadRequest(new { message = "اختر العميل" });
        var list = await _db.CustomerPortalTokens.AsNoTracking()
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.CreatedAt).Take(100)
            .Select(t => new TokenRow(
                t.PortalTokenId, t.Purpose, t.InvoiceId, t.CreatedAt, t.ExpiresAt,
                t.MaxUses, t.UsedCount, t.LastUsedAt, t.IsActive, t.RevokedAt))
            .ToListAsync(ct);
        return Ok(list);
    }

    /// <summary>إبطال رابط.</summary>
    [HttpPost("tokens/{id:long}/revoke")]
    [Authorize(Policy = "PERM:CUSTOMER.PORTAL_TOKEN")]
    public async Task<IActionResult> Revoke(long id, CancellationToken ct)
    {
        var t = await _db.CustomerPortalTokens.FirstOrDefaultAsync(x => x.PortalTokenId == id, ct);
        if (t is null) return NotFound(new { message = "الرابط مش موجود" });
        if (!t.IsActive) return BadRequest(new { message = "الرابط مُبطل بالفعل" });

        t.IsActive  = false;
        t.RevokedAt = DateTime.UtcNow;
        t.RevokedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ الرابط اتبطل" });
    }

    /// <summary>سجل دخول العميل من البوابة.</summary>
    [HttpGet("tokens/{id:long}/logs")]
    [Authorize(Policy = "PERM:CUSTOMER.PORTAL_TOKEN")]
    public async Task<IActionResult> Logs(long id, CancellationToken ct)
    {
        var list = await _db.PortalAccessLogs.AsNoTracking()
            .Where(l => l.PortalTokenId == id)
            .OrderByDescending(l => l.AccessedAt).Take(200)
            .Select(l => new LogRow(l.AccessedAt, l.Action, l.EntityType, l.EntityId, l.IpAddress))
            .ToListAsync(ct);
        return Ok(list);
    }

    // ══════════════════════════════════════════════════════════════════
    //  داخلي
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// بيفكّ التوكن ويتحقق منه: موجود · نشط · مش مُبطل · لسه صالح ·
    /// العميل مفعّل البوابة · ماوصلش لحد الاستخدام.
    /// </summary>
    private async Task<(CustomerPortalToken? Token, Customer? Customer, string Error)>
        ResolveAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, null, "الرابط ناقص");

        var raw = token.Trim();
        if (raw.Length < 20 || raw.Length > 128)
            return (null, null, "الرابط غير صالح");

        var hash = Sha256(raw);
        var t = await _db.CustomerPortalTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (t is null) return (null, null, "الرابط غير صالح");

        if (!t.IsActive || t.RevokedAt is not null)
            return (null, null, "الرابط اتبطل");

        if (t.ExpiresAt is not null && t.ExpiresAt < DateTime.UtcNow)
            return (null, null, "الرابط خلصت صلاحيته");

        if (t.MaxUses is not null && t.UsedCount >= t.MaxUses)
            return (null, null, "الرابط اتستخدم العدد المسموح بيه");

        var cust = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == t.CustomerId && !c.IsDeleted, ct);
        if (cust is null) return (null, null, "العميل مش موجود");
        if (!cust.PortalEnabled)
        {
            _db.PortalAccessLogs.Add(new PortalAccessLog
            {
                CustomerId    = cust.CustomerId,
                PortalTokenId = t.PortalTokenId,
                AccessedAt    = DateTime.UtcNow,
                IpAddress     = Clip(HttpContext.Connection.RemoteIpAddress?.ToString(), 64),
                UserAgent     = Clip(HttpContext.Request.Headers.UserAgent.ToString(), 500),
                Action        = "Denied"
            });
            await _db.SaveChangesAsync(ct);
            return (null, null, "البوابة مش مفعّلة لهذا العميل");
        }

        return (t, cust, "");
    }

    /// <summary>بيسجّل في <c>PortalAccessLogs</c> — مابيوقفش الطلب لو فشل.</summary>
    private async Task LogAsync(CustomerPortalToken? t, int customerId, string action,
                                string? entityType, long? entityId, CancellationToken ct)
    {
        try
        {
            _db.PortalAccessLogs.Add(new PortalAccessLog
            {
                CustomerId    = customerId,
                PortalTokenId = t?.PortalTokenId,
                AccessedAt    = DateTime.UtcNow,
                IpAddress     = Clip(HttpContext.Connection.RemoteIpAddress?.ToString(), 64),
                UserAgent     = Clip(HttpContext.Request.Headers.UserAgent.ToString(), 500),
                EntityType    = entityType,
                EntityId      = entityId,
                Action        = action
            });
            await Task.CompletedTask;
        }
        catch { /* السجل مش أهم من الخدمة */ }
    }

    private static string? Clip(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s : s[..max]);

    // ══════════════════════════════════════════════════════════════════
    //  DTOs
    // ══════════════════════════════════════════════════════════════════

    public record VerifyOut(int CustomerId, string CustomerName, string CustomerCode,
                            string? Phone, string Purpose, DateTime? ExpiresAt, decimal Outstanding);

    public record PortalLine(long InvoiceItemId, string Description, decimal Quantity, decimal UnitPrice,
                             decimal Discount, decimal TaxRate, decimal LineSubtotal,
                             decimal LineTax, decimal LineTotal);

    public record PortalAlloc(string PaymentNumber, DateOnly PaymentDate, string? MethodName,
                              decimal AllocatedAmount);

    public record PortalInvoice(string InvoiceNumber, string DocumentTypeCode, string InvoiceType,
                                DateOnly InvoiceDate, DateOnly? DueDate, string Status,
                                string PaymentStatus, decimal SubTotal, decimal DiscountTotal,
                                decimal TaxTotal, decimal GrandTotal, decimal PaidAmount, decimal Due,
                                string CurrencyCode, string? Notes, string CustomerName,
                                string CustomerCode, string? CustomerPhone,
                                List<PortalLine> Items, List<PortalAlloc> Allocations);

    public record PortalInvoiceRow(long InvoiceId, string InvoiceNumber, string DocumentTypeCode,
                                   DateOnly InvoiceDate, DateOnly? DueDate, decimal GrandTotal,
                                   decimal PaidAmount, decimal Due, string Status,
                                   string PaymentStatus, string CurrencyCode);

    public record PortalStmtLine(DateOnly Date, string Kind, string Ref, string Description,
                                 decimal Debit, decimal Credit, decimal Balance);

    public record PortalStatement(string CustomerName, decimal Opening, List<PortalStmtLine> Lines,
                                  decimal TotalDebit, decimal TotalCredit, decimal Closing);

    public record NewToken(long PortalTokenId, string Token, string Path, DateTime? ExpiresAt);

    public record TokenRow(long PortalTokenId, string Purpose, long? InvoiceId, DateTime CreatedAt,
                           DateTime? ExpiresAt, int? MaxUses, int UsedCount, DateTime? LastUsedAt,
                           bool IsActive, DateTime? RevokedAt);

    public record LogRow(DateTime AccessedAt, string Action, string? EntityType, long? EntityId,
                         string? IpAddress);
}
