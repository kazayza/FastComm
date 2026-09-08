using System.Security.Claims;
using System.Text.RegularExpressions;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// الحاويات — الرقم الفعلي للحاوية (مش النوع).
/// الرقم بييجي من الخط الملاحي، فمافيش ترقيم تلقائي — بس منع تكرار.
/// </summary>
[ApiController]
[Route("api/containers")]
[Authorize]
public class ContainersController : ControllerBase
{
    private readonly FastComDbContext _db;
    public ContainersController(FastComDbContext db) => _db = db;

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static readonly string[] OwnerTypes = { "ShippingLine", "Customer", "Leased", "Company" };
    private static readonly string[] Statuses   = { "Available", "InTransit", "AtPort", "AtDepot", "Delivered", "Returned" };

    // ISO 6346: 4 حروف + 7 أرقام  (مثال: MSCU1234567)
    private static readonly Regex IsoNumber = new("^[A-Z]{4}[0-9]{7}$", RegexOptions.Compiled);

    // ═══════════════ DTOs ═══════════════

    public record ListItem(long ContainerId, string ContainerNumber, string TypeName, byte SizeFeet,
        bool IsReefer, string OwnerType, string? OwnerName, decimal? WeightKg, string CurrentStatus);

    public record Detail(long ContainerId, string ContainerNumber, int ContainerTypeId, string OwnerType,
        string? OwnerName, decimal? WeightKg, string CurrentStatus, string? Notes);

    public record Upsert(string ContainerNumber, int ContainerTypeId, string OwnerType,
        string? OwnerName, decimal? WeightKg, string CurrentStatus, string? Notes);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q, CancellationToken ct)
    {
        var query = _db.Containers.AsNoTracking().Where(c => !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(c => c.CurrentStatus == status);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(c => c.ContainerNumber.Contains(s) ||
                                     (c.OwnerName != null && c.OwnerName.Contains(s)));
        }

        var rows = await query
            .OrderByDescending(c => c.ContainerId)
            .Take(500)
            .Select(c => new ListItem(c.ContainerId, c.ContainerNumber, c.ContainerType.NameAr,
                c.ContainerType.SizeFeet, c.ContainerType.IsReefer,
                c.OwnerType, c.OwnerName, c.WeightKg, c.CurrentStatus))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ONE ═══════════════

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var c = await _db.Containers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ContainerId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "الحاوية مش موجودة" });

        return Ok(new Detail(c.ContainerId, c.ContainerNumber, c.ContainerTypeId,
            c.OwnerType, c.OwnerName, c.WeightKg, c.CurrentStatus, c.Notes));
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Create([FromBody] Upsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, null, ct);
        if (err is not null) return BadRequest(new { message = err });

        var c = new Container
        {
            ContainerNumber = req.ContainerNumber.Trim().ToUpperInvariant(),
            ContainerTypeId = req.ContainerTypeId,
            OwnerType       = req.OwnerType,
            OwnerName       = B(req.OwnerName),
            WeightKg        = req.WeightKg is > 0 ? req.WeightKg : null,
            CurrentStatus   = req.CurrentStatus,
            Notes           = B(req.Notes),
            CreatedBy       = CurrentUserId()
        };
        _db.Containers.Add(c);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = c.ContainerId, message = $"✅ اتسجلت الحاوية {c.ContainerNumber}" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:long}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Update(long id, [FromBody] Upsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, id, ct);
        if (err is not null) return BadRequest(new { message = err });

        var c = await _db.Containers.FirstOrDefaultAsync(x => x.ContainerId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "الحاوية مش موجودة" });

        c.ContainerNumber = req.ContainerNumber.Trim().ToUpperInvariant();
        c.ContainerTypeId = req.ContainerTypeId;
        c.OwnerType       = req.OwnerType;
        c.OwnerName       = B(req.OwnerName);
        c.WeightKg        = req.WeightKg is > 0 ? req.WeightKg : null;
        c.CurrentStatus   = req.CurrentStatus;
        c.Notes           = B(req.Notes);
        c.UpdatedAt       = DateTime.UtcNow;
        c.UpdatedBy       = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var c = await _db.Containers.FirstOrDefaultAsync(x => x.ContainerId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "الحاوية مش موجودة" });

        var usedInBooking = await _db.BookingContainerDetails
            .AnyAsync(d => d.ContainerId == id, ct);
        if (usedInBooking)
            return BadRequest(new { message = "الحاوية مربوطة بحجز — ماينفعش تتحذف" });

        c.IsDeleted = true;
        c.DeletedAt = DateTime.UtcNow;
        c.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اتحذفت الحاوية" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(Upsert? r, long? exceptId, CancellationToken ct)
    {
        if (r is null) return "البيانات مش كاملة";

        var number = B(r.ContainerNumber);
        if (number is null) return "رقم الحاوية مطلوب";

        number = number.ToUpperInvariant();
        if (!IsoNumber.IsMatch(number))
            return "رقم الحاوية لازم يكون 4 حروف إنجليزي وراهم 7 أرقام — مثال MSCU1234567";

        if (await _db.Containers.AnyAsync(c => c.ContainerNumber == number &&
                                               c.ContainerId != (exceptId ?? 0), ct))
            return "رقم الحاوية ده مسجل قبل كده";

        if (!await _db.ContainerTypes.AnyAsync(t => t.ContainerTypeId == r.ContainerTypeId, ct))
            return "نوع الحاوية مش موجود";

        if (!OwnerTypes.Contains(r.OwnerType)) return "جهة الملكية مش صالحة";
        if (!Statuses.Contains(r.CurrentStatus)) return "الحالة مش صالحة";
        if (r.WeightKg is < 0) return "الوزن ماينفعش يكون سالب";

        return null;
    }
}
