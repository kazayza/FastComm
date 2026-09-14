using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Fleet;

/// <summary>
/// صيانة العربات — جدول `VehicleMaintenance` كان موجود من غير أي endpoints.
/// </summary>
/// <remarks>
/// <para><b>الترقيم</b> بييجي من <c>usp_GetNextNumber('MAINT')</c> زي باقي المستندات.
/// 🔴 **لازم سلاسل سنة `MAINT` تكون موجودة** وإلا هيرمي
/// «سلسلة الترقيم غير معرّفة لهذا النوع» — استخدم «تجهيز سنة جديدة» في شاشة الترقيم.</para>
///
/// <para><b>الحالة</b> محصورة بـ CHECK: <c>Scheduled | InProgress | Completed | Cancelled</c>.</para>
///
/// <para><b>التكلفة هنا سجل بس</b> — مش بتتسجّل مصروف تلقائيًا. لو عايز التكلفة
/// تدخل الخزينة، اعمل مصروف من شاشة المصروفات واربطه بالمورد.</para>
/// </remarks>
[ApiController]
[Route("api/fleet/maintenance")]
[Authorize]
public class MaintenanceController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly INumberingService _num;
    public MaintenanceController(FastComDbContext db, INumberingService num)
    { _db = db; _num = num; }

    private static int UserId(ClaimsPrincipal u) =>
        int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    // ═══════════════ DTOs ═══════════════

    public record Item(long MaintenanceId, string MaintenanceNumber,
        int VehicleId, string VehiclePlate, string? VehicleModel,
        int BranchId, DateOnly MaintenanceDate, string MaintenanceType,
        int? SupplierId, string? SupplierName, decimal? Odometer, decimal Cost,
        DateOnly? NextDueDate, decimal? NextDueOdometer, string? Description,
        string Status, int DaysToDue, DateTime CreatedAt);

    public record Upsert(int VehicleId, int BranchId, string MaintenanceDate, string MaintenanceType,
        int? SupplierId, decimal? Odometer, decimal Cost,
        string? NextDueDate, decimal? NextDueOdometer, string? Description, string Status);

    private static readonly string[] Types =
    {
        "Oil", "Filter", "Brake", "Tire", "Battery", "Coolant",
        "Transmission", "Electrical", "Body", "Inspection", "Other"
    };
    private static readonly string[] Statuses = { "Scheduled", "InProgress", "Completed", "Cancelled" };

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:MAINTENANCE.VIEW")]
    public async Task<IActionResult> List(
        [FromQuery] string? status, [FromQuery] int? vehicleId,
        [FromQuery] int? dueWithinDays, CancellationToken ct)
    {
        var q = _db.VehicleMaintenances.AsNoTracking().Where(m => !m.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && Statuses.Contains(status))
            q = q.Where(m => m.Status == status);
        if (vehicleId is > 0)
            q = q.Where(m => m.VehicleId == vehicleId);

        var raw = await q.OrderByDescending(m => m.MaintenanceDate).Take(1000).ToListAsync(ct);

        /* 🔴 الأسماء باستعلامات منفصلة — Join بعد الاستعلام مش بيتترجم في EF8 */
        var vehIds = raw.Select(m => m.VehicleId).Distinct().ToList();
        var supIds = raw.Where(m => m.SupplierId != null).Select(m => m.SupplierId!.Value).Distinct().ToList();

        var veh = (await _db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.VehicleId))
            .Select(v => new { v.VehicleId, v.PlateNumber, v.Model }).ToListAsync(ct))
            .ToDictionary(x => x.VehicleId, x => x);
        var sup = (await _db.Suppliers.AsNoTracking().Where(s => supIds.Contains(s.SupplierId))
            .Select(s => new { s.SupplierId, s.NameAr }).ToListAsync(ct))
            .ToDictionary(x => x.SupplierId, x => x.NameAr);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        /* فلتر «مستحقة قريبًا» بيشتغل على `NextDueDate` — بنعمله بعد الجلب
           لأن `DateOnly` مابيتترجمش في عمليات طرح داخل EF. */
        IEnumerable<VehicleMaintenance> res = raw;
        if (dueWithinDays is > 0)
        {
            var limit = today.AddDays(dueWithinDays.Value);
            res = raw.Where(m => m.NextDueDate is not null &&
                                 m.NextDueDate >= today && m.NextDueDate <= limit &&
                                 m.Status != "Cancelled");
        }

        return Ok(res.Select(m => new Item(
            m.MaintenanceId, m.MaintenanceNumber,
            m.VehicleId,
            veh.TryGetValue(m.VehicleId, out var v) ? v.PlateNumber : "?",
            veh.TryGetValue(m.VehicleId, out var v2) ? v2.Model : null,
            m.BranchId, m.MaintenanceDate, m.MaintenanceType,
            m.SupplierId,
            m.SupplierId is not null && sup.TryGetValue(m.SupplierId.Value, out var sn) ? sn : null,
            m.Odometer, m.Cost, m.NextDueDate, m.NextDueOdometer, m.Description,
            m.Status,
            /* 🔴 CS1061: `NextDueDate` نوعها `DateOnly?` — لازم `.Value`
                  (الـ null check اللي قبلها بيضيّق النوع منطقيًا بس
                  الـ compiler مش بيضيّقه في الـ ternary هنا) */
            m.NextDueDate is null ? int.MaxValue : m.NextDueDate.Value.DayNumber - today.DayNumber,
            m.CreatedAt)).ToList());
    }

    /// <summary>ملخص للـ KPIs — عدد المستحق واللي جاي وتكلفة الشهر.</summary>
    [HttpGet("summary")]
    [Authorize(Policy = "PERM:MAINTENANCE.VIEW")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var all = await _db.VehicleMaintenances.AsNoTracking().Where(m => !m.IsDeleted).ToListAsync(ct);

        return Ok(new
        {
            total      = all.Count,
            open       = all.Count(m => m.Status == "Scheduled" || m.Status == "InProgress"),
            overdue    = all.Count(m => m.NextDueDate is not null && m.NextDueDate < today
                                        && m.Status != "Cancelled"),
            dueSoon    = all.Count(m => m.NextDueDate is not null && m.NextDueDate >= today
                                        && m.NextDueDate <= today.AddDays(30) && m.Status != "Cancelled"),
            monthCost  = all.Where(m => m.MaintenanceDate >= monthStart).Sum(m => m.Cost)
        });
    }

    /// <summary>العربات اللي صيانتها جاية — للتنبيه في الأسطول.</summary>
    [HttpGet("due-vehicles")]
    [Authorize(Policy = "PERM:MAINTENANCE.VIEW")]
    public async Task<IActionResult> DueVehicles([FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (days is < 1 or > 365) return BadRequest(new { message = "الأيام من 1 لـ 365" });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var limit = today.AddDays(days);

        var raw = await _db.VehicleMaintenances.AsNoTracking()
            .Where(m => !m.IsDeleted && m.Status != "Cancelled" && m.NextDueDate != null)
            .ToListAsync(ct);

        var due = raw.Where(m => m.NextDueDate! <= limit)
                     .GroupBy(m => m.VehicleId)
                     .Select(g => new { Id = g.Key, Next = g.Min(m => m.NextDueDate!.Value) })
                     .ToList();

        var ids = due.Select(d => d.Id).ToList();
        var veh = (await _db.Vehicles.AsNoTracking().Where(v => ids.Contains(v.VehicleId))
            .Select(v => new { v.VehicleId, v.PlateNumber, v.Model }).ToListAsync(ct))
            .ToDictionary(x => x.VehicleId, x => x);

        return Ok(due.OrderBy(d => d.Next).Select(d => new
        {
            vehicleId    = d.Id,
            plateNumber  = veh.TryGetValue(d.Id, out var v) ? v.PlateNumber : "?",
            model        = veh.TryGetValue(d.Id, out var v2) ? v2.Model : null,
            nextDueDate  = d.Next,
            daysLeft     = d.Next.DayNumber - today.DayNumber
        }).ToList());
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:MAINTENANCE.CREATE")]
    public async Task<IActionResult> Create([FromBody] Upsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        /* 🔴 اسم السلسلة لازم يطابق `NumberSequences.DocumentType` حرفيًا —
         *    المزروع في الـ schema هو 'MAINTENANCE' (مش 'MAINT').
         *    غلطة سابقة هنا كانت بترمي SqlException 50002 → 500 غير مفهوم. */
        string number;
        try
        {
            number = await _num.NextAsync("MAINTENANCE", ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var m = new VehicleMaintenance
        {
            MaintenanceNumber = number,
            VehicleId         = req.VehicleId,
            BranchId          = req.BranchId,
            MaintenanceDate   = DateOnly.Parse(req.MaintenanceDate),
            MaintenanceType   = req.MaintenanceType,
            SupplierId        = req.SupplierId,
            Odometer          = req.Odometer,
            Cost              = req.Cost,
            NextDueDate       = string.IsNullOrWhiteSpace(req.NextDueDate) ? null : DateOnly.Parse(req.NextDueDate),
            NextDueOdometer   = req.NextDueOdometer,
            Description       = req.Description?.Trim(),
            Status            = req.Status,
            CreatedBy         = UserId(User),
            CreatedAt         = DateTime.UtcNow
        };

        _db.VehicleMaintenances.Add(m);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = m.MaintenanceId, message = $"✅ اتسجّلت الصيانة برقم {number}" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:long}")]
    [Authorize(Policy = "PERM:MAINTENANCE.EDIT")]
    public async Task<IActionResult> Update(long id, [FromBody] Upsert req, CancellationToken ct)
    {
        var m = await _db.VehicleMaintenances.FirstOrDefaultAsync(x => x.MaintenanceId == id && !x.IsDeleted, ct);
        if (m is null) return NotFound(new { message = "سجل الصيانة غير موجود" });

        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        m.VehicleId       = req.VehicleId;
        m.BranchId        = req.BranchId;
        m.MaintenanceDate = DateOnly.Parse(req.MaintenanceDate);
        m.MaintenanceType = req.MaintenanceType;
        m.SupplierId      = req.SupplierId;
        m.Odometer        = req.Odometer;
        m.Cost            = req.Cost;
        m.NextDueDate     = string.IsNullOrWhiteSpace(req.NextDueDate) ? null : DateOnly.Parse(req.NextDueDate);
        m.NextDueOdometer = req.NextDueOdometer;
        m.Description     = req.Description?.Trim();
        m.Status          = req.Status;
        m.UpdatedAt       = DateTime.UtcNow;
        m.UpdatedBy       = UserId(User);

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعدّل سجل الصيانة {m.MaintenanceNumber}" });
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:MAINTENANCE.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var m = await _db.VehicleMaintenances.FirstOrDefaultAsync(x => x.MaintenanceId == id && !x.IsDeleted, ct);
        if (m is null) return NotFound(new { message = "سجل الصيانة غير موجود" });

        m.IsDeleted = true;
        m.UpdatedAt = DateTime.UtcNow;
        m.UpdatedBy = UserId(User);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتحذف سجل الصيانة {m.MaintenanceNumber}" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(Upsert? r, CancellationToken ct)
    {
        if (r is null) return "البيانات غير كاملة";
        if (r.VehicleId <= 0) return "العربة مطلوبة";
        if (r.BranchId <= 0)  return "الفرع مطلوب";
        if (!DateOnly.TryParse(r.MaintenanceDate, out var d)) return "تاريخ الصيانة غير صالح";
        if (d > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            return "تاريخ الصيانة ماينفعش يكون في المستقبل";

        if (!Types.Contains(r.MaintenanceType))
            return "نوع الصيانة لازم يكون واحد من: " + string.Join(" · ", Types);
        if (!Statuses.Contains(r.Status))
            return "الحالة لازم تكون واحدة من: " + string.Join(" · ", Statuses);

        if (r.Cost < 0) return "التكلفة ماينفعش تكون سالب";
        if (r.Odometer is < 0) return "قراءة العداد ماينفعش تكون سالب";
        if (r.NextDueOdometer is < 0) return "العداد القادم ماينفعش يكون سالب";
        if (r.Description is { Length: > 1000 }) return "الوصف أطول من 1000 حرف";

        if (!await _db.Vehicles.AnyAsync(v => v.VehicleId == r.VehicleId && !v.IsDeleted, ct))
            return "العربة غير موجودة";
        if (!await _db.Branches.AnyAsync(b => b.BranchId == r.BranchId && !b.IsDeleted, ct))
            return "الفرع غير موجود";
        if (r.SupplierId is not null && !await _db.Suppliers.AnyAsync(s => s.SupplierId == r.SupplierId && !s.IsDeleted, ct))
            return "المورد غير موجود";

        if (!string.IsNullOrWhiteSpace(r.NextDueDate))
        {
            if (!DateOnly.TryParse(r.NextDueDate, out var nd)) return "تاريخ الصيانة القادمة غير صالح";
            if (nd <= d) return "تاريخ الصيانة القادمة لازم يكون بعد تاريخ الصيانة";
        }
        if (r.NextDueOdometer is not null && r.Odometer is not null && r.NextDueOdometer <= r.Odometer)
            return "العداد القادم لازم يكون أكتر من العداد الحالي";

        return null;
    }
}
