using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// جدول نوالين السائقين (نولون النقله = أجرة السائق — تكلفة على الشركة).
/// قاعدة = ميناء + وجهة + نوع رحلة (أي بُعد فاضي = «أي قيمة») + مبلغ + فترة سريان.
/// الاقتراح التلقائي: <c>api/trips/freight-suggest</c> — الأخص يكسب ثم الأولوية ثم الأحدث،
/// نفس منطق قواعد تسعير العميل.
/// </summary>
[ApiController]
[Route("api/driver-freight-rules")]
[Authorize]
public class DriverFreightRulesController : ControllerBase
{
    private readonly FastComDbContext _db;
    public DriverFreightRulesController(FastComDbContext db) { _db = db; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    // ═══════════════ DTOs ═══════════════

    public record ListItem(int DriverFreightRuleId, int? PortId, string? PortName,
        int? DestinationId, string? DestinationName, int? TripTypeId, string? TripTypeName,
        decimal Amount, DateOnly ValidFrom, DateOnly? ValidTo, int Priority,
        bool IsActive, string? Notes);

    public record Upsert(int? PortId, int? DestinationId, int? TripTypeId,
        decimal Amount, string ValidFrom, string? ValidTo, int Priority,
        bool IsActive, string? Notes);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> List([FromQuery] bool? activeOnly,
        [FromQuery] int? portId, [FromQuery] int? destinationId, CancellationToken ct)
    {
        var q = _db.DriverFreightRules.AsNoTracking().Where(r => !r.IsDeleted);
        if (activeOnly == true) q = q.Where(r => r.IsActive);
        if (portId is > 0)        q = q.Where(r => r.PortId == portId);
        if (destinationId is > 0) q = q.Where(r => r.DestinationId == destinationId);

        var rows = await q
            .OrderBy(r => r.Priority).ThenByDescending(r => r.ValidFrom)
            .Select(r => new ListItem(r.DriverFreightRuleId, r.PortId,
                r.PortId != null
                    ? _db.Ports.Where(p => p.PortId == r.PortId).Select(p => p.NameAr).FirstOrDefault()
                    : null,
                r.DestinationId,
                r.DestinationId != null
                    ? _db.Destinations.Where(d => d.DestinationId == r.DestinationId).Select(d => d.NameAr).FirstOrDefault()
                    : null,
                r.TripTypeId,
                r.TripTypeId != null
                    ? _db.TripTypes.Where(t => t.TripTypeId == r.TripTypeId).Select(t => t.NameAr).FirstOrDefault()
                    : null,
                r.Amount, r.ValidFrom, r.ValidTo, r.Priority, r.IsActive, r.Notes))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ═══════════════

    [HttpGet("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var r = await _db.DriverFreightRules.AsNoTracking()
            .FirstOrDefaultAsync(x => x.DriverFreightRuleId == id && !x.IsDeleted, ct);
        if (r is null) return NotFound(new { message = "القاعدة مش موجودة" });

        var portName = r.PortId is null ? null : await _db.Ports.AsNoTracking()
            .Where(p => p.PortId == r.PortId).Select(p => p.NameAr).FirstOrDefaultAsync(ct);
        var destName = r.DestinationId is null ? null : await _db.Destinations.AsNoTracking()
            .Where(d => d.DestinationId == r.DestinationId).Select(d => d.NameAr).FirstOrDefaultAsync(ct);
        var typeName = r.TripTypeId is null ? null : await _db.TripTypes.AsNoTracking()
            .Where(t => t.TripTypeId == r.TripTypeId).Select(t => t.NameAr).FirstOrDefaultAsync(ct);

        return Ok(new ListItem(r.DriverFreightRuleId, r.PortId, portName,
            r.DestinationId, destName, r.TripTypeId, typeName,
            r.Amount, r.ValidFrom, r.ValidTo, r.Priority, r.IsActive, r.Notes));
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Create([FromBody] Upsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var from = DateOnly.Parse(req.ValidFrom);
        var to   = DateOnly.TryParse(req.ValidTo, out var d) ? d : (DateOnly?)null;

        var r = new DriverFreightRule
        {
            PortId        = req.PortId,
            DestinationId = req.DestinationId,
            TripTypeId    = req.TripTypeId,
            Amount        = req.Amount,
            ValidFrom     = from,
            ValidTo       = to,
            Priority      = req.Priority,
            IsActive      = req.IsActive,
            Notes         = req.Notes?.Trim(),
            CreatedBy     = CurrentUserId()
        };

        _db.DriverFreightRules.Add(r);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = r.DriverFreightRuleId, message = "اتضافت قاعدة النولون" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Update(int id, [FromBody] Upsert req, CancellationToken ct)
    {
        var r = await _db.DriverFreightRules.FirstOrDefaultAsync(x => x.DriverFreightRuleId == id && !x.IsDeleted, ct);
        if (r is null) return NotFound(new { message = "القاعدة مش موجودة" });

        var err = await ValidateAsync(req, ct, id);
        if (err is not null) return BadRequest(new { message = err });

        r.PortId        = req.PortId;
        r.DestinationId = req.DestinationId;
        r.TripTypeId    = req.TripTypeId;
        r.Amount        = req.Amount;
        r.ValidFrom     = DateOnly.Parse(req.ValidFrom);
        r.ValidTo       = DateOnly.TryParse(req.ValidTo, out var d) ? d : null;
        r.Priority      = req.Priority;
        r.IsActive      = req.IsActive;
        r.Notes         = req.Notes?.Trim();
        r.UpdatedAt     = DateTime.UtcNow;
        r.UpdatedBy     = CurrentUserId();

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "اتعدّلت قاعدة النولون" });
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var r = await _db.DriverFreightRules.FirstOrDefaultAsync(x => x.DriverFreightRuleId == id && !x.IsDeleted, ct);
        if (r is null) return NotFound(new { message = "القاعدة مش موجودة" });

        /* مافيش جدول تاني بيشير للقواعد — الحذف الناعم آمن في أي وقت،
           والرحلات اللي اتسعرت قبل كده محتفظة بنولونها (Trip.FreightAmount). */
        r.IsDeleted = true;
        r.UpdatedAt = DateTime.UtcNow;
        r.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "اتحذفت قاعدة النولون" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(Upsert? r, CancellationToken ct, int? currentId = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (r.Amount <= 0) return "مبلغ النولون لازم يكون أكبر من صفر";
        if (r.Priority < 0) return "الأولوية ماينفعش تكون بالسالب";
        if (!DateOnly.TryParse(r.ValidFrom, out var from)) return "تاريخ «ساري من» غير صالح";
        DateOnly? to = null;
        if (!string.IsNullOrWhiteSpace(r.ValidTo))
        {
            if (!DateOnly.TryParse(r.ValidTo, out var d)) return "تاريخ «ساري لحد» غير صالح";
            to = d;
            if (to < from) return "«ساري لحد» قبل «ساري من»";
        }

        // الأبعاد المختارة لازم تكون موجودة فعلًا
        if (r.PortId is > 0 && !await _db.Ports.AnyAsync(p => p.PortId == r.PortId, ct))
            return "الميناء مش موجود";
        if (r.DestinationId is > 0 && !await _db.Destinations.AnyAsync(d => d.DestinationId == r.DestinationId, ct))
            return "الوجهة مش موجودة";
        if (r.TripTypeId is > 0 && !await _db.TripTypes.AnyAsync(t => t.TripTypeId == r.TripTypeId, ct))
            return "نوع الرحلة مش موجود";

        /* قاعدة مكررة = نفس الأبعاد بالظبط + فترة متداخلة + شغالة —
           هيخلي الاقتراح التلقائي محتار من غير لازمة */
        var dup = await _db.DriverFreightRules.AsNoTracking().AnyAsync(x =>
            !x.IsDeleted && x.IsActive && x.PortId == r.PortId &&
            x.DestinationId == r.DestinationId && x.TripTypeId == r.TripTypeId &&
            x.ValidFrom <= (to ?? DateOnly.MaxValue) && (x.ValidTo == null || x.ValidTo >= from) &&
            (currentId == null || x.DriverFreightRuleId != currentId), ct);
        if (dup) return "فيه قاعدة شغالة بنفس الأبعاد وفترة متداخلة — عدّل الموجودة بدل ما تعمل جديدة";

        return null;
    }
}
