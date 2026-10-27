using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// البيانات الأساسية — أنواع الرحلات · شروط الدفع · الخدمات.
/// </summary>
/// <remarks>
/// <para>التلاتة كانوا **للقراءة بس** من <c>api/options/*</c>. اتجمعوا في كنترولر واحد
/// لأنهم كلهم جداول تصنيف بسيطة بنفس الشكل (كود + اسم + خصائص).</para>
///
/// <para>🔴 <b>مافيش `IsDeleted` في التلات جداول</b> — فالحذف = **تعطيل**
/// (<c>IsActive</c>) لو العمود موجود، أو **منع الحذف** لو مستخدم.</para>
///
/// <para>🔴 <b>`Services` فيها `Gs1Code` و `EgsCode`</b> — دول مطلوبين
/// للفاتورة الإلكترونية المصرية (ETA). مش إلزاميين دلوقتي بس هيبقوا لازميين
/// لما نعمل التكامل.</para>
/// </remarks>
[ApiController]
[Route("api/master")]
[Authorize]
public class MasterDataController : ControllerBase
{
    private readonly FastComDbContext _db;
    public MasterDataController(FastComDbContext db) { _db = db; }

    private static int UserId(ClaimsPrincipal u) =>
        int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    // ══════════════════════════════════════════════════════════════
    //  أنواع الرحلات  TripTypes
    // ══════════════════════════════════════════════════════════════

    public record TripTypeItem(int TripTypeId, string Code, string NameAr, string? NameEn,
        int? DefaultTaxRateId, string? TaxRateName, int UsageCount);

    public record TripTypeUpsert(string Code, string NameAr, string? NameEn, int? DefaultTaxRateId);

