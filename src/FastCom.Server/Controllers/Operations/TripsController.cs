using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Auth;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Operations;

/// <summary>
/// الرحلات — سائق + عربية (+ مقطورة) وبينفّذوا كذا عملية في نفس الخروج.
/// <para>الرحلة هي المستوى اللي بتتعمل عليه العهدة، وبتتوزّع منه التكلفة على العمليات.</para>
/// </summary>
[ApiController]
[Route("api/trips")]
[Authorize]
public class TripsController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly INumberingService _numbers;
    private readonly IPermissionService _perms;

    public TripsController(FastComDbContext db, INumberingService numbers, IPermissionService perms)
    { _db = db; _numbers = numbers; _perms = perms; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateTime? Dt(string? s) => DateTime.TryParse(s, out var d) ? d : null;

    private static decimal? Dec(string? s) => decimal.TryParse(s, out var d) && d >= 0 ? d : null;

    private async Task<int?> ResolveBranchIdAsync(CancellationToken ct)
    {
        if (int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0) return b;
        return await _db.Branches.AsNoTracking()
            .OrderBy(x => x.BranchId).Select(x => (int?)x.BranchId).FirstOrDefaultAsync(ct);
    }

    // ═══════════════ DTOs ═══════════════

    public record TripOpDto(long OperationId, int SequenceNo, string? PickupAt, string? DeliveryAt, string? Notes);

    public record TripUpsert(int DriverId, int VehicleId, int? TrailerId, int? TripTypeId,
        string? PlannedStartAt, string? Notes, List<TripOpDto>? Operations);

    public record ListItem(long TripId, string TripNumber, string DriverName, string VehiclePlate,
        string? TrailerPlate, int OperationsCount, decimal DirectCost, decimal AllocatedCost,
        string Status, DateTime? PlannedStartAt, DateTime? ActualStartAt, DateTime? ActualEndAt);

    public record OpLine(long TripOperationId, long OperationId, string OperationNumber,
        string CustomerName, string Route, int SequenceNo, string? PickupAt, string? DeliveryAt,
        string Status, decimal RevenueNet);

    public record AllocLine(long TripCostAllocationId, long OperationId, string OperationNumber,
        string AllocationBasis, decimal AllocationPercent, decimal AllocatedAmount, decimal RevenueNet);

    public record Detail(long TripId, string TripNumber, int DriverId, string DriverName,
        int VehicleId, string VehiclePlate, int? TrailerId, string? TrailerPlate,
        int? TripTypeId, string? PlannedStartAt, string? ActualStartAt, string? ActualEndAt,
        decimal? StartOdometer, decimal? EndOdometer, decimal? TotalDistanceKm,
        string? Notes, string Status, decimal DirectCost);

    public record DetailResponse(Detail Trip, List<OpLine> Operations, List<AllocLine> Allocations);

    public record StartRequest(string? ActualStartAt, string? StartOdometer);
    public record CompleteRequest(string? ActualEndAt, string? EndOdometer);
    public record DistributeRequest(string Basis, List<AllocInput>? Manual);
    public record AllocInput(long OperationId, decimal Percent);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:TRIP.VIEW")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q,
        [FromQuery] int take = 200, CancellationToken ct = default)
    {
        if (take is < 1 or > 500) take = 200;

        var query = _db.Trips.AsNoTracking().Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(t =>
                t.TripNumber.Contains(s) ||
                t.Driver.FullName.Contains(s) ||
                t.Vehicle.PlateNumber.Contains(s));
        }

        var rows = await query
            .OrderByDescending(t => t.TripId)
            .Take(take)
            .Select(t => new ListItem(
                t.TripId, t.TripNumber, t.Driver.FullName, t.Vehicle.PlateNumber,
                t.Trailer != null ? t.Trailer.PlateNumber : null,
                t.TripOperations.Count,
                _db.Expenses.Where(e => e.TripId == t.TripId && e.Status != "Cancelled" && !e.IsDeleted)
                            .Sum(e => (decimal?)e.Amount) ?? 0m,
                _db.TripCostAllocations.Where(a => a.TripId == t.TripId)
                            .Sum(a => (decimal?)a.AllocatedAmount) ?? 0m,
                t.Status, t.PlannedStartAt, t.ActualStartAt, t.ActualEndAt))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ONE ═══════════════

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:TRIP.VIEW")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var t = await _db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TripId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "الرحلة مش موجودة" });

        var driverName = await _db.Drivers.AsNoTracking()
            .Where(d => d.DriverId == t.DriverId).Select(d => d.FullName).FirstOrDefaultAsync(ct) ?? "";
        var plate = await _db.Vehicles.AsNoTracking()
            .Where(v => v.VehicleId == t.VehicleId).Select(v => v.PlateNumber).FirstOrDefaultAsync(ct) ?? "";
        string? trailerPlate = null;
        if (t.TrailerId is not null)
            trailerPlate = await _db.Trailers.AsNoTracking()
                .Where(x => x.TrailerId == t.TrailerId)
                .Select(x => x.PlateNumber ?? x.TrailerCode).FirstOrDefaultAsync(ct);

        var ops = await _db.TripOperations.AsNoTracking()
            .Where(to => to.TripId == id)
            .OrderBy(to => to.SequenceNo)
            .Select(to => new OpLine(to.TripOperationId, to.OperationId, to.Operation.OperationNumber,
                to.Operation.Customer.NameAr,
                (to.Operation.Port != null ? to.Operation.Port.NameAr : "—") + " ← " +
                (to.Operation.Destination != null ? to.Operation.Destination.NameAr : "—"),
                to.SequenceNo,
                to.PickupAt != null ? to.PickupAt.Value.ToString("yyyy-MM-ddTHH:mm") : null,
                to.DeliveryAt != null ? to.DeliveryAt.Value.ToString("yyyy-MM-ddTHH:mm") : null,
                to.Status, to.Operation.RevenueNet))
            .ToListAsync(ct);

        var allocs = await _db.TripCostAllocations.AsNoTracking()
            .Where(a => a.TripId == id)
            .OrderBy(a => a.Operation.OperationNumber)
            .Select(a => new AllocLine(a.TripCostAllocationId, a.OperationId, a.Operation.OperationNumber,
                a.AllocationBasis, a.AllocationPercent, a.AllocatedAmount, a.Operation.RevenueNet))
            .ToListAsync(ct);

        var directCost = await _db.Expenses
            .Where(e => e.TripId == id && e.Status != "Cancelled" && !e.IsDeleted)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        var detail = new Detail(t.TripId, t.TripNumber, t.DriverId, driverName,
            t.VehicleId, plate, t.TrailerId, trailerPlate, t.TripTypeId,
            t.PlannedStartAt?.ToString("yyyy-MM-ddTHH:mm"),
            t.ActualStartAt?.ToString("yyyy-MM-ddTHH:mm"),
            t.ActualEndAt?.ToString("yyyy-MM-ddTHH:mm"),
            t.StartOdometer, t.EndOdometer, t.TotalDistanceKm,
            t.Notes, t.Status, directCost);

        return Ok(new DetailResponse(detail, ops, allocs));
    }

    // ═══════════════ عمليات جاهزة تتربط برحلة ═══════════════

    [HttpGet("available-operations")]
    [Authorize(Policy = "PERM:TRIP.ASSIGN")]
    public async Task<IActionResult> AvailableOperations([FromQuery] long? exceptTripId, CancellationToken ct)
    {
        var rows = await _db.Operations.AsNoTracking()
            .Where(o => !o.IsDeleted &&
                        (o.Status == "Pending" || o.Status == "Assigned"))
            .Where(o => !_db.TripOperations.Any(to => to.OperationId == o.OperationId &&
                                                      to.Status != "Cancelled" &&
                                                      (exceptTripId == null || to.TripId != exceptTripId)))
            .OrderByDescending(o => o.OperationId)
            .Take(150)
            .Select(o => new
            {
                o.OperationId, o.OperationNumber,
                CustomerName = o.Customer.NameAr,
                Route = (o.Port != null ? o.Port.NameAr : "—") + " ← " +
                        (o.Destination != null ? o.Destination.NameAr : "—"),
                o.RevenueNet
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:TRIP.CREATE")]
    public async Task<IActionResult> Create([FromBody] TripUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var t = new Trip
        {
            TripNumber    = await _numbers.NextAsync("TRIP", ct),
            BranchId      = branchId.Value,
            DriverId      = req.DriverId,
            VehicleId     = req.VehicleId,
            TrailerId     = req.TrailerId,
            TripTypeId    = req.TripTypeId,
            PlannedStartAt = Dt(req.PlannedStartAt),
            Notes         = B(req.Notes),
            Status        = "Planned",
            CreatedBy     = CurrentUserId()
        };
        // 🔴 Atomicity: الرحلة + ربط العمليات في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.Trips.Add(t);
            await _db.SaveChangesAsync(ct);

            if (req.Operations is { Count: > 0 })
                await AttachOperationsAsync(t.TripId, req.Operations, ct);

            await _db.SaveChangesAsync(ct);

            await _db.Database.CommitTransactionAsync(ct);

            return Ok(new { id = t.TripId, number = t.TripNumber,
                message = $"✅ اتفتحت الرحلة برقم {t.TripNumber}" });
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:long}")]
    [Authorize(Policy = "PERM:TRIP.EDIT")]
    public async Task<IActionResult> Update(long id, [FromBody] TripUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var t = await _db.Trips.FirstOrDefaultAsync(x => x.TripId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "الرحلة مش موجودة" });

        if (t.Status is "Completed" or "Cancelled")
            return BadRequest(new { message = "الرحلة خلصت — التعديل مقفول" });

        t.DriverId      = req.DriverId;
        t.VehicleId     = req.VehicleId;
        t.TrailerId     = req.TrailerId;
        t.TripTypeId    = req.TripTypeId;
        t.PlannedStartAt = Dt(req.PlannedStartAt);
        t.Notes         = B(req.Notes);
        t.UpdatedAt     = DateTime.UtcNow;
        t.UpdatedBy     = CurrentUserId();

        // 🔴 Atomicity: تعديل الرحلة كامل (الربط + الترتيب) في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (req.Operations is not null)
            {
                var keep = req.Operations.Select(o => o.OperationId).ToList();

                // اللي اتشال من الرحلة → يرجع قيد الانتظار
                var removed = await _db.TripOperations
                    .Where(to => to.TripId == id && !keep.Contains(to.OperationId))
                    .ToListAsync(ct);
                if (removed.Count > 0)
                {
                    _db.TripOperations.RemoveRange(removed);
                    await _db.SaveChangesAsync(ct);
                    await SetOperationsStatusAsync(removed.Select(r => r.OperationId).ToList(), "Pending", ct);
                }

                await AttachOperationsAsync(id, req.Operations, ct);
            }

            await _db.SaveChangesAsync(ct);

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    private async Task AttachOperationsAsync(long tripId, List<TripOpDto> ops, CancellationToken ct)
    {
        var existing = await _db.TripOperations
            .Where(to => to.TripId == tripId).ToListAsync(ct);

        var seq = 0;
        foreach (var o in ops.OrderBy(x => x.SequenceNo))
        {
            seq++;
            var line = existing.FirstOrDefault(x => x.OperationId == o.OperationId);
            if (line is null)
            {
                _db.TripOperations.Add(new TripOperation
                {
                    TripId      = tripId,
                    OperationId = o.OperationId,
                    SequenceNo  = seq,
                    PickupAt    = Dt(o.PickupAt),
                    DeliveryAt  = Dt(o.DeliveryAt),
                    Status      = "Assigned",
                    Notes       = B(o.Notes),
                    CreatedBy   = CurrentUserId()
                });
            }
            else
            {
                line.SequenceNo = seq;
                line.PickupAt   = Dt(o.PickupAt);
                line.DeliveryAt = Dt(o.DeliveryAt);
            }
        }

        await SetOperationsStatusAsync(ops.Select(o => o.OperationId).Distinct().ToList(), "Assigned", ct);
    }

    private async Task SetOperationsStatusAsync(List<long> ids, string status, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var list = await _db.Operations
            .Where(o => ids.Contains(o.OperationId) && !o.IsDeleted).ToListAsync(ct);
        foreach (var o in list)
        {
            // مانرجّعش عملية اتسلّمت أو اتقفلت
            if (o.Status is "Delivered" or "Closed" or "Cancelled") continue;
            o.Status    = status;
            o.UpdatedAt = DateTime.UtcNow;
            o.UpdatedBy = CurrentUserId();
        }
    }

    // ═══════════════ STATUS ═══════════════

    [HttpPost("{id:long}/start")]
    [Authorize(Policy = "PERM:TRIP.EDIT")]
    public async Task<IActionResult> Start(long id, [FromBody] StartRequest? req, CancellationToken ct)
    {
        var t = await _db.Trips.FirstOrDefaultAsync(x => x.TripId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "الرحلة مش موجودة" });
        if (t.Status is not ("Planned" or "Assigned"))
            return BadRequest(new { message = "الرحلة مش في حالة تسمح بالتحرك" });

        if (!await _db.TripOperations.AnyAsync(to => to.TripId == id && to.Status != "Cancelled", ct))
            return BadRequest(new { message = "اربط عملية واحدة على الأقل بالرحلة" });

        var start = Dt(req?.ActualStartAt) ?? DateTime.UtcNow;
        var odo = Dec(req?.StartOdometer);

        // 🔴 Atomicity: تحريك الرحلة + تحديث حالة العمليات في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            t.ActualStartAt  = start;
            t.StartOdometer  = odo;
            t.Status         = "Started";
            t.UpdatedAt      = DateTime.UtcNow;
            t.UpdatedBy      = CurrentUserId();
            await _db.SaveChangesAsync(ct);

            var ids = await _db.TripOperations.Where(to => to.TripId == id)
                .Select(to => to.OperationId).ToListAsync(ct);
            await SetOperationsStatusAsync(ids, "InTransit", ct);
            await _db.SaveChangesAsync(ct);

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { message = "✅ الرحلة اتحركت" });
    }

    [HttpPost("{id:long}/complete")]
    [Authorize(Policy = "PERM:TRIP.EDIT")]
    public async Task<IActionResult> Complete(long id, [FromBody] CompleteRequest? req, CancellationToken ct)
    {
        var t = await _db.Trips.FirstOrDefaultAsync(x => x.TripId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "الرحلة مش موجودة" });
        if (t.Status is not ("Started" or "Assigned" or "Planned"))
            return BadRequest(new { message = "الرحلة مش شغالة" });

        var end = Dt(req?.ActualEndAt) ?? DateTime.UtcNow;
        var odo = Dec(req?.EndOdometer);

        // CK_Trips_Dates / CK_Trips_Odometer
        if (t.ActualStartAt is not null && end < t.ActualStartAt)
            return BadRequest(new { message = "وقت النهاية قبل البداية" });
        if (odo is not null && t.StartOdometer is not null && odo < t.StartOdometer)
            return BadRequest(new { message = "عداد النهاية أقل من البداية" });

        // 🔴 Atomicity: إتمام الرحلة + تسليم العمليات في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            t.ActualEndAt = end;
            t.EndOdometer = odo;
            if (odo is not null && t.StartOdometer is not null)
                t.TotalDistanceKm = odo.Value - t.StartOdometer.Value;
            t.Status    = "Completed";
            t.UpdatedAt = DateTime.UtcNow;
            t.UpdatedBy = CurrentUserId();
            await _db.SaveChangesAsync(ct);

            var ids = await _db.TripOperations.Where(to => to.TripId == id)
                .Select(to => to.OperationId).ToListAsync(ct);
            await SetOperationsStatusAsync(ids, "Delivered", ct);
            await _db.SaveChangesAsync(ct);

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { message = "✅ الرحلة خلصت — العمليات بقت مسلّمة" });
    }

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = "PERM:TRIP.CANCEL")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        var t = await _db.Trips.FirstOrDefaultAsync(x => x.TripId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "الرحلة مش موجودة" });
        if (t.Status is "Completed") return BadRequest(new { message = "الرحلة خلصت — ماينفعش تتلغى" });
        if (t.Status is "Cancelled") return BadRequest(new { message = "الرحلة ملغاة بالفعل" });

        var hasCustody = await _db.DriverCustodies
            .AnyAsync(c => c.TripId == id && !c.IsDeleted, ct);
        if (hasCustody) return BadRequest(new { message = "فيه عهدة على الرحلة — سوّيها الأول" });

        // 🔴 Atomicity: إلغاء الرحلة + إرجاع العمليات قيد الانتظار في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            t.Status    = "Cancelled";
            t.UpdatedAt = DateTime.UtcNow;
            t.UpdatedBy = CurrentUserId();
            await _db.SaveChangesAsync(ct);

            var ids = await _db.TripOperations.Where(to => to.TripId == id)
                .Select(to => to.OperationId).ToListAsync(ct);
            await SetOperationsStatusAsync(ids, "Pending", ct);
            await _db.SaveChangesAsync(ct);

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { message = "✅ اتلغت الرحلة والعمليات رجعت قيد الانتظار" });
    }

    // ═══════════════ توزيع تكلفة الرحلة ═══════════════

    [HttpPost("{id:long}/distribute")]
    [Authorize(Policy = "PERM:TRIP.EDIT")]
    public async Task<IActionResult> Distribute(long id, [FromBody] DistributeRequest req, CancellationToken ct)
    {
        var basis = req?.Basis?.Trim();
        if (basis is not ("Manual" or "Weight" or "ContainerCount" or "RevenueShare"))
            return BadRequest(new { message = "طريقة التوزيع مش صالحة" });

        var t = await _db.Trips.FirstOrDefaultAsync(x => x.TripId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "الرحلة مش موجودة" });

        var opIds = await _db.TripOperations.Where(to => to.TripId == id)
            .Select(to => to.OperationId).ToListAsync(ct);
        if (opIds.Count == 0)
            return BadRequest(new { message = "لايوجد عمليات مربوطة بالرحلة" });

        var total = await _db.Expenses
            .Where(e => e.TripId == id && e.Status != "Cancelled" && !e.IsDeleted)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        if (total <= 0)
            return BadRequest(new { message = "مافيش مصروفات على الرحلة — سجل المصروفات الأول" });

        var ops = await _db.Operations.AsNoTracking()
            .Where(o => opIds.Contains(o.OperationId))
            .Select(o => new { o.OperationId, o.RevenueNet })
            .ToListAsync(ct);

        // ── الأوزان ──
        decimal[] weights = new decimal[ops.Count];
        if (basis == "RevenueShare")
        {
            for (var i = 0; i < ops.Count; i++) weights[i] = ops[i].RevenueNet > 0 ? ops[i].RevenueNet : 0m;
            if (weights.Sum() <= 0)
                return BadRequest(new { message = "الإيراد كله صفر — استخدم التوزيع المتساوي (Manual)" });
        }
        else if (basis == "ContainerCount")
        {
            var counts = await _db.OperationContainers
                .Where(oc => opIds.Contains(oc.OperationId) && oc.Status != "Cancelled")
                .GroupBy(oc => oc.OperationId)
                .Select(g => new { OperationId = g.Key, Cnt = g.Count() })
                .ToListAsync(ct);
            for (var i = 0; i < ops.Count; i++)
                weights[i] = counts.FirstOrDefault(c => c.OperationId == ops[i].OperationId)?.Cnt ?? 0;
            if (weights.Sum() <= 0)
                return BadRequest(new { message = "مافيش حاويات مربوطة بالعمليات — استخدم التوزيع المتساوي" });
        }
        else // Manual / Weight بدون أوزان → متساوي
        {
            for (var i = 0; i < ops.Count; i++) weights[i] = 1m;
        }

        // ── Manual: النسب من المستخدم ──
        if (basis == "Manual" && req!.Manual is { Count: > 0 })
        {
            var sumPct = req.Manual.Sum(m => m.Percent);
            if (sumPct <= 0 || sumPct > 100.0001m)
                return BadRequest(new { message = "مجموع النسب لازم يكون 100%" });
            for (var i = 0; i < ops.Count; i++)
            {
                var m = req.Manual.FirstOrDefault(x => x.OperationId == ops[i].OperationId);
                weights[i] = m?.Percent ?? 0m;
            }
        }

        var wSum = weights.Sum();
        if (wSum <= 0) return BadRequest(new { message = "مافيش أساس للتوزيع" });

        // ── احسب المبالغ، والباقي يتحط على آخر عملية عشان المجموع = المصروفات بالظبط ──
        var amounts = new decimal[ops.Count];
        var running = 0m;
        for (var i = 0; i < ops.Count; i++)
        {
            amounts[i] = i == ops.Count - 1
                ? total - running
                : Math.Round(total * weights[i] / wSum, 4, MidpointRounding.AwayFromZero);
            running += amounts[i];
        }

        // 🔴 Atomicity: توزيع التكلفة (حذف القديم + إضافة الجديد) في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var old = await _db.TripCostAllocations.Where(a => a.TripId == id).ToListAsync(ct);
            _db.TripCostAllocations.RemoveRange(old);
            await _db.SaveChangesAsync(ct);      // الـ trigger بيصفّر ActualCost

            for (var i = 0; i < ops.Count; i++)
            {
                _db.TripCostAllocations.Add(new TripCostAllocation
                {
                    TripId            = id,
                    OperationId       = ops[i].OperationId,
                    AllocationBasis   = basis!,
                    AllocationPercent = Math.Round(weights[i] / wSum * 100m, 4, MidpointRounding.AwayFromZero),
                    AllocatedAmount   = amounts[i] < 0 ? 0 : amounts[i],
                    CreatedBy         = CurrentUserId()
                });
            }
            await _db.SaveChangesAsync(ct);      // الـ trigger بيحدّث ActualCost لكل عملية

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { message = $"✅ اتوزّعت {total:N2} على {ops.Count} عملية", total });
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:TRIP.CANCEL")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var t = await _db.Trips.FirstOrDefaultAsync(x => x.TripId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "الرحلة مش موجودة" });
        if (t.Status is not ("Planned" or "Cancelled"))
            return BadRequest(new { message = "الرحلة اللي اشتغلت بتتلغى مش بتتحذف" });

        if (await _db.DriverCustodies.AnyAsync(c => c.TripId == id && !c.IsDeleted, ct))
            return BadRequest(new { message = "فيه عهدة على الرحلة" });

        // 🔴 Atomicity: حذف الرحلة + روابطها + توزيعاتها + إرجاع العمليات في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var links = await _db.TripOperations.Where(to => to.TripId == id).ToListAsync(ct);
            _db.TripOperations.RemoveRange(links);

            var allocs = await _db.TripCostAllocations.Where(a => a.TripId == id).ToListAsync(ct);
            _db.TripCostAllocations.RemoveRange(allocs);

            t.IsDeleted = true;
            t.DeletedAt = DateTime.UtcNow;
            t.DeletedBy = CurrentUserId();
            await _db.SaveChangesAsync(ct);

            await SetOperationsStatusAsync(links.Select(l => l.OperationId).ToList(), "Pending", ct);
            await _db.SaveChangesAsync(ct);

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { message = "✅ اتحذفت الرحلة" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(TripUpsert? r, CancellationToken ct)
    {
        if (r is null) return "البيانات مش كاملة";
        if (r.DriverId <= 0) return "لازم تختار السائق";
        if (r.VehicleId <= 0) return "لازم تختار العربية";

        var driver = await _db.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DriverId == r.DriverId, ct);
        if (driver is null || driver.IsDeleted) return "السائق مش موجود";
        if (driver.Status != "Active") return "السائق مش نشط";

        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.VehicleId == r.VehicleId, ct);
        if (vehicle is null || vehicle.IsDeleted) return "العربية مش موجودة";
        if (vehicle.Status is "OutOfService") return "العربية خارج الخدمة";

        if (r.TrailerId is not null)
        {
            var tr = await _db.Trailers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TrailerId == r.TrailerId, ct);
            if (tr is null || tr.IsDeleted) return "المقطورة مش موجودة";
            if (tr.Status is "OutOfService") return "المقطورة خارج الخدمة";
        }

        if (r.TripTypeId is not null && !await _db.TripTypes.AnyAsync(x => x.TripTypeId == r.TripTypeId, ct))
            return "نوع الرحلة مش موجود";

        if (r.Operations is { Count: > 0 })
        {
            var ids = r.Operations.Select(o => o.OperationId).Distinct().ToList();
            if (ids.Count != r.Operations.Count) return "فيه عملية مكررة في القائمة";

            var found = await _db.Operations
                .Where(o => ids.Contains(o.OperationId) && !o.IsDeleted)
                .Select(o => o.OperationId).ToListAsync(ct);
            if (found.Count != ids.Count) return "فيه عملية مش موجودة";
        }

        return null;
    }
}
