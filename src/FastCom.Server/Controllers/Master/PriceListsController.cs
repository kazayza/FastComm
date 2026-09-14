using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// قوائم الأسعار وقواعد أسعار العملاء.
/// </summary>
/// <remarks>
/// <para>🔴 <b>القاعدة الأخص تكسب.</b> نفس منطق <c>price-suggest</c> في
/// <c>OperationsController</c> بالظبط:
/// <c>OrderByDescending(عدد الأبعاد المعبّاة).ThenBy(Priority).ThenByDescending(ValidFrom)</c>.
/// البعد الفاضي (NULL) معناه «أي قيمة».</para>
///
/// <para><b>الحذف ناعم</b> (<c>IsDeleted = 1</c>) — <c>price-suggest</c> بيعمل
/// فلتر <c>!IsDeleted &amp;&amp; IsActive</c> فالقاعدة المحذوفة بتبطّل فورًا.</para>
///
/// <para>🔴 <b>`PriceLists` مالهاش `IsDeleted`</b> — فقايمة فيها قواعد ماتتحذفش،
/// بتتعطّل بـ `ValidTo` بدل كده.</para>
/// </remarks>
[ApiController]
[Route("api/master/pricelists")]
[Authorize]
public class PriceListsController : ControllerBase
{
    private readonly FastComDbContext _db;
    public PriceListsController(FastComDbContext db) { _db = db; }

    private static int UserId(ClaimsPrincipal u) =>
        int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    // ══════════════════════════════════════════════════════════════
    //  قوائم الأسعار  PriceLists
    // ══════════════════════════════════════════════════════════════

    public record ListItem(int PriceListId, string PriceListCode, string NameAr, string? NameEn,
        DateOnly ValidFrom, DateOnly? ValidTo, int RuleCount);

    public record ListUpsert(string PriceListCode, string NameAr, string? NameEn,
        string ValidFrom, string? ValidTo);

    [HttpGet]
    [Authorize(Policy = "PERM:PRICING.VIEW")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _db.PriceLists.AsNoTracking()
            .OrderBy(p => p.PriceListCode)
            .Select(p => new ListItem(p.PriceListId, p.PriceListCode, p.NameAr, p.NameEn,
                p.ValidFrom, p.ValidTo, 0))
            .ToListAsync(ct);

        var counts = await _db.CustomerPriceRules.AsNoTracking()
            .Where(r => !r.IsDeleted && r.PriceListId != null)
            .GroupBy(r => r.PriceListId!.Value).Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var map = counts.ToDictionary(x => (int)x.Id, x => x.N);

        return Ok(rows.Select(r => r with
        {
            RuleCount = map.TryGetValue(r.PriceListId, out var n) ? n : 0
        }).ToList());
    }

