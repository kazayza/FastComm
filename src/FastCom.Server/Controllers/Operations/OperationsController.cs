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
/// العمليات — الوحدة اللي بيتقاس عليها الإيراد والتكلفة والربح.
/// <para>RevenueNet / RevenueTax بتتحسب بـ trigger من سطور الإيراد،</para>
/// <para>و ActualCost بتتحسب بـ trigger من المصروفات + توزيع تكلفة الرحلات.</para>
/// </summary>
[ApiController]
[Route("api/operations")]
[Authorize]
public class OperationsController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly INumberingService _numbers;
    private readonly IPermissionService _perms;

    public OperationsController(FastComDbContext db, INumberingService numbers, IPermissionService perms)
    { _db = db; _numbers = numbers; _perms = perms; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateTime? Dt(string? s) =>
        DateTime.TryParse(s, out var d) ? d : null;

    private async Task<int?> ResolveBranchIdAsync(CancellationToken ct)
    {
        if (int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0) return b;
        return await _db.Branches.AsNoTracking()
            .OrderBy(x => x.BranchId).Select(x => (int?)x.BranchId).FirstOrDefaultAsync(ct);
    }

    // ═══════════════ DTOs ═══════════════

    public record RevenueLineDto(int ServiceId, string? Description, decimal Quantity,
        decimal UnitPrice, decimal Discount, int? TaxRateId, long? PriceRuleId);

    public record OperationUpsert(long? BookingId, int CustomerId, int? ServiceId, int? PortId,
        int? DestinationId, int? TripTypeId, string? PlannedDate, decimal EstimatedCost,
        string? Notes, List<RevenueLineDto>? RevenueLines);

    public record ListItem(long OperationId, string OperationNumber, string? BookingNumber,
        string CustomerName, string? ServiceName, string? PortName, string? DestinationName,
        DateTime? PlannedDate, string Status, decimal RevenueNet, decimal RevenueTax,
        decimal EstimatedCost, decimal ActualCost, DateTime CreatedAt);

    public record RevenueLineItem(long OperationRevenueItemId, int ServiceId, string ServiceName,
        string? Description, decimal Quantity, decimal UnitPrice, decimal Discount,
        int? TaxRateId, decimal TaxRate, decimal LineNet, decimal LineTax, long? PriceRuleId);

    public record Detail(long OperationId, string OperationNumber, long? BookingId,
        string? BookingNumber, int CustomerId, string CustomerName, int? ServiceId, int? PortId,
        int? DestinationId, int? TripTypeId, string? PlannedDate, decimal EstimatedCost,
        string? Notes, string Status, decimal RevenueNet, decimal RevenueTax, decimal ActualCost,
        DateTime CreatedAt);

    public record DetailResponse(Detail Operation, List<RevenueLineItem> RevenueLines);

    public record PriceSuggestion(decimal UnitPrice, decimal? CostPrice, int? TaxRateId,
        decimal TaxRate, long? PriceRuleId, string Source);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q,
        [FromQuery] int take = 200, CancellationToken ct = default)
    {
        if (take is < 1 or > 500) take = 200;

        var query = _db.Operations.AsNoTracking().Where(o => !o.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(o => o.Status == status);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(o =>
                o.OperationNumber.Contains(s) ||
                o.Customer.NameAr.Contains(s) ||
                (o.Booking != null && o.Booking.BookingNumber.Contains(s)));
        }

        var rows = await query
            .OrderByDescending(o => o.OperationId)
            .Take(take)
            .Select(o => new ListItem(
                o.OperationId, o.OperationNumber,
                o.Booking != null ? o.Booking.BookingNumber : null,
                o.Customer.NameAr,
                o.Service != null ? o.Service.NameAr : null,
                o.Port != null ? o.Port.NameAr : null,
                o.Destination != null ? o.Destination.NameAr : null,
                o.PlannedDate, o.Status,
                o.RevenueNet, o.RevenueTax, o.EstimatedCost, o.ActualCost, o.CreatedAt))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ONE ═══════════════

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var o = await _db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية مش موجودة" });

        var customerName = await _db.Customers.AsNoTracking()
            .Where(c => c.CustomerId == o.CustomerId).Select(c => c.NameAr)
            .FirstOrDefaultAsync(ct) ?? "";

        string? bookingNumber = null;
        if (o.BookingId is not null)
            bookingNumber = await _db.Bookings.AsNoTracking()
                .Where(b => b.BookingId == o.BookingId).Select(b => b.BookingNumber)
                .FirstOrDefaultAsync(ct);

        var lines = await _db.OperationRevenueItems.AsNoTracking()
            .Where(r => r.OperationId == id)
            .OrderBy(r => r.OperationRevenueItemId)
            .Select(r => new RevenueLineItem(
                r.OperationRevenueItemId, r.ServiceId, r.Service.NameAr, r.Description,
                r.Quantity, r.UnitPrice, r.Discount, r.TaxRateId, r.TaxRate,
                r.LineNet ?? 0, r.LineTax ?? 0, r.PriceRuleId))
            .ToListAsync(ct);

        var detail = new Detail(o.OperationId, o.OperationNumber, o.BookingId, bookingNumber,
            o.CustomerId, customerName, o.ServiceId, o.PortId, o.DestinationId, o.TripTypeId,
            o.PlannedDate?.ToString("yyyy-MM-ddTHH:mm"), o.EstimatedCost, o.Notes, o.Status,
            o.RevenueNet, o.RevenueTax, o.ActualCost, o.CreatedAt);

        return Ok(new DetailResponse(detail, lines));
    }

    // ═══════════════ حجوزات جاهزة تتحول لعملية ═══════════════

    [HttpGet("available-bookings")]
    [Authorize(Policy = "PERM:OPERATION.CREATE")]
    public async Task<IActionResult> AvailableBookings(CancellationToken ct)
    {
        var rows = await _db.Bookings.AsNoTracking()
            .Where(b => !b.IsDeleted && b.Status == "Confirmed")
            .Where(b => !_db.Operations.Any(o => o.BookingId == b.BookingId && !o.IsDeleted))
            .OrderByDescending(b => b.BookingId)
            .Take(100)
            .Select(b => new { b.BookingId, b.BookingNumber, CustomerName = b.Customer.NameAr })
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ اقتراح السعر (قواعد العميل → سعر الخدمة) ═══════════════

    [HttpGet("price-suggest")]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> PriceSuggest(
        [FromQuery] int customerId, [FromQuery] int serviceId,
        [FromQuery] int? portId, [FromQuery] int? destinationId,
        [FromQuery] int? tripTypeId, [FromQuery] int? containerTypeId,
        [FromQuery] string? date, CancellationToken ct = default)
    {
        if (customerId <= 0 || serviceId <= 0)
            return BadRequest(new { message = "العميل والخدمة مطلوبين" });

        var on = DateOnly.TryParse(date, out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow);

        // الأبعاد الاختيارية: القاعدة الفاضية (NULL) = "أي قيمة" → بتطبق على كل الحالات
        var rules = await _db.CustomerPriceRules.AsNoTracking()
            .Where(r => !r.IsDeleted && r.IsActive &&
                        r.CustomerId == customerId && r.ServiceId == serviceId &&
                        r.ValidFrom <= on && (r.ValidTo == null || r.ValidTo >= on))
            .ToListAsync(ct);

        var match = rules
            .Where(r => portId == null || r.PortId == null || r.PortId == portId)
            .Where(r => destinationId == null || r.DestinationId == null || r.DestinationId == destinationId)
            .Where(r => tripTypeId == null || r.TripTypeId == null || r.TripTypeId == tripTypeId)
            .Where(r => containerTypeId == null || r.ContainerTypeId == null || r.ContainerTypeId == containerTypeId)
            // القاعدة الأخص (أبعاد معبّاة أكتر) تكسب، وبعدين الأولوية (الرقم الأصغر أعلى)
            .OrderByDescending(r => (r.PortId is not null ? 1 : 0) + (r.DestinationId is not null ? 1 : 0)
                                  + (r.TripTypeId is not null ? 1 : 0) + (r.ContainerTypeId is not null ? 1 : 0))
            .ThenBy(r => r.Priority)
            .ThenByDescending(r => r.ValidFrom)
            .FirstOrDefault();

        var service = await _db.Services.AsNoTracking()
            .Where(s => s.ServiceId == serviceId)
            .Select(s => new { s.DefaultSellingPrice, s.TaxRateId })
            .FirstOrDefaultAsync(ct);
        if (service is null) return NotFound(new { message = "الخدمة مش موجودة" });

        decimal unit; decimal? cost; int? taxId; long? ruleId; string source;

        if (match is not null)
        {
            unit = match.UnitPrice; cost = match.CostPrice;
            taxId = match.TaxRateId ?? service.TaxRateId;
            ruleId = match.CustomerPriceRuleId;
            source = "قاعدة تسعير العميل";
        }
        else
        {
            unit = service.DefaultSellingPrice; cost = null;
            taxId = service.TaxRateId; ruleId = null;
            source = "سعر الخدمة الافتراضي";
        }

        // آخر احتياط: ضريبة نوع الرحلة (الترانزيت مثلًا معفاة)
        if (taxId is null && tripTypeId is not null)
            taxId = await _db.TripTypes.AsNoTracking()
                .Where(t => t.TripTypeId == tripTypeId).Select(t => t.DefaultTaxRateId)
                .FirstOrDefaultAsync(ct);

        var rate = await RateOfAsync(taxId, on, ct);

        return Ok(new PriceSuggestion(unit, cost, taxId, rate, ruleId, source));
    }

    private async Task<decimal> RateOfAsync(int? taxRateId, DateOnly on, CancellationToken ct)
    {
        if (taxRateId is null) return 0m;
        return await _db.TaxRates.AsNoTracking()
            .Where(t => t.TaxRateId == taxRateId && t.IsActive &&
                        t.ValidFrom <= on && (t.ValidTo == null || t.ValidTo >= on))
            .Select(t => t.Rate)
            .FirstOrDefaultAsync(ct);
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:OPERATION.CREATE")]
    public async Task<IActionResult> Create([FromBody] OperationUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var o = new Operation
        {
            OperationNumber = await _numbers.NextAsync("OPERATION", ct),
            BranchId        = branchId.Value,
            BookingId       = req.BookingId,
            CustomerId      = req.CustomerId,
            ServiceId       = req.ServiceId,
            PortId          = req.PortId,
            DestinationId   = req.DestinationId,
            TripTypeId      = req.TripTypeId,
            PlannedDate     = Dt(req.PlannedDate),
            EstimatedCost   = req.EstimatedCost < 0 ? 0 : req.EstimatedCost,
            Notes           = B(req.Notes),
            Status          = "Pending",
            CreatedBy       = CurrentUserId()
        };
        // 🔴 Atomicity: العملية + سطور الإيراد في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.Operations.Add(o);
            await _db.SaveChangesAsync(ct);

            AddRevenueLines(o.OperationId, req.RevenueLines!, CurrentUserId());
            await FillTaxRateSnapshotsAsync(ct);
            await _db.SaveChangesAsync(ct);

            await TouchBookingAsync(req.BookingId, ct);
            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { id = o.OperationId, number = o.OperationNumber,
            message = $"✅ اتفتحت العملية برقم {o.OperationNumber}" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:long}")]
    [Authorize(Policy = "PERM:OPERATION.EDIT")]
    public async Task<IActionResult> Update(long id, [FromBody] OperationUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var o = await _db.Operations.FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية مش موجودة" });

        if (o.Status is "Closed" or "Cancelled")
            return BadRequest(new { message = "العملية مقفولة — افتحها الأول" });

        o.CustomerId    = req.CustomerId;
        o.ServiceId     = req.ServiceId;
        o.PortId        = req.PortId;
        o.DestinationId = req.DestinationId;
        o.TripTypeId    = req.TripTypeId;
        o.PlannedDate   = Dt(req.PlannedDate);
        o.EstimatedCost = req.EstimatedCost < 0 ? 0 : req.EstimatedCost;
        o.Notes         = B(req.Notes);
        o.UpdatedAt     = DateTime.UtcNow;
        o.UpdatedBy     = CurrentUserId();

        // 🔴 Atomicity: التعديل (حذف السطور القديمة + بناء الجديدة) في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (req.RevenueLines is not null)
            {
                var old = await _db.OperationRevenueItems
                    .Where(r => r.OperationId == id).ToListAsync(ct);
                _db.OperationRevenueItems.RemoveRange(old);
                await _db.SaveChangesAsync(ct);          // الـ trigger بيصفّر الإيراد

                AddRevenueLines(id, req.RevenueLines, CurrentUserId());
                await FillTaxRateSnapshotsAsync(ct);
            }

            await _db.SaveChangesAsync(ct);            // الـ trigger بيعيد حساب RevenueNet

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    private void AddRevenueLines(long operationId, List<RevenueLineDto> lines, int userId)
    {
        foreach (var l in lines)
        {
            if (l.ServiceId <= 0 || l.Quantity <= 0) continue;
            _db.OperationRevenueItems.Add(new OperationRevenueItem
            {
                OperationId = operationId,
                ServiceId   = l.ServiceId,
                PriceRuleId = l.PriceRuleId,
                Description = B(l.Description),
                Quantity    = l.Quantity,
                UnitPrice   = l.UnitPrice < 0 ? 0 : l.UnitPrice,
                Discount    = l.Discount < 0 ? 0 : l.Discount,
                TaxRateId   = l.TaxRateId,
                TaxRate     = 0,                   // بتتملأ من TaxRates تحت (Snapshot)
                CreatedBy   = userId
            });
        }
    }

    /// <summary>
    /// TaxRate في الجدول Denormalized Snapshot — لازم التطبيق ينسخ القيمة من TaxRates.
    /// بينادى عليها قبل كل SaveChanges فيه سطور إيراد.
    /// </summary>
    private async Task FillTaxRateSnapshotsAsync(CancellationToken ct)
    {
        var pending = _db.ChangeTracker.Entries<OperationRevenueItem>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity)
            .ToList();
        if (pending.Count == 0) return;

        var ids = pending.Where(r => r.TaxRateId is not null)
                         .Select(r => r.TaxRateId!.Value).Distinct().ToList();
        if (ids.Count == 0) return;

        var rates = await _db.TaxRates.AsNoTracking()
            .Where(t => ids.Contains(t.TaxRateId))
            .ToDictionaryAsync(t => t.TaxRateId, t => t.Rate, ct);

        foreach (var r in pending)
            r.TaxRate = r.TaxRateId is not null && rates.TryGetValue(r.TaxRateId.Value, out var v) ? v : 0m;
    }

    // ═══════════════ STATUS ═══════════════

    public record StatusRequest(string? To);

    [HttpPost("{id:long}/status")]
    public async Task<IActionResult> SetStatus(long id, [FromBody] StatusRequest req, CancellationToken ct)
    {
        var to = req?.To?.Trim();
        var policy = to switch
        {
            "Delivered" => "OPERATION.EDIT",
            "Cancelled" => "OPERATION.CANCEL",
            "Pending"   => "OPERATION.EDIT",
            _           => null
        };
        if (policy is null)
            return BadRequest(new { message = "الحالة المطلوبة مش صالحة من هنا" });

        if (!await _perms.HasAsync(CurrentUserId(), policy, ct))
            return StatusCode(403, new { message = "ليس لديكصلاحية للحركة دي" });

        var o = await _db.Operations.FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية مش موجودة" });

        if (!Allowed(o.Status, to!))
            return BadRequest(new { message = $"مش ممكن تنقل العملية من {Ar(o.Status)} إلى {Ar(to!)}" });

        o.Status    = to!;
        o.UpdatedAt = DateTime.UtcNow;
        o.UpdatedBy = CurrentUserId();
        if (to == "Delivered") o.ActualDeliveryAt ??= DateTime.UtcNow;
        if (to == "Cancelled") await TouchBookingAsync(o.BookingId, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"✅ الحالة بقت {Ar(to!)}" });
    }

    [HttpPost("{id:long}/close")]
    [Authorize(Policy = "PERM:OPERATION.CLOSE")]
    public async Task<IActionResult> Close(long id, CancellationToken ct)
    {
        var o = await _db.Operations.FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية مش موجودة" });

        if (o.Status is "Closed")  return BadRequest(new { message = "العملية مقفولة بالفعل" });
        if (o.Status is "Cancelled") return BadRequest(new { message = "العملية ملغاة — ماينفعش تتقفل" });
        if (o.Status is not ("Delivered" or "ExpensesPending" or "CustodyPending" or "ReadyToClose"))
            return BadRequest(new { message = "العملية لازم تكون اتسلّمت الأول" });

        // CK_Expenses_Status: Draft / Posted / Cancelled — الـ Draft لسه مااتأكدش
        var draftExpenses = await _db.Expenses.CountAsync(
            e => e.OperationId == id && e.Status == "Draft" && !e.IsDeleted, ct);
        if (draftExpenses > 0)
            return BadRequest(new { message = $"فيه {draftExpenses} مصروف لسه مسودة — أكّده الأول" });

        // العهدة على مستوى الرحلة (مش العملية) — فالعملية تتقفل لما رحلة تتقفل عهدتها
        var openCustody = await _db.DriverCustodies.CountAsync(c =>
            !c.IsDeleted &&
            (c.Status == "Open" || c.Status == "PartiallySettled") &&
            _db.TripOperations.Any(to => to.TripId == c.TripId && to.OperationId == id), ct);
        if (openCustody > 0)
            return BadRequest(new { message = "فيه عهدة مفتوحة على رحلة العملية — سوّيها الأول" });

        o.Status   = "Closed";
        o.ClosedAt = DateTime.UtcNow;
        o.ClosedBy = CurrentUserId();
        o.UpdatedAt = DateTime.UtcNow;
        o.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اتقفلت العملية" });
    }

    [HttpPost("{id:long}/reopen")]
    [Authorize(Policy = "PERM:OPERATION.REOPEN")]
    public async Task<IActionResult> Reopen(long id, CancellationToken ct)
    {
        var o = await _db.Operations.FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية مش موجودة" });
        if (o.Status is not "Closed") return BadRequest(new { message = "العملية مش مقفولة" });

        o.Status   = "Delivered";
        o.ClosedAt = null;
        o.ClosedBy = null;
        o.UpdatedAt = DateTime.UtcNow;
        o.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اتفتحت العملية تاني" });
    }

    private static bool Allowed(string from, string to) => (from, to) switch
    {
        ("Pending", "Cancelled")    => true,
        ("Assigned", "Cancelled")   => true,
        ("Assigned", "Pending")     => true,
        ("Assigned", "Delivered")   => true,
        ("InTransit", "Delivered")  => true,
        ("DriverReceived", "Delivered") => true,
        _                           => false
    };

    private static string Ar(string s) => s switch
    {
        "Pending" => "قيد الانتظار", "Assigned" => "تم التعيين",
        "DriverReceived" => "استلم السائق", "InTransit" => "في الطريق",
        "Delivered" => "تم التسليم", "ExpensesPending" => "بانتظار المصروفات",
        "CustodyPending" => "بانتظار العهدة", "ReadyToClose" => "جاهزة للإغلاق",
        "Closed" => "مغلقة", "Cancelled" => "ملغاة", _ => s
    };

    private async Task TouchBookingAsync(long? bookingId, CancellationToken ct)
    {
        if (bookingId is null) return;
        var b = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b is null || b.Status is not ("Confirmed" or "Draft")) return;

        var stillOpen = await _db.Operations.AnyAsync(
            o => o.BookingId == bookingId && o.Status != "Cancelled" && !o.IsDeleted, ct);
        b.Status = stillOpen ? "InProgress" : "Confirmed";
        b.UpdatedAt = DateTime.UtcNow;
        b.UpdatedBy = CurrentUserId();
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:OPERATION.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var o = await _db.Operations.FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية مش موجودة" });

        if (o.Status is not ("Pending" or "Cancelled"))
            return BadRequest(new { message = "العملية اللي اشتغلت بتتلغى مش بتتحذف" });

        var hasTrip = await _db.TripOperations.AnyAsync(t => t.OperationId == id, ct);
        if (hasTrip) return BadRequest(new { message = "العملية مربوطة برحلة — ماينفعش تتحذف" });

        // 🔴 Atomicity: حذف العملية + السطور + تحديث الحجز في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var lines = await _db.OperationRevenueItems
                .Where(r => r.OperationId == id).ToListAsync(ct);
            _db.OperationRevenueItems.RemoveRange(lines);

            o.IsDeleted = true;
            o.DeletedAt = DateTime.UtcNow;
            o.DeletedBy = CurrentUserId();
            await _db.SaveChangesAsync(ct);

            await TouchBookingAsync(o.BookingId, ct);
            await _db.SaveChangesAsync(ct);

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { message = "✅ اتحذفت العملية" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(OperationUpsert? r, CancellationToken ct)
    {
        if (r is null) return "البيانات مش كاملة";
        if (r.CustomerId <= 0) return "لازم تختار العميل";

        if (!await _db.Customers.AnyAsync(c => c.CustomerId == r.CustomerId && !c.IsDeleted, ct))
            return "العميل مش موجود";

        if (r.BookingId is not null)
        {
            var bk = await _db.Bookings.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookingId == r.BookingId && !b.IsDeleted, ct);
            if (bk is null) return "الحجز مش موجود";
            if (bk.CustomerId != r.CustomerId) return "الحجز مش تابع للعميل ده";
        }

        if (r.ServiceId is not null && !await _db.Services.AnyAsync(x => x.ServiceId == r.ServiceId, ct))
            return "الخدمة مش موجودة";
        if (r.PortId is not null && !await _db.Ports.AnyAsync(x => x.PortId == r.PortId, ct))
            return "الميناء مش موجود";
        if (r.DestinationId is not null && !await _db.Destinations.AnyAsync(x => x.DestinationId == r.DestinationId, ct))
            return "الجهة مش موجودة";
        if (r.TripTypeId is not null && !await _db.TripTypes.AnyAsync(x => x.TripTypeId == r.TripTypeId, ct))
            return "نوع الرحلة مش موجود";

        if (r.EstimatedCost < 0) return "التكلفة التقديرية ماينفعش تكون سالبة";

        if (r.RevenueLines is not null)
        {
            var svcIds = r.RevenueLines.Select(l => l.ServiceId).Distinct().ToList();
            var found = await _db.Services.Where(s => svcIds.Contains(s.ServiceId))
                .Select(s => s.ServiceId).ToListAsync(ct);
            if (found.Count != svcIds.Count) return "فيه خدمة في سطور الإيراد مش موجودة";

            foreach (var l in r.RevenueLines)
            {
                if (l.Quantity <= 0) return "الكمية في سطر الإيراد لازم تكون أكتر من صفر";
                if (l.UnitPrice < 0) return "سعر الوحدة ماينفعش يكون سالب";
                if (l.Discount < 0) return "الخصم ماينفعش يكون سالب";
                if (l.Discount > l.Quantity * l.UnitPrice)
                    return "الخصم أكبر من قيمة السطر";
            }
        }

        return null;
    }

}
