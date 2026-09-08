using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers;

/// <summary>
/// ⚙️ الإعدادات — بيانات الشركة (<c>CompanyProfile</c>) + المفاتيح العامة (<c>SystemSettings</c>).
///
/// <para>🔴 <b>D11:</b> بيانات الشركة في <c>CompanyProfile</c> بس — عمرها ما تيجي من <c>SystemSettings</c>.</para>
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly IPermissionService _perms;
    private readonly IWebHostEnvironment _env;

    public SettingsController(FastComDbContext db, IPermissionService perms, IWebHostEnvironment env)
    {
        _db    = db;
        _perms = perms;
        _env   = env;
    }

    private static readonly string[] ValueTypes = { "String", "Int", "Bool", "Decimal", "Json", "Date" };

    /// <summary>
    /// 🔴 مافيش <c>.svg</c> — الـ SVG ممكن يبقى فيه JavaScript، وأي حد يفتح الرابط
    ///    على طول هيتنفذ عنده على نفس الـ domain (Stored XSS).
    /// </summary>
    private static readonly string[] LogoExts = { ".png", ".jpg", ".jpeg", ".webp" };

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private async Task<bool> CanManage(CancellationToken ct) =>
        await _perms.HasAsync(CurrentUserId(), "SETTINGS.MANAGE", ct);

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ══════════════════════════════════════════════════════════════════
    //  1) بيانات الشركة
    // ══════════════════════════════════════════════════════════════════

    [HttpGet("company")]
    [Authorize(Policy = "PERM:SETTINGS.VIEW")]
    public async Task<IActionResult> GetCompany(CancellationToken ct)
    {
        var p = await _db.CompanyProfiles.AsNoTracking()
            .OrderBy(x => x.CompanyProfileId)
            .FirstOrDefaultAsync(ct);

        if (p is null) return NotFound(new { message = "مافيش بيانات شركة — كلم مدير النظام" });

        return Ok(new ProfileOut(
            p.CompanyProfileId, p.LegalNameAr, p.LegalNameEn, p.TradeNameAr, p.TradeNameEn,
            p.TaxNumber, p.VatRegistrationNumber, p.CommercialRegister,
            p.AddressAr, p.AddressEn, p.CityAr, p.CityEn,
            p.Phone, p.Mobile, p.Email, p.Website,
            p.LogoPath, p.LogoDarkPath, p.StampPath,
            p.InvoiceFooterNoteAr, p.InvoiceFooterNoteEn,
            p.DefaultCurrencyCode, p.DefaultTaxRateId, p.FiscalYearStartMonth,
            p.UpdatedAt?.ToString("yyyy-MM-dd HH:mm")));
    }

    [HttpPut("company")]
    public async Task<IActionResult> UpdateCompany([FromBody] ProfileIn req, CancellationToken ct)
    {
        if (!await CanManage(ct)) return Forbid();

        var p = await _db.CompanyProfiles
            .OrderBy(x => x.CompanyProfileId)
            .FirstOrDefaultAsync(ct);
        if (p is null) return NotFound(new { message = "مافيش بيانات شركة" });

        if (string.IsNullOrWhiteSpace(req.LegalNameAr))
            return BadRequest(new { message = "الاسم القانوني مطلوب" });
        if (req.LegalNameAr.Trim().Length > 200)
            return BadRequest(new { message = "الاسم القانوني طويل — 200 حرف أقصى حد" });
        if (string.IsNullOrWhiteSpace(req.TaxNumber))
            return BadRequest(new { message = "الرقم الضريبي مطلوب" });
        if (string.IsNullOrWhiteSpace(req.AddressAr))
            return BadRequest(new { message = "العنوان مطلوب" });

        var cur = (B(req.CurrencyCode) ?? "EGP").ToUpperInvariant();
        if (cur.Length != 3)
            return BadRequest(new { message = "كود العملة 3 حروف — زي EGP" });

        if (req.DefaultTaxRateId is not null &&
            !await _db.TaxRates.AnyAsync(t => t.TaxRateId == req.DefaultTaxRateId && t.IsActive, ct))
            return BadRequest(new { message = "نسبة الضريبة الافتراضية مش موجودة" });

        if (req.FiscalYearStartMonth is < 1 or > 12)
            return BadRequest(new { message = "شهر بداية السنة المالية من 1 لـ 12" });

        p.LegalNameAr          = req.LegalNameAr.Trim();
        p.LegalNameEn          = B(req.LegalNameEn);
        p.TradeNameAr          = B(req.TradeNameAr);
        p.TradeNameEn          = B(req.TradeNameEn);
        p.TaxNumber            = req.TaxNumber.Trim();
        p.VatRegistrationNumber = B(req.VatRegistrationNumber);
        p.CommercialRegister   = B(req.CommercialRegister);
        p.AddressAr            = req.AddressAr.Trim();
        p.AddressEn            = B(req.AddressEn);
        p.CityAr               = B(req.CityAr);
        p.CityEn               = B(req.CityEn);
        p.Phone                = B(req.Phone);
        p.Mobile               = B(req.Mobile);
        p.Email                = B(req.Email);
        p.Website              = B(req.Website);
        p.InvoiceFooterNoteAr  = B(req.InvoiceFooterNoteAr);
        p.InvoiceFooterNoteEn  = B(req.InvoiceFooterNoteEn);
        p.DefaultCurrencyCode  = cur;
        p.DefaultTaxRateId     = req.DefaultTaxRateId;
        p.FiscalYearStartMonth = req.FiscalYearStartMonth;
        p.UpdatedAt            = DateTime.UtcNow;
        p.UpdatedBy            = CurrentUserId();

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ بيانات الشركة اتحدّثت" });
    }

    /// <summary>رفع اللوجو / الختم — بيخزّن تحت <c>wwwroot/App_Data/company</c>.</summary>
    [HttpPost("company/logo")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo([FromForm] IFormFile? file,
        [FromForm] string? kind, CancellationToken ct)
    {
        if (!await CanManage(ct)) return Forbid();
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "مافيش ملف" });
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "الملف أكبر من 5 ميجا" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!LogoExts.Contains(ext))
            return BadRequest(new { message = "المسموح: png · jpg · jpeg · webp" });

        var slot = kind?.Trim().ToLowerInvariant() switch
        {
            "dark"  => "dark",
            "stamp" => "stamp",
            _       => "main"
        };

        // 🔴 اللوجو **لازم** يبقى في مسار عام — الـ Header بيعرضه بـ <img>
        //    قبل ما المستخدم يسجّل دخول. عشان كده مش تحت App_Data.
        var dir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "company");
        Directory.CreateDirectory(dir);

        // اسم الملف من عندنا إحنا — مش من اسم ملف المستخدم
        var name = $"{slot}-{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
        var full = Path.Combine(dir, name);
        await using (var fs = System.IO.File.Create(full))
            await file.CopyToAsync(fs, ct);

        var rel = $"/uploads/company/{name}";

        var p = await _db.CompanyProfiles
            .OrderBy(x => x.CompanyProfileId).FirstOrDefaultAsync(ct);
        if (p is not null)
        {
            if (slot == "dark")      p.LogoDarkPath = rel;
            else if (slot == "stamp") p.StampPath   = rel;
            else                      p.LogoPath    = rel;
            p.UpdatedAt = DateTime.UtcNow;
            p.UpdatedBy = CurrentUserId();
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new { path = rel, message = "✅ الصورة اتحفظة" });
    }

    // ══════════════════════════════════════════════════════════════════
    //  2) المفاتيح العامة
    // ══════════════════════════════════════════════════════════════════

    [HttpGet]
    [Authorize(Policy = "PERM:SETTINGS.VIEW")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _db.SystemSettings.AsNoTracking()
            .OrderBy(s => s.SettingKey)
            .Select(s => new SettingOut(s.SettingId, s.SettingKey, s.SettingValue,
                s.ValueType, s.Description, s.IsSystem,
                s.UpdatedAt == null ? null : s.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")))
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SettingIn req, CancellationToken ct)
    {
        if (!await CanManage(ct)) return Forbid();

        var key = B(req.SettingKey);
        if (key is null) return BadRequest(new { message = "اسم المفتاح مطلوب" });
        if (key.Length > 100) return BadRequest(new { message = "اسم المفتاح طويل — 100 حرف أقصى حد" });
        if (!key.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or ':'))
            return BadRequest(new { message = "اسم المفتاح: حروف وأرقام ونقطة وشرطة سفلية بس" });

        var vt = B(req.ValueType) ?? "String";
        if (!ValueTypes.Contains(vt))
            return BadRequest(new { message = $"النوع لازم يكون واحد من: {string.Join(" · ", ValueTypes)}" });

        if (await _db.SystemSettings.AnyAsync(s => s.SettingKey == key, ct))
            return BadRequest(new { message = "المفتاح ده موجود بالفعل" });

        var verr = CheckValue(req.SettingValue, vt);
        if (verr is not null) return BadRequest(new { message = verr });

        _db.SystemSettings.Add(new SystemSetting
        {
            SettingKey   = key,
            SettingValue = B(req.SettingValue),
            ValueType    = vt,
            Description  = B(req.Description),
            IsSystem     = false,
            UpdatedAt    = DateTime.UtcNow,
            UpdatedBy    = CurrentUserId()
        });
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ المفتاح {key} اتضاف" });
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] SettingIn req, CancellationToken ct)
    {
        if (!await CanManage(ct)) return Forbid();

        var s = await _db.SystemSettings.FirstOrDefaultAsync(x => x.SettingKey == key, ct);
        if (s is null) return NotFound(new { message = "المفتاح مش موجود" });

        var verr = CheckValue(req.SettingValue, s.ValueType);
        if (verr is not null) return BadRequest(new { message = verr });

        s.SettingValue = B(req.SettingValue);
        s.Description  = B(req.Description) ?? s.Description;
        s.UpdatedAt    = DateTime.UtcNow;
        s.UpdatedBy    = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"✅ {key} اتحدّث" });
    }

    /// <summary>حفظ مجموعة مفاتيح مرة واحدة (زر «حفظ» في صفحة الإعدادات).</summary>
    [HttpPost("bulk")]
    public async Task<IActionResult> Bulk([FromBody] List<SettingIn> req, CancellationToken ct)
    {
        if (!await CanManage(ct)) return Forbid();
        if (req is null || req.Count == 0) return BadRequest(new { message = "مافيش حاجة تتحفظ" });
        if (req.Count > 200) return BadRequest(new { message = "200 مفتاح أقصى حد في المرة" });

        var keys = req.Select(r => B(r.SettingKey)).Where(k => k is not null).Select(k => k!).Distinct().ToList();
        var found = await _db.SystemSettings
            .Where(s => keys.Contains(s.SettingKey)).ToListAsync(ct);
        var byKey = found.ToDictionary(s => s.SettingKey, StringComparer.Ordinal);

        var n = 0;
        foreach (var r in req)
        {
            var k = B(r.SettingKey);
            if (k is null || !byKey.TryGetValue(k, out var s)) continue;

            var verr = CheckValue(r.SettingValue, s.ValueType);
            if (verr is not null) return BadRequest(new { message = $"{k}: {verr}" });

            s.SettingValue = B(r.SettingValue);
            s.UpdatedAt    = DateTime.UtcNow;
            s.UpdatedBy    = CurrentUserId();
            n++;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتحفظ {n} مفتاح" });
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        if (!await CanManage(ct)) return Forbid();

        var s = await _db.SystemSettings.FirstOrDefaultAsync(x => x.SettingKey == key, ct);
        if (s is null) return NotFound(new { message = "المفتاح مش موجود" });
        if (s.IsSystem)
            return BadRequest(new { message = "المفتاح ده من أساسيات النظام — مينفعش يتحذف" });

        _db.SystemSettings.Remove(s);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"🗑️ {key} اتحذف" });
    }

    /// <summary>قوائم منسدلة للفورم.</summary>
    [HttpGet("options")]
    [Authorize(Policy = "PERM:SETTINGS.VIEW")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        var taxes = await _db.TaxRates.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.TaxRateId)
            .Select(t => new TaxOpt(t.TaxRateId, t.Code, t.NameAr, t.Rate))
            .ToListAsync(ct);

        return Ok(new { taxRates = taxes, valueTypes = ValueTypes });
    }

    // ══════════════════════════════════════════════════════════════════

    /// <summary>بيتأكد إن القيمة مناسبة للنوع — أحسن ما تقع وقت القراءة.</summary>
    private static string? CheckValue(string? raw, string valueType)
    {
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v)) return null;   // فاضي = مسموح

        return valueType switch
        {
            "Int"     => int.TryParse(v, out _) ? null : "لازم رقم صحيح",
            "Decimal" => decimal.TryParse(v, out _) ? null : "لازم رقم",
            "Bool"    => v is "true" or "false" or "1" or "0" ? null : "لازم true أو false",
            "Date"    => DateOnly.TryParse(v, out _) ? null : "لازم تاريخ yyyy-MM-dd",
            _         => null
        };
    }

    // ══════════════════════════════════════════════════════════════════
    //  DTOs
    // ══════════════════════════════════════════════════════════════════

    public record ProfileOut(int CompanyProfileId, string LegalNameAr, string? LegalNameEn,
        string? TradeNameAr, string? TradeNameEn, string TaxNumber, string? VatRegistrationNumber,
        string? CommercialRegister, string AddressAr, string? AddressEn, string? CityAr,
        string? CityEn, string? Phone, string? Mobile, string? Email, string? Website,
        string? LogoPath, string? LogoDarkPath, string? StampPath,
        string? InvoiceFooterNoteAr, string? InvoiceFooterNoteEn,
        string DefaultCurrencyCode, int? DefaultTaxRateId, byte FiscalYearStartMonth,
        string? UpdatedAt);

    public record ProfileIn(string? LegalNameAr, string? LegalNameEn, string? TradeNameAr,
        string? TradeNameEn, string? TaxNumber, string? VatRegistrationNumber,
        string? CommercialRegister, string? AddressAr, string? AddressEn, string? CityAr,
        string? CityEn, string? Phone, string? Mobile, string? Email, string? Website,
        string? InvoiceFooterNoteAr, string? InvoiceFooterNoteEn,
        string? CurrencyCode, int? DefaultTaxRateId, byte FiscalYearStartMonth);

    public record SettingOut(int SettingId, string SettingKey, string? SettingValue,
        string ValueType, string? Description, bool IsSystem, string? UpdatedAt);

    public record SettingIn(string? SettingKey, string? SettingValue, string? ValueType,
        string? Description);

    public record TaxOpt(int TaxRateId, string Code, string NameAr, decimal Rate);
}
