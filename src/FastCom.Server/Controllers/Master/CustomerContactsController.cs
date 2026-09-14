using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// 📇 جهات اتصال العميل (<c>CustomerContacts</c>).
///
/// <para>
/// الجدول كان موجود من أول الـ schema — و<c>Bookings.ContactId</c> بيـ FK عليه —
/// و<c>api/options/contacts</c> بيغذّي دروبداون الحجز — بس **مافيش أي طريقة
/// تعمل جهة اتصال**. فالشاشة دي بتقفل الدائرة دي.
/// </para>
///
/// <para>
/// 🔐 الصلاحيات: <c>CUSTOMER.VIEW</c> للعرض · <c>CUSTOMER.EDIT</c> للتعديل —
/// عشان جهة الاتصال جزء من ملف العميل، مش وحدة مستقلة.
/// </para>
///
/// <para>
/// 🔴 الجدول **مافيش فيه <c>IsDeleted</c>** ⇒ الحذف فعلي.
/// عشان كده الحذف **ممنوع لو فيه حجوزات** مرتبطة بجهة الاتصال دي
/// (<c>Bookings.ContactId</c> FK من غير <c>ON DELETE</c>) — هنمنع الخطأ قبل ما يحصل.
/// </para>
/// </summary>
[ApiController]
[Route("api/customers/{customerId:int}/contacts")]
[Authorize]
public class CustomerContactsController : ControllerBase
{
    private readonly FastComDbContext _db;

    public CustomerContactsController(FastComDbContext db) => _db = db;

    /* ═══════════════ DTOs ═══════════════ */

    public record ContactDto(
        int CustomerContactId, int CustomerId, string ContactName,
        string? JobTitle, string? Phone, string? Mobile, string? Email,
        bool IsPrimary, bool CanReceiveInvoices, bool IsActive);

    public record UpsertRequest(
        string? ContactName, string? JobTitle, string? Phone,
        string? Mobile, string? Email,
        bool IsPrimary, bool CanReceiveInvoices, bool IsActive);

    /* ═══════════════ LIST ═══════════════ */