    [HttpGet("trip-types")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> TripTypes(CancellationToken ct)
    {
        var rows = await _db.TripTypes.AsNoTracking()
            .OrderBy(t => t.Code)
            .Select(t => new TripTypeItem(t.TripTypeId, t.Code, t.NameAr, t.NameEn,
                t.DefaultTaxRateId,
                t.DefaultTaxRate != null ? t.DefaultTaxRate.Code + " — " + t.DefaultTaxRate.Rate.ToString("0.##") + "%" : null,
                0))
            .ToListAsync(ct);

        /* عدد الرحلات على كل نوع — عشان يعرف يعدّل ولا لأ */
        var counts = await _db.Trips.AsNoTracking().Where(t => !t.IsDeleted)
            .Where(t => t.TripTypeId.HasValue)
            .GroupBy(t => t.TripTypeId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var map = counts.ToDictionary(x => x.Id, x => x.N);

        return Ok(rows.Select(r => r with
        {
            UsageCount = map.TryGetValue(r.TripTypeId, out var n) ? n : 0
        }).ToList());
    }

    [HttpPost("trip-types")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> CreateTripType([FromBody] TripTypeUpsert req, CancellationToken ct)
    {
        var err = await ValidateTripTypeAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var t = new TripType
        {
            Code             = req.Code.Trim().ToUpperInvariant(),
            NameAr           = req.NameAr.Trim(),
            NameEn           = req.NameEn?.Trim(),
            DefaultTaxRateId = req.DefaultTaxRateId
        };
        _db.TripTypes.Add(t);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = t.TripTypeId, message = $"✅ اتضاف نوع الرحلة «{t.NameAr}»" });
    }

    [HttpPut("trip-types/{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> UpdateTripType(int id, [FromBody] TripTypeUpsert req, CancellationToken ct)
    {
        var t = await _db.TripTypes.FirstOrDefaultAsync(x => x.TripTypeId == id, ct);
        if (t is null) return NotFound(new { message = "نوع الرحلة غير موجود" });

        var err = await ValidateTripTypeAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        var used = await _db.Trips.CountAsync(x => x.TripTypeId == id && !x.IsDeleted, ct);
        /* 🔴 الكود مستخدم في قواعد أسعار (`CustomerPriceRules.TripTypeId`) — تغييره يبوّظ المطابقة */
        if (used > 0 && !string.Equals(t.Code, req.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = $"الكود مستخدم في {used} رحلة — مايتغيرش. اعمل نوع جديد" });

        t.Code             = req.Code.Trim().ToUpperInvariant();
        t.NameAr           = req.NameAr.Trim();
        t.NameEn           = req.NameEn?.Trim();
        t.DefaultTaxRateId = req.DefaultTaxRateId;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعدّل نوع الرحلة «{t.NameAr}»" });
    }

    private async Task<string?> ValidateTripTypeAsync(TripTypeUpsert? r, CancellationToken ct, int? cur = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (string.IsNullOrWhiteSpace(r.Code))   return "الكود مطلوب";
        if (string.IsNullOrWhiteSpace(r.NameAr)) return "الاسم بالعربي مطلوب";
        if (r.Code.Trim().Length > 30)    return "الكود أطول من 30 حرف";
        if (r.NameAr.Trim().Length > 100) return "الاسم أطول من 100 حرف";
        if (!CodeOk(r.Code)) return "الكود بالحروف الإنجليزية والأرقام بس";
        if (await _db.TripTypes.AnyAsync(t => t.Code.ToLower() == r.Code.Trim().ToLower()
                && (cur == null || t.TripTypeId != cur), ct))
            return $"الكود «{r.Code.Trim().ToUpperInvariant()}» موجود أصلًا";
        if (r.DefaultTaxRateId is not null &&
            !await _db.TaxRates.AnyAsync(x => x.TaxRateId == r.DefaultTaxRateId && x.IsActive, ct))
            return "نسبة الضريبة غير موجودة";
        return null;
    }

    // ══════════════════════════════════════════════════════════════
    //  شروط الدفع  PaymentTerms
    // ══════════════════════════════════════════════════════════════

    public record TermItem(int PaymentTermId, string Code, string NameAr, string? NameEn,
        int DueDays, int UsageCount);

    public record TermUpsert(string Code, string NameAr, string? NameEn, int DueDays);

    [HttpGet("payment-terms")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> PaymentTerms(CancellationToken ct)
    {
        var rows = await _db.PaymentTerms.AsNoTracking()
            .OrderBy(t => t.DueDays).ThenBy(t => t.Code)
            .Select(t => new TermItem(t.PaymentTermId, t.Code, t.NameAr, t.NameEn, t.DueDays, 0))
            .ToListAsync(ct);

        var counts = await _db.Customers.AsNoTracking().Where(c => !c.IsDeleted && c.PaymentTermId != null)
            .GroupBy(c => c.PaymentTermId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var map = counts.ToDictionary(x => (int)x.Id, x => x.N);

        return Ok(rows.Select(r => r with
        {
            UsageCount = map.TryGetValue(r.PaymentTermId, out var n) ? n : 0
        }).ToList());
    }

    [HttpPost("payment-terms")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> CreateTerm([FromBody] TermUpsert req, CancellationToken ct)
    {
        var err = await ValidateTermAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var t = new PaymentTerm
        {
            Code    = req.Code.Trim().ToUpperInvariant(),
            NameAr  = req.NameAr.Trim(),
            NameEn  = req.NameEn?.Trim(),
            DueDays = req.DueDays
        };
        _db.PaymentTerms.Add(t);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = t.PaymentTermId, message = $"✅ اتضاف شرط الدفع «{t.NameAr}»" });
    }

    [HttpPut("payment-terms/{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> UpdateTerm(int id, [FromBody] TermUpsert req, CancellationToken ct)
    {
        var t = await _db.PaymentTerms.FirstOrDefaultAsync(x => x.PaymentTermId == id, ct);
        if (t is null) return NotFound(new { message = "شرط الدفع غير موجود" });

        var err = await ValidateTermAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        var used = await _db.Customers.CountAsync(c => c.PaymentTermId == id && !c.IsDeleted, ct);
        if (used > 0 && !string.Equals(t.Code, req.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = $"الكود مستخدم في {used} عميل — مايتغيرش" });

        t.Code    = req.Code.Trim().ToUpperInvariant();
        t.NameAr  = req.NameAr.Trim();
        t.NameEn  = req.NameEn?.Trim();
        t.DueDays = req.DueDays;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعدّل شرط الدفع «{t.NameAr}»" });
    }

    private async Task<string?> ValidateTermAsync(TermUpsert? r, CancellationToken ct, int? cur = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (string.IsNullOrWhiteSpace(r.Code))   return "الكود مطلوب";
        if (string.IsNullOrWhiteSpace(r.NameAr)) return "الاسم بالعربي مطلوب";
        if (r.Code.Trim().Length > 30)    return "الكود أطول من 30 حرف";
        if (r.NameAr.Trim().Length > 100) return "الاسم أطول من 100 حرف";
        if (!CodeOk(r.Code)) return "الكود بالحروف الإنجليزية والأرقام بس";
        if (r.DueDays < 0 || r.DueDays > 730) return "عدد الأيام لازم يكون بين 0 و 730";
        if (await _db.PaymentTerms.AnyAsync(t => t.Code.ToLower() == r.Code.Trim().ToLower()
                && (cur == null || t.PaymentTermId != cur), ct))
            return $"الكود «{r.Code.Trim().ToUpperInvariant()}» موجود أصلًا";
        return null;
    }

    // ══════════════════════════════════════════════════════════════
    //  الخدمات  Services
    // ══════════════════════════════════════════════════════════════

    public record ServiceItem(int ServiceId, string ServiceCode, string NameAr, string? NameEn,
        string Unit, decimal DefaultSellingPrice, decimal DefaultCost,
        int? TaxRateId, string? TaxRateName, bool IsTaxable,
        string? Gs1Code, string? EgsCode, bool IsActive, int UsageCount);

    public record ServiceUpsert(string ServiceCode, string NameAr, string? NameEn, string Unit,
        decimal DefaultSellingPrice, decimal DefaultCost, int? TaxRateId, bool IsTaxable,
        string? Gs1Code, string? EgsCode, bool IsActive);

    [HttpGet("services")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> Services([FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var q = _db.Services.AsNoTracking().Where(s => !s.IsDeleted);
        if (activeOnly == true) q = q.Where(s => s.IsActive);

        var rows = await q.OrderBy(s => s.ServiceCode)
            .Select(s => new ServiceItem(s.ServiceId, s.ServiceCode, s.NameAr, s.NameEn, s.Unit,
                s.DefaultSellingPrice, s.DefaultCost, s.TaxRateId,
                s.TaxRate != null ? s.TaxRate.Code + " — " + s.TaxRate.Rate.ToString("0.##") + "%" : null,
                s.IsTaxable, s.Gs1Code, s.EgsCode, s.IsActive, 0))
            .ToListAsync(ct);

        /* عدد بنود الفواتير على كل خدمة — يعرف يعدّل السعر ولا لأ */
        var counts = await _db.InvoiceItems.AsNoTracking().Where(i => i.ServiceId != null)
            .GroupBy(i => i.ServiceId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var map = counts.ToDictionary(x => (int)x.Id, x => x.N);

        return Ok(rows.Select(r => r with
        {
            UsageCount = map.TryGetValue(r.ServiceId, out var n) ? n : 0
        }).ToList());
    }

    [HttpGet("services/{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> GetService(int id, CancellationToken ct)
    {
        var s = await _db.Services.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ServiceId == id && !x.IsDeleted, ct);
        if (s is null) return NotFound(new { message = "الخدمة غير موجودة" });
        return Ok(new ServiceItem(s.ServiceId, s.ServiceCode, s.NameAr, s.NameEn, s.Unit,
            s.DefaultSellingPrice, s.DefaultCost, s.TaxRateId, null, s.IsTaxable,
            s.Gs1Code, s.EgsCode, s.IsActive, 0));
    }

    [HttpPost("services")]
    [Authorize(Policy = "PERM:PRICING.CREATE")]
    public async Task<IActionResult> CreateService([FromBody] ServiceUpsert req, CancellationToken ct)
    {
        var err = await ValidateServiceAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var s = new Service
        {
            ServiceCode         = req.ServiceCode.Trim().ToUpperInvariant(),
            NameAr              = req.NameAr.Trim(),
            NameEn              = req.NameEn?.Trim(),
            Unit                = req.Unit.Trim(),
            DefaultSellingPrice = req.DefaultSellingPrice,
            DefaultCost         = req.DefaultCost,
            TaxRateId           = req.TaxRateId,
            IsTaxable           = req.IsTaxable,
            Gs1Code             = req.Gs1Code?.Trim(),
            EgsCode             = req.EgsCode?.Trim(),
            IsActive            = req.IsActive
        };
        _db.Services.Add(s);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = s.ServiceId, message = $"✅ اتضافت الخدمة «{s.NameAr}»" });
    }

    [HttpPut("services/{id:int}")]
    [Authorize(Policy = "PERM:PRICING.EDIT")]
    public async Task<IActionResult> UpdateService(int id, [FromBody] ServiceUpsert req, CancellationToken ct)
    {
        var s = await _db.Services.FirstOrDefaultAsync(x => x.ServiceId == id && !x.IsDeleted, ct);
        if (s is null) return NotFound(new { message = "الخدمة غير موجودة" });

        var err = await ValidateServiceAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        var used = await _db.InvoiceItems.CountAsync(i => i.ServiceId == id, ct);
        /* 🔴 الكود مستخدم في قواعد الأسعار (`CustomerPriceRules.ServiceId`) */
        if (used > 0 && !string.Equals(s.ServiceCode, req.ServiceCode.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = $"الكود مستخدم في {used} بند فاتورة — مايتغيرش. اعمل خدمة جديدة" });

        s.ServiceCode         = req.ServiceCode.Trim().ToUpperInvariant();
        s.NameAr              = req.NameAr.Trim();
        s.NameEn              = req.NameEn?.Trim();
        s.Unit                = req.Unit.Trim();
        s.DefaultSellingPrice = req.DefaultSellingPrice;
        s.DefaultCost         = req.DefaultCost;
        s.TaxRateId           = req.TaxRateId;
        s.IsTaxable           = req.IsTaxable;
        s.Gs1Code             = req.Gs1Code?.Trim();
        s.EgsCode             = req.EgsCode?.Trim();
        s.IsActive            = req.IsActive;
        s.UpdatedAt           = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعدّلت الخدمة «{s.NameAr}»" });
    }

    [HttpPost("services/{id:int}/deactivate")]
    [Authorize(Policy = "PERM:PRICING.EDIT")]
    public async Task<IActionResult> DeactivateService(int id, CancellationToken ct)
    {
        var s = await _db.Services.FirstOrDefaultAsync(x => x.ServiceId == id && !x.IsDeleted, ct);
        if (s is null) return NotFound(new { message = "الخدمة غير موجودة" });
        if (!s.IsActive) return BadRequest(new { message = "الخدمة معطّلة بالفعل" });

        s.IsActive  = false;
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعطّلت الخدمة «{s.NameAr}» — بنود الفواتير القديمة زي ما هي" });
    }

    [HttpPost("services/{id:int}/activate")]
    [Authorize(Policy = "PERM:PRICING.EDIT")]
    public async Task<IActionResult> ActivateService(int id, CancellationToken ct)
    {
        var s = await _db.Services.FirstOrDefaultAsync(x => x.ServiceId == id && !x.IsDeleted, ct);
        if (s is null) return NotFound(new { message = "الخدمة غير موجودة" });
        if (s.IsActive) return BadRequest(new { message = "الخدمة نشطة بالفعل" });

        s.IsActive  = true;
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتفعّلت الخدمة «{s.NameAr}»" });
    }

    private static readonly string[] Units = { "Trip", "Job", "Hour", "Day", "Unit", "Km", "Ton" };

    private async Task<string?> ValidateServiceAsync(ServiceUpsert? r, CancellationToken ct, int? cur = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (string.IsNullOrWhiteSpace(r.ServiceCode)) return "كود الخدمة مطلوب";
        if (string.IsNullOrWhiteSpace(r.NameAr))      return "الاسم بالعربي مطلوب";
        if (string.IsNullOrWhiteSpace(r.Unit))        return "الوحدة مطلوبة";

        if (r.ServiceCode.Trim().Length > 30) return "الكود أطول من 30 حرف";
        if (r.NameAr.Trim().Length > 150)     return "الاسم أطول من 150 حرف";
        if (!CodeOk(r.ServiceCode))           return "الكود بالحروف الإنجليزية والأرقام بس";
        if (!Units.Contains(r.Unit.Trim(), StringComparer.OrdinalIgnoreCase))
            return "الوحدة لازم تكون واحدة من: " + string.Join(" · ", Units);

        if (r.DefaultSellingPrice < 0) return "سعر البيع ماينفعش يكون سالب";
        if (r.DefaultCost < 0)         return "التكلفة ماينفعش تكون سالب";
        if (r.Gs1Code is { Length: > 50 }) return "كود GS1 أطول من 50 حرف";
        if (r.EgsCode is { Length: > 50 }) return "كود EGS أطول من 50 حرف";

        if (r.TaxRateId is not null &&
            !await _db.TaxRates.AnyAsync(x => x.TaxRateId == r.TaxRateId && x.IsActive, ct))
            return "نسبة الضريبة غير موجودة";

        if (await _db.Services.AnyAsync(s => !s.IsDeleted
                && s.ServiceCode.ToLower() == r.ServiceCode.Trim().ToLower()
                && (cur == null || s.ServiceId != cur), ct))
            return $"الكود «{r.ServiceCode.Trim().ToUpperInvariant()}» موجود أصلًا";

        return null;
    }

    // ══════════════════════════════════════════════════════════════
    //  المواني  Ports  (سحب + تعتيق)  #69
    // ══════════════════════════════════════════════════════════════

    public record PortItem(int PortId, string PortCode, string NameAr, string? NameEn,
        string? CityAr, bool IsActive, int UsageCount);

    public record PortUpsert(string NameAr, string? NameEn, string? CityAr, bool IsActive);

    [HttpGet("ports")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> Ports(CancellationToken ct)
    {
        var rows = await _db.Ports.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.NameAr)
            .Select(p => new PortItem(p.PortId, p.PortCode, p.NameAr, p.NameEn,
                p.CityAr, p.IsActive, 0))
            .ToListAsync(ct);

        /* الاستخدام = سحب + تعتيق من الحجوزات والعمليات — 4 استعلامات GroupBy بسيطة (SelectMany بمصفوفة ما بيترجمش في EF) */
        var bkPull = await _db.Bookings.AsNoTracking().Where(b => !b.IsDeleted && b.PortId != null)
            .GroupBy(b => b.PortId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var bkTah = await _db.Bookings.AsNoTracking().Where(b => !b.IsDeleted && b.TahteeqPortId != null)
            .GroupBy(b => b.TahteeqPortId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var opPull = await _db.Operations.AsNoTracking().Where(o => !o.IsDeleted && o.PortId != null)
            .GroupBy(o => o.PortId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var opTah = await _db.Operations.AsNoTracking().Where(o => !o.IsDeleted && o.TahteeqPortId != null)
            .GroupBy(o => o.TahteeqPortId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var map = bkPull.Concat(bkTah).Concat(opPull).Concat(opTah)
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.N));

        return Ok(rows.Select(r => r with
        {
            UsageCount = map.TryGetValue(r.PortId, out var n) ? n : 0
        }).ToList());
    }

    [HttpPost("ports")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> CreatePort([FromBody] PortUpsert req, CancellationToken ct)
    {
        var err = await ValidatePortAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var p = new Port
        {
            PortCode  = await NextCodeAsync("PRT-", x => _db.Ports.AnyAsync(y => y.PortCode == x, ct), ct),
            NameAr    = req.NameAr.Trim(),
            NameEn    = string.IsNullOrWhiteSpace(req.NameEn) ? null : req.NameEn.Trim(),
            CityAr    = string.IsNullOrWhiteSpace(req.CityAr) ? null : req.CityAr.Trim(),
            IsActive  = req.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        _db.Ports.Add(p);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = p.PortId, message = $"✅ اتضاف الميناء «{p.NameAr}»" });
    }

    [HttpPut("ports/{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> UpdatePort(int id, [FromBody] PortUpsert req, CancellationToken ct)
    {
        var p = await _db.Ports.FirstOrDefaultAsync(x => x.PortId == id && !x.IsDeleted, ct);
        if (p is null) return NotFound(new { message = "الميناء غير موجود" });

        var err = await ValidatePortAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        p.NameAr   = req.NameAr.Trim();
        p.NameEn   = string.IsNullOrWhiteSpace(req.NameEn) ? null : req.NameEn.Trim();
        p.CityAr   = string.IsNullOrWhiteSpace(req.CityAr) ? null : req.CityAr.Trim();
        p.IsActive = req.IsActive;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعدّل الميناء «{p.NameAr}»" });
    }

    private async Task<string?> ValidatePortAsync(PortUpsert? r, CancellationToken ct, int? cur = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (string.IsNullOrWhiteSpace(r.NameAr)) return "الاسم بالعربي مطلوب";
        if (r.NameAr.Trim().Length > 150)          return "الاسم أطول من 150 حرف";
        if (r.NameEn is { Length: > 150 })         return "الاسم الإنجليزي أطول من 150 حرف";
        if (r.CityAr is { Length: > 100 })         return "المدينة أطول من 100 حرف";
        if (await _db.Ports.AnyAsync(p => !p.IsDeleted && p.NameAr == r.NameAr.Trim()
                && (cur == null || p.PortId != cur), ct))
            return $"الميناء «{r.NameAr.Trim()}» موجود أصلًا";
        return null;
    }

    // ══════════════════════════════════════════════════════════════
    //  الوجهات  Destinations  #69
    // ══════════════════════════════════════════════════════════════

    public record DestItem(int DestinationId, string Code, string NameAr, string? NameEn,
        string? CityAr, decimal? DistanceKm, bool IsActive, int UsageCount);

    public record DestUpsert(string NameAr, string? NameEn, string? CityAr, decimal? DistanceKm, bool IsActive);

    [HttpGet("destinations")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> Destinations(CancellationToken ct)
    {
        var rows = await _db.Destinations.AsNoTracking()
            .Where(d => !d.IsDeleted)
            .OrderBy(d => d.NameAr)
            .Select(d => new DestItem(d.DestinationId, d.Code, d.NameAr, d.NameEn,
                d.CityAr, d.DistanceKm, d.IsActive, 0))
            .ToListAsync(ct);

        var bk = await _db.Bookings.AsNoTracking().Where(b => !b.IsDeleted && b.DestinationId != null)
            .GroupBy(b => b.DestinationId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var op = await _db.Operations.AsNoTracking().Where(o => !o.IsDeleted && o.DestinationId != null)
            .GroupBy(o => o.DestinationId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var map = bk.Concat(op).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.N));

        return Ok(rows.Select(r => r with
        {
            UsageCount = map.TryGetValue(r.DestinationId, out var n) ? n : 0
        }).ToList());
    }

    [HttpPost("destinations")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> CreateDestination([FromBody] DestUpsert req, CancellationToken ct)
    {
        var err = await ValidateDestAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var d = new Destination
        {
            Code       = await NextCodeAsync("DST-", x => _db.Destinations.AnyAsync(y => y.Code == x, ct), ct),
            NameAr     = req.NameAr.Trim(),
            NameEn     = string.IsNullOrWhiteSpace(req.NameEn) ? null : req.NameEn.Trim(),
            CityAr     = string.IsNullOrWhiteSpace(req.CityAr) ? null : req.CityAr.Trim(),
            DistanceKm = req.DistanceKm,
            IsActive   = req.IsActive
        };
        _db.Destinations.Add(d);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = d.DestinationId, message = $"✅ اتضافت الوجهة «{d.NameAr}»" });
    }

    [HttpPut("destinations/{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> UpdateDestination(int id, [FromBody] DestUpsert req, CancellationToken ct)
    {
        var d = await _db.Destinations.FirstOrDefaultAsync(x => x.DestinationId == id && !x.IsDeleted, ct);
        if (d is null) return NotFound(new { message = "الوجهة غير موجودة" });

        var err = await ValidateDestAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        d.NameAr     = req.NameAr.Trim();
        d.NameEn     = string.IsNullOrWhiteSpace(req.NameEn) ? null : req.NameEn.Trim();
        d.CityAr     = string.IsNullOrWhiteSpace(req.CityAr) ? null : req.CityAr.Trim();
        d.DistanceKm = req.DistanceKm;
        d.IsActive   = req.IsActive;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعدّلت الوجهة «{d.NameAr}»" });
    }

    private async Task<string?> ValidateDestAsync(DestUpsert? r, CancellationToken ct, int? cur = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (string.IsNullOrWhiteSpace(r.NameAr)) return "الاسم بالعربي مطلوب";
        if (r.NameAr.Trim().Length > 150)          return "الاسم أطول من 150 حرف";
        if (r.NameEn is { Length: > 150 })         return "الاسم الإنجليزي أطول من 150 حرف";
        if (r.CityAr is { Length: > 100 })         return "المدينة أطول من 100 حرف";
        if (r.DistanceKm is < 0)                   return "المسافة ماينفعش تكون سالبة";
        if (r.DistanceKm is > 9999999m)            return "المسافة أكبر من المسموح";
        if (await _db.Destinations.AnyAsync(d => !d.IsDeleted && d.NameAr == r.NameAr.Trim()
                && (cur == null || d.DestinationId != cur), ct))
            return $"الوجهة «{r.NameAr.Trim()}» موجودة أصلًا";
        return null;
    }

    /* #69 — توليد كود فريد بالبادئة: PRT-001 / DST-001 ... */
    private static async Task<string> NextCodeAsync(string prefix,
        Func<string, Task<bool>> exists, CancellationToken ct)
    {
        for (var i = 1; i < 1000; i++)
        {
            var c = prefix + i.ToString("000");
            if (!await exists(c)) return c;
        }
        return prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    // ══════════════════════════════════════════════════════════════

    private static bool CodeOk(string code) =>
        System.Text.RegularExpressions.Regex.IsMatch(code.Trim(), "^[A-Za-z0-9_-]+$");
}