    [HttpPost]
    [Authorize(Policy = "PERM:PRICING.CREATE")]
    public async Task<IActionResult> Create([FromBody] ListUpsert req, CancellationToken ct)
    {
        var err = await ValidateListAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var p = new PriceList
        {
            PriceListCode = req.PriceListCode.Trim().ToUpperInvariant(),
            NameAr        = req.NameAr.Trim(),
            NameEn        = req.NameEn?.Trim(),
            ValidFrom     = DateOnly.Parse(req.ValidFrom),
            ValidTo       = string.IsNullOrWhiteSpace(req.ValidTo) ? null : DateOnly.Parse(req.ValidTo),
            CreatedBy     = UserId(User)
        };
        _db.PriceLists.Add(p);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = p.PriceListId, message = $"✅ اتضافت قائمة الأسعار «{p.NameAr}»" });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:PRICING.EDIT")]
    public async Task<IActionResult> Update(int id, [FromBody] ListUpsert req, CancellationToken ct)
    {
        var p = await _db.PriceLists.FirstOrDefaultAsync(x => x.PriceListId == id, ct);
        if (p is null) return NotFound(new { message = "قائمة الأسعار غير موجودة" });

        var err = await ValidateListAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        var used = await _db.CustomerPriceRules.CountAsync(r => r.PriceListId == id && !r.IsDeleted, ct);
        if (used > 0 && !string.Equals(p.PriceListCode, req.PriceListCode.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = $"الكود مستخدم في {used} قاعدة — مايتغيرش" });

        p.PriceListCode = req.PriceListCode.Trim().ToUpperInvariant();
        p.NameAr        = req.NameAr.Trim();
        p.NameEn        = req.NameEn?.Trim();
        p.ValidFrom     = DateOnly.Parse(req.ValidFrom);
        p.ValidTo       = string.IsNullOrWhiteSpace(req.ValidTo) ? null : DateOnly.Parse(req.ValidTo);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعدّلت قائمة الأسعار «{p.NameAr}»" });
    }

    /// <summary>
    /// 🔴 مافيش `IsDeleted` في `PriceLists` — فبنقفلها بـ `ValidTo = امبارح`.
    /// </summary>
    [HttpPost("{id:int}/close")]
    [Authorize(Policy = "PERM:PRICING.EDIT")]
    public async Task<IActionResult> Close(int id, CancellationToken ct)
    {
        var p = await _db.PriceLists.FirstOrDefaultAsync(x => x.PriceListId == id, ct);
        if (p is null) return NotFound(new { message = "قائمة الأسعار غير موجودة" });
        if (p.ValidTo is not null && p.ValidTo < DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(new { message = "القائمة مقفولة بالفعل" });

        p.ValidTo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتقفلت قائمة «{p.NameAr}» — مبقتش سارية" });
    }

    private async Task<string?> ValidateListAsync(ListUpsert? r, CancellationToken ct, int? cur = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (string.IsNullOrWhiteSpace(r.PriceListCode)) return "كود القائمة مطلوب";
        if (string.IsNullOrWhiteSpace(r.NameAr))        return "الاسم بالعربي مطلوب";
        if (r.PriceListCode.Trim().Length > 30) return "الكود أطول من 30 حرف";
        if (r.NameAr.Trim().Length > 150)       return "الاسم أطول من 150 حرف";
        if (!DateOnly.TryParse(r.ValidFrom, out var from)) return "تاريخ «ساري من» غير صالح";
        DateOnly? to = null;
        if (!string.IsNullOrWhiteSpace(r.ValidTo))
        {
            if (!DateOnly.TryParse(r.ValidTo, out var t)) return "تاريخ «ساري لغاية» غير صالح";
            to = t;
            if (to < from) return "«ساري لغاية» قبل «ساري من»";
        }
        if (await _db.PriceLists.AnyAsync(p => p.PriceListCode.ToLower() == r.PriceListCode.Trim().ToLower()
                && (cur == null || p.PriceListId != cur), ct))
            return $"الكود «{r.PriceListCode.Trim().ToUpperInvariant()}» موجود أصلًا";
        return null;
    }

    // ══════════════════════════════════════════════════════════════
    //  قواعد أسعار العملاء  CustomerPriceRules
    // ══════════════════════════════════════════════════════════════

    public record RuleItem(long CustomerPriceRuleId, int CustomerId, string CustomerName,
        int? PriceListId, string? PriceListName, int ServiceId, string ServiceName,
        int? PortId, string? PortName, int? DestinationId, string? DestinationName,
        int? ContainerTypeId, string? ContainerTypeName, int? TripTypeId, string? TripTypeName,
        decimal UnitPrice, decimal? CostPrice, int? TaxRateId, string? TaxRateName,
        DateOnly ValidFrom, DateOnly? ValidTo, int Priority, bool IsActive, string? Notes,
        int Specificity);

    public record RuleUpsert(int CustomerId, int? PriceListId, int ServiceId,
        int? PortId, int? DestinationId, int? ContainerTypeId, int? TripTypeId,
        decimal UnitPrice, decimal? CostPrice, int? TaxRateId,
        string ValidFrom, string? ValidTo, int Priority, bool IsActive, string? Notes);

    [HttpGet("{id:int}/rules")]
    [Authorize(Policy = "PERM:PRICING.VIEW")]
    public async Task<IActionResult> Rules(int id, [FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var q = _db.CustomerPriceRules.AsNoTracking()
            .Where(r => !r.IsDeleted && r.PriceListId == id);
        if (activeOnly == true) q = q.Where(r => r.IsActive);

        var rows = await ProjectAsync(q, ct);
        return Ok(rows.OrderByDescending(r => r.Specificity).ThenBy(r => r.Priority).ToList());
    }

    /// <summary>كل قواعد عميل معيّن — عشان شاشة العميل تعرض أسعاره.</summary>
    [HttpGet("rules/by-customer/{customerId:int}")]
    [Authorize(Policy = "PERM:PRICING.VIEW")]
    public async Task<IActionResult> RulesByCustomer(int customerId, CancellationToken ct)
    {
        var q = _db.CustomerPriceRules.AsNoTracking()
            .Where(r => !r.IsDeleted && r.CustomerId == customerId);
        return Ok(await ProjectAsync(q, ct));
    }

    /// <summary>قاعدة واحدة — للتعديل.</summary>
    [HttpGet("rules/{ruleId:long}")]
    [Authorize(Policy = "PERM:PRICING.VIEW")]
    public async Task<IActionResult> GetRule(long ruleId, CancellationToken ct)
    {
        var q = _db.CustomerPriceRules.AsNoTracking()
            .Where(r => !r.IsDeleted && r.CustomerPriceRuleId == ruleId);
        var rows = await ProjectAsync(q, ct);
        var r = rows.FirstOrDefault();
        return r is null ? NotFound(new { message = "القاعدة غير موجودة" }) : Ok(r);
    }

    [HttpPost("{id:int}/rules")]
    [Authorize(Policy = "PERM:PRICING.CREATE")]
    public async Task<IActionResult> CreateRule(int id, [FromBody] RuleUpsert req, CancellationToken ct)
    {
        if (!await _db.PriceLists.AnyAsync(p => p.PriceListId == id, ct))
            return NotFound(new { message = "قائمة الأسعار غير موجودة" });

        var err = await ValidateRuleAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var r = new CustomerPriceRule
        {
            CustomerId      = req.CustomerId,
            PriceListId     = id,
            ServiceId       = req.ServiceId,
            PortId          = req.PortId,
            DestinationId   = req.DestinationId,
            ContainerTypeId = req.ContainerTypeId,
            TripTypeId      = req.TripTypeId,
            UnitPrice       = req.UnitPrice,
            CostPrice       = req.CostPrice,
            TaxRateId       = req.TaxRateId,
            ValidFrom       = DateOnly.Parse(req.ValidFrom),
            ValidTo         = string.IsNullOrWhiteSpace(req.ValidTo) ? null : DateOnly.Parse(req.ValidTo),
            Priority        = req.Priority,
            IsActive        = req.IsActive,
            Notes           = req.Notes?.Trim(),
            CreatedBy       = UserId(User)
        };

        _db.CustomerPriceRules.Add(r);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = r.CustomerPriceRuleId, message = "✅ اتضافت قاعدة السعر" });
    }

    [HttpPut("rules/{ruleId:long}")]
    [Authorize(Policy = "PERM:PRICING.EDIT")]
    public async Task<IActionResult> UpdateRule(long ruleId, [FromBody] RuleUpsert req, CancellationToken ct)
    {
        var r = await _db.CustomerPriceRules.FirstOrDefaultAsync(x => x.CustomerPriceRuleId == ruleId && !x.IsDeleted, ct);
        if (r is null) return NotFound(new { message = "القاعدة غير موجودة" });

        var err = await ValidateRuleAsync(req, ct, ruleId);
        if (err is not null) return BadRequest(new { message = err });

        r.CustomerId      = req.CustomerId;
        r.ServiceId       = req.ServiceId;
        r.PortId          = req.PortId;
        r.DestinationId   = req.DestinationId;
        r.ContainerTypeId = req.ContainerTypeId;
        r.TripTypeId      = req.TripTypeId;
        r.UnitPrice       = req.UnitPrice;
        r.CostPrice       = req.CostPrice;
        r.TaxRateId       = req.TaxRateId;
        r.ValidFrom       = DateOnly.Parse(req.ValidFrom);
        r.ValidTo         = string.IsNullOrWhiteSpace(req.ValidTo) ? null : DateOnly.Parse(req.ValidTo);
        r.Priority        = req.Priority;
        r.IsActive        = req.IsActive;
        r.Notes           = req.Notes?.Trim();
        r.UpdatedAt       = DateTime.UtcNow;
        r.UpdatedBy       = UserId(User);

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتعدّلت قاعدة السعر" });
    }

    /// <summary>حذف ناعم — القاعدة بتبطّل فورًا من `price-suggest`.</summary>
    [HttpDelete("rules/{ruleId:long}")]
    [Authorize(Policy = "PERM:PRICING.DELETE")]
    public async Task<IActionResult> DeleteRule(long ruleId, CancellationToken ct)
    {
        var r = await _db.CustomerPriceRules.FirstOrDefaultAsync(x => x.CustomerPriceRuleId == ruleId && !x.IsDeleted, ct);
        if (r is null) return NotFound(new { message = "القاعدة غير موجودة" });

        r.IsDeleted = true;
        r.UpdatedAt = DateTime.UtcNow;
        r.UpdatedBy = UserId(User);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحذفت قاعدة السعر — مبقتش بتتطبق" });
    }

    // ══════════════════════════════════════════════════════════════
    //  معاينة المطابقة — «لو العميل ده طلب الخدمة دي، هياخد سعر كام؟»
    // ══════════════════════════════════════════════════════════════

    [HttpGet("{id:int}/match")]
    [Authorize(Policy = "PERM:PRICING.VIEW")]
    public async Task<IActionResult> Match(int id, [FromQuery] int customerId, [FromQuery] int serviceId,
        [FromQuery] int? portId, [FromQuery] int? destinationId,
        [FromQuery] int? containerTypeId, [FromQuery] int? tripTypeId,
        [FromQuery] string? date, CancellationToken ct)
    {
        if (customerId <= 0 || serviceId <= 0)
            return BadRequest(new { message = "العميل والخدمة مطلوبين" });

        var on = DateOnly.TryParse(date, out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow);

        var q = _db.CustomerPriceRules.AsNoTracking()
            .Where(r => !r.IsDeleted && r.IsActive && r.PriceListId == id &&
                        r.CustomerId == customerId && r.ServiceId == serviceId &&
                        r.ValidFrom <= on && (r.ValidTo == null || r.ValidTo >= on));
        var all = await ProjectAsync(q, ct);

        /* 🔴 نفس منطق price-suggest — الأخص تكسب */
        var match = all
            .Where(r => portId == null || r.PortId == null || r.PortId == portId)
            .Where(r => destinationId == null || r.DestinationId == null || r.DestinationId == destinationId)
            .Where(r => tripTypeId == null || r.TripTypeId == null || r.TripTypeId == tripTypeId)
            .Where(r => containerTypeId == null || r.ContainerTypeId == null || r.ContainerTypeId == containerTypeId)
            .OrderByDescending(r => r.Specificity)
            .ThenBy(r => r.Priority)
            .ThenByDescending(r => r.ValidFrom)
            .FirstOrDefault();

        return Ok(new
        {
            matched  = match is not null,
            rule     = match,
            checked_ = all.Count
        });
    }

    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// الإسقاط المشترك — بيجيب الأسماء مع الأبعاد، ويحسب `Specificity`
    /// (عدد الأبعاد المعبّاة) اللي بيحدد أنهي قاعدة تكسب.
    /// </summary>
    private async Task<List<RuleItem>> ProjectAsync(IQueryable<CustomerPriceRule> q, CancellationToken ct)
    {
        var raw = await q.ToListAsync(ct);
        if (raw.Count == 0) return new();

        var custIds = raw.Select(r => r.CustomerId).Distinct().ToList();
        var svcIds  = raw.Select(r => r.ServiceId).Distinct().ToList();
        var listIds = raw.Where(r => r.PriceListId != null).Select(r => r.PriceListId!.Value).Distinct().ToList();
        var portIds = raw.Where(r => r.PortId != null).Select(r => r.PortId!.Value).Distinct().ToList();
        var destIds = raw.Where(r => r.DestinationId != null).Select(r => r.DestinationId!.Value).Distinct().ToList();
        var ctIds   = raw.Where(r => r.ContainerTypeId != null).Select(r => r.ContainerTypeId!.Value).Distinct().ToList();
        var ttIds   = raw.Where(r => r.TripTypeId != null).Select(r => r.TripTypeId!.Value).Distinct().ToList();

        /* 🔴 بنجيب القواميس باستعلامات منفصلة — GroupBy + Join بعد
           الاستعلام مش بيتترجم في EF8. */
        var cust = (await _db.Customers.AsNoTracking().Where(c => custIds.Contains(c.CustomerId))
            .Select(c => new { c.CustomerId, c.NameAr }).ToListAsync(ct)).ToDictionary(x => x.CustomerId, x => x.NameAr);
        var svc = (await _db.Services.AsNoTracking().Where(s => svcIds.Contains(s.ServiceId))
            .Select(s => new { s.ServiceId, s.NameAr }).ToListAsync(ct)).ToDictionary(x => x.ServiceId, x => x.NameAr);
        var lst = (await _db.PriceLists.AsNoTracking().Where(p => listIds.Contains(p.PriceListId))
            .Select(p => new { p.PriceListId, p.NameAr }).ToListAsync(ct)).ToDictionary(x => x.PriceListId, x => x.NameAr);
        var prt = (await _db.Ports.AsNoTracking().Where(p => portIds.Contains(p.PortId))
            .Select(p => new { p.PortId, p.NameAr }).ToListAsync(ct)).ToDictionary(x => x.PortId, x => x.NameAr);
        var dst = (await _db.Destinations.AsNoTracking().Where(d => destIds.Contains(d.DestinationId))
            .Select(d => new { d.DestinationId, d.NameAr }).ToListAsync(ct)).ToDictionary(x => x.DestinationId, x => x.NameAr);
        var cty = (await _db.ContainerTypes.AsNoTracking().Where(c => ctIds.Contains(c.ContainerTypeId))
            .Select(c => new { c.ContainerTypeId, c.NameAr }).ToListAsync(ct)).ToDictionary(x => x.ContainerTypeId, x => x.NameAr);
        var ttp = (await _db.TripTypes.AsNoTracking().Where(t => ttIds.Contains(t.TripTypeId))
            .Select(t => new { t.TripTypeId, t.NameAr }).ToListAsync(ct)).ToDictionary(x => x.TripTypeId, x => x.NameAr);

        return raw.Select(r =>
        {
            var spec = (r.PortId is not null ? 1 : 0) + (r.DestinationId is not null ? 1 : 0)
                     + (r.TripTypeId is not null ? 1 : 0) + (r.ContainerTypeId is not null ? 1 : 0);
            return new RuleItem(
                r.CustomerPriceRuleId, r.CustomerId,
                cust.TryGetValue(r.CustomerId, out var cn) ? cn : "?",
                r.PriceListId,
                r.PriceListId is not null && lst.TryGetValue(r.PriceListId.Value, out var ln) ? ln : null,
                r.ServiceId,
                svc.TryGetValue(r.ServiceId, out var sn) ? sn : "?",
                r.PortId,        r.PortId        is not null && prt.TryGetValue(r.PortId.Value, out var pn) ? pn : null,
                r.DestinationId, r.DestinationId is not null && dst.TryGetValue(r.DestinationId.Value, out var dn) ? dn : null,
                r.ContainerTypeId, r.ContainerTypeId is not null && cty.TryGetValue(r.ContainerTypeId.Value, out var tn) ? tn : null,
                r.TripTypeId,    r.TripTypeId    is not null && ttp.TryGetValue(r.TripTypeId.Value, out var tt) ? tt : null,
                r.UnitPrice, r.CostPrice, r.TaxRateId, null,
                r.ValidFrom, r.ValidTo, r.Priority, r.IsActive, r.Notes, spec);
        }).ToList();
    }

    private async Task<string?> ValidateRuleAsync(RuleUpsert? r, CancellationToken ct, long? cur = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (r.CustomerId <= 0) return "العميل مطلوب";
        if (r.ServiceId <= 0)  return "الخدمة مطلوبة";
        if (r.UnitPrice < 0)   return "السعر ماينفعش يكون سالب";
        if (r.CostPrice is < 0) return "التكلفة ماينفعش تكون سالب";
        if (r.Priority < 1 || r.Priority > 9999) return "الأولوية لازم تكون بين 1 و 9999";
        if (r.Notes is { Length: > 500 }) return "الملاحظات أطول من 500 حرف";

        if (!DateOnly.TryParse(r.ValidFrom, out var from)) return "تاريخ «ساري من» غير صالح";
        if (!string.IsNullOrWhiteSpace(r.ValidTo))
        {
            if (!DateOnly.TryParse(r.ValidTo, out var to)) return "تاريخ «ساري لغاية» غير صالح";
            if (to < from) return "«ساري لغاية» قبل «ساري من»";
        }

        if (!await _db.Customers.AnyAsync(c => c.CustomerId == r.CustomerId && !c.IsDeleted, ct))
            return "العميل غير موجود";
        if (!await _db.Services.AnyAsync(s => s.ServiceId == r.ServiceId && !s.IsDeleted, ct))
            return "الخدمة غير موجودة";
        if (r.TaxRateId is not null && !await _db.TaxRates.AnyAsync(t => t.TaxRateId == r.TaxRateId && t.IsActive, ct))
            return "نسبة الضريبة غير موجودة";

        return null;
    }
}