    [HttpGet]
    [Authorize(Policy = "PERM:CUSTOMER.VIEW")]
    public async Task<IActionResult> List(int customerId, CancellationToken ct)
    {
        if (!await CustomerExistsAsync(customerId, ct))
            return NotFound(new { message = "العميل مش موجود" });

        var rows = await _db.CustomerContacts.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .OrderByDescending(c => c.IsPrimary)
            .ThenByDescending(c => c.IsActive)
            .ThenBy(c => c.ContactName)
            .Select(c => new ContactDto(
                c.CustomerContactId, c.CustomerId, c.ContactName, c.JobTitle,
                c.Phone, c.Mobile, c.Email,
                c.IsPrimary, c.CanReceiveInvoices, c.IsActive))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /* ═══════════════ CREATE ═══════════════ */

    [HttpPost]
    [Authorize(Policy = "PERM:CUSTOMER.EDIT")]
    public async Task<IActionResult> Create(int customerId, [FromBody] UpsertRequest req, CancellationToken ct)
    {
        if (!await CustomerExistsAsync(customerId, ct))
            return NotFound(new { message = "العميل مش موجود" });

        /* 🔴 التحقق قبل الحفظ — نفس نمط باقي الكنترولرز:
              رسالة عربية واضحة بدل ما نسيب الـ SQL يرمي */
        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        var trimmed = Trim(req.ContactName)!;

        if (await _db.CustomerContacts.AnyAsync(
                c => c.CustomerId == customerId && c.ContactName == trimmed, ct))
            return BadRequest(new { message = $"فيه جهة اتصال بالاسم ده فعلًا: {trimmed}" });

        var c = new CustomerContact
        {
            CustomerId         = customerId,
            ContactName        = trimmed,
            JobTitle           = B(req.JobTitle),
            Phone              = B(req.Phone),
            Mobile             = B(req.Mobile),
            Email              = B(req.Email),
            IsPrimary          = req.IsPrimary,
            CanReceiveInvoices = req.CanReceiveInvoices,
            IsActive           = req.IsActive,
            CreatedAt          = DateTime.UtcNow,
            CreatedBy          = CurrentUserId()
        };

        /* 🔴 أول جهة اتصال للعميل تبقى **رئيسية** أوتوماتيك — عشان
              دروبداون الحجز (بيـ OrderByDescending على IsPrimary) يلاقي حاجة */
        var isFirst = !await _db.CustomerContacts.AnyAsync(x => x.CustomerId == customerId, ct);
        if (isFirst) c.IsPrimary = true;

        _db.CustomerContacts.Add(c);

        /* 🔴 «رئيسية» = واحدة بس لكل عميل — ننزل الباقي */
        if (c.IsPrimary) await ClearPrimaryAsync(customerId, null, ct);

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            message = $"✅ اتضافت جهة الاتصال: {c.ContactName}",
            id      = c.CustomerContactId
        });
    }

    /* ═══════════════ UPDATE ═══════════════ */

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:CUSTOMER.EDIT")]
    public async Task<IActionResult> Update(int customerId, int id, [FromBody] UpsertRequest req, CancellationToken ct)
    {
        var c = await _db.CustomerContacts
            .FirstOrDefaultAsync(x => x.CustomerContactId == id && x.CustomerId == customerId, ct);
        if (c is null) return NotFound(new { message = "جهة الاتصال مش موجودة" });

        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        var trimmed = Trim(req.ContactName)!;

        if (await _db.CustomerContacts.AnyAsync(
                x => x.CustomerId == customerId && x.ContactName == trimmed
                     && x.CustomerContactId != id, ct))
            return BadRequest(new { message = $"فيه جهة اتصال تانية بالاسم ده: {trimmed}" });

        c.ContactName        = trimmed;
        c.JobTitle           = B(req.JobTitle);
        c.Phone              = B(req.Phone);
        c.Mobile             = B(req.Mobile);
        c.Email              = B(req.Email);
        c.CanReceiveInvoices = req.CanReceiveInvoices;
        c.IsActive           = req.IsActive;
        c.UpdatedAt          = DateTime.UtcNow;
        c.UpdatedBy          = CurrentUserId();

        /* 🔴 لو بقى رئيسي → ننزل الباقي. ولو كان رئيسي وبطل →
              لازم يفضل فيه رئيسي واحد، فنرقّي أول واحد تاني */
        if (req.IsPrimary && !c.IsPrimary)
        {
            await ClearPrimaryAsync(customerId, id, ct);
            c.IsPrimary = true;
        }
        else if (!req.IsPrimary && c.IsPrimary)
        {
            var next = await _db.CustomerContacts.AsNoTracking()
                .Where(x => x.CustomerId == customerId && x.CustomerContactId != id)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.CustomerContactId)
                .FirstOrDefaultAsync(ct);

            if (next is null)
                return BadRequest(new { message = "دي جهة الاتصال الوحيدة — لازم تفضل رئيسية" });

            c.IsPrimary = false;
            var n = await _db.CustomerContacts.FirstAsync(x => x.CustomerContactId == next.CustomerContactId, ct);
            n.IsPrimary = true;
            n.UpdatedAt = DateTime.UtcNow;
            n.UpdatedBy = CurrentUserId();
        }

        /* 🔴 جهة اتصال غير نشطة ماينفعش تبقى رئيسية
              (قبل SaveChanges — فمافيش حاجة اتسجلت لو رفضنا) */
        if (!c.IsActive && c.IsPrimary)
            return BadRequest(new { message = "جهة الاتصال الرئيسية لازم تكون نشطة" });

        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"✅ اتحدّثت: {c.ContactName}" });
    }

    /* ═══════════════ DELETE (فعلي — مافيش IsDeleted) ═══════════════ */

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:CUSTOMER.DELETE")]
    public async Task<IActionResult> Delete(int customerId, int id, CancellationToken ct)
    {
        var c = await _db.CustomerContacts
            .FirstOrDefaultAsync(x => x.CustomerContactId == id && x.CustomerId == customerId, ct);
        if (c is null) return NotFound(new { message = "جهة الاتصال مش موجودة" });

        /* 🔴 مافيش IsDeleted في الجدول ⇒ الحذف نهائي.
              Bookings.ContactId FK من غير ON DELETE ⇒ SQL هيرمي.
              نمنعها برسالة واضحة بدل exception. */
        var used = await _db.Bookings.CountAsync(b => b.ContactId == id && !b.IsDeleted, ct);
        if (used > 0)
            return BadRequest(new
            {
                message = $"مش ممكن تحذفها — مرتبطة بـ {used} حجز. " +
                          "خلّيها «غير نشطة» بدل الحذف."
            });

        var wasPrimary = c.IsPrimary;
        _db.CustomerContacts.Remove(c);

        /* 🔴 لو الرئيسية اتحذفت → نرقّي أول واحدة تانية */
        if (wasPrimary)
        {
            var next = await _db.CustomerContacts.AsNoTracking()
                .Where(x => x.CustomerId == customerId && x.CustomerContactId != id)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.CustomerContactId)
                .FirstOrDefaultAsync(ct);

            if (next is not null)
            {
                var n = await _db.CustomerContacts.FirstAsync(x => x.CustomerContactId == next.CustomerContactId, ct);
                n.IsPrimary = true;
                n.UpdatedAt = DateTime.UtcNow;
                n.UpdatedBy = CurrentUserId();
            }
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"✅ اتحذفت: {c.ContactName}" });
    }

    /* ═══════════════ helpers ═══════════════ */

    /// <summary>🔴 رئيسية واحدة بس لكل عميل — <paramref name="exceptId"/> مايتنزلش.</summary>
    private async Task ClearPrimaryAsync(int customerId, int? exceptId, CancellationToken ct)
    {
        var others = await _db.CustomerContacts
            .Where(x => x.CustomerId == customerId && x.IsPrimary
                        && (exceptId == null || x.CustomerContactId != exceptId))
            .ToListAsync(ct);

        foreach (var o in others)
        {
            o.IsPrimary = false;
            o.UpdatedAt = DateTime.UtcNow;
            o.UpdatedBy = CurrentUserId();
        }
    }

    private async Task<bool> CustomerExistsAsync(int id, CancellationToken ct) =>
        await _db.Customers.AnyAsync(c => c.CustomerId == id && !c.IsDeleted, ct);

    private static string? Validate(UpsertRequest r)
    {
        var name = Trim(r.ContactName);
        if (string.IsNullOrWhiteSpace(name))       return "اسم جهة الاتصال مطلوب";
        if (name.Length > 150)                     return "الاسم أطول من 150 حرف";
        if (r.JobTitle is { Length: > 100 })       return "المسمى الوظيفي أطول من 100 حرف";
        if (r.Phone    is { Length: > 50 })        return "رقم التليفون أطول من 50 حرف";
        if (r.Mobile   is { Length: > 50 })        return "رقم الموبايل أطول من 50 حرف";
        if (r.Email    is { Length: > 254 })       return "البريد الإلكتروني أطول من 254 حرف";
        if (!string.IsNullOrWhiteSpace(r.Email) && !r.Email.Contains('@'))
            return "البريد الإلكتروني شكله مش صحيح";
        if (string.IsNullOrWhiteSpace(r.Phone) && string.IsNullOrWhiteSpace(r.Mobile))
            return "لازم تليفون أو موبايل على الأقل";
        return null;
    }

    private static string Trim(string? s) => s?.Trim() ?? "";

    /// <summary>Empty → <c>null</c> (الأعمدة nullable في الـ schema).</summary>
    private static string? B(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private int? CurrentUserId()
    {
        var v = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return int.TryParse(v, out var id) ? id : null;
    }
}
