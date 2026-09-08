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
/// الحجوزات — طلب العميل قبل ما يتحول لعملية نقل.
/// الحالة: Draft → Confirmed → Assigned → InProgress → Completed (أو Cancelled).
/// </summary>
[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly INumberingService _numbers;
    private readonly IPermissionService _perms;

    public BookingsController(FastComDbContext db, INumberingService numbers, IPermissionService perms)
    { _db = db; _numbers = numbers; _perms = perms; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static DateOnly? D(string? s) => DateOnly.TryParse(s, out var d) ? d : null;

    /// <summary>BranchId من التوكن، ولو مافيش → أول فرع (الافتراضي MAIN).</summary>
    private async Task<int?> ResolveBranchIdAsync(CancellationToken ct)
    {
        if (int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0) return b;
        return await _db.Branches.AsNoTracking()
            .OrderBy(x => x.BranchId).Select(x => (int?)x.BranchId).FirstOrDefaultAsync(ct);
    }

    // ═══════════════ DTOs ═══════════════

    public record BookingLineDto(int ContainerTypeId, int RequestedQty, decimal? WeightKg, string? Notes);

    public record BookingUpsert(
        int CustomerId, string? RequestedDate, int? ServiceId, int? PortId, int? DestinationId,
        int? TripTypeId, string? CustomerReference, int? ContactId, string? Notes,
        List<BookingLineDto>? Lines);

    public record ListItem(
        long BookingId, string BookingNumber, string CustomerName, string? ServiceName,
        string? PortName, string? DestinationName, string? CustomerReference,
        DateOnly? RequestedDate, int ContainersQty, string Status, DateTime CreatedAt);

    public record LineItem(
        long BookingContainerLineId, int LineNo, int ContainerTypeId, string ContainerTypeName,
        int RequestedQty, int AssignedQty, decimal? WeightKg, string Status, string? Notes);

    public record Detail(
        long BookingId, string BookingNumber, int CustomerId, string CustomerName,
        string? RequestedDate, int? ServiceId, string? ServiceName, int? PortId, string? PortName,
        int? DestinationId, string? DestinationName, int? TripTypeId, string? TripTypeName,
        string? CustomerReference, int? ContactId, string? Notes, string Status, DateTime CreatedAt);

    public record DetailResponse(Detail Booking, List<LineItem> Lines);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:BOOKING.VIEW")]
    public async Task<IActionResult> List(
        [FromQuery] string? status, [FromQuery] string? q, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        if (take is < 1 or > 500) take = 200;

        var query = _db.Bookings.AsNoTracking().Where(b => !b.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(b => b.Status == status);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(b =>
                b.BookingNumber.Contains(s) ||
                b.Customer.NameAr.Contains(s) ||
                (b.CustomerReference != null && b.CustomerReference.Contains(s)));
        }

        var rows = await query
            .OrderByDescending(b => b.BookingId)
            .Take(take)
            .Select(b => new ListItem(
                b.BookingId, b.BookingNumber, b.Customer.NameAr,
                b.Service != null ? b.Service.NameAr : null,
                b.Port != null ? b.Port.NameAr : null,
                b.Destination != null ? b.Destination.NameAr : null,
                b.CustomerReference,
                b.RequestedDate,
                b.BookingContainerLines.Sum(l => l.RequestedQty),
                b.Status, b.CreatedAt))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ONE ═══════════════

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:BOOKING.VIEW")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var b = await _db.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BookingId == id && !x.IsDeleted, ct);
        if (b is null) return NotFound(new { message = "الحجز مش موجود" });

        var customerName = await _db.Customers.AsNoTracking()
            .Where(c => c.CustomerId == b.CustomerId).Select(c => c.NameAr).FirstOrDefaultAsync(ct) ?? "";

        var lines = await _db.BookingContainerLines.AsNoTracking()
            .Where(l => l.BookingId == id)
            .OrderBy(l => l.LineNo)
            .Select(l => new LineItem(
                l.BookingContainerLineId, l.LineNo, l.ContainerTypeId, l.ContainerType.NameAr,
                l.RequestedQty, l.AssignedQty, l.WeightKg, l.Status, l.Notes))
            .ToListAsync(ct);

        var names = await LookupNamesAsync(b, ct);

        var detail = new Detail(
            b.BookingId, b.BookingNumber, b.CustomerId, customerName,
            b.RequestedDate?.ToString("yyyy-MM-dd"), b.ServiceId, names.Service,
            b.PortId, names.Port, b.DestinationId, names.Destination,
            b.TripTypeId, names.TripType, b.CustomerReference, b.ContactId,
            b.Notes, b.Status, b.CreatedAt);

        return Ok(new DetailResponse(detail, lines));
    }

    private async Task<(string? Service, string? Port, string? Destination, string? TripType)>
        LookupNamesAsync(Booking b, CancellationToken ct)
    {
        string? svc = null, port = null, dest = null, tt = null;
        if (b.ServiceId is not null)
            svc = await _db.Services.AsNoTracking().Where(x => x.ServiceId == b.ServiceId)
                .Select(x => x.NameAr).FirstOrDefaultAsync(ct);
        if (b.PortId is not null)
            port = await _db.Ports.AsNoTracking().Where(x => x.PortId == b.PortId)
                .Select(x => x.NameAr).FirstOrDefaultAsync(ct);
        if (b.DestinationId is not null)
            dest = await _db.Destinations.AsNoTracking().Where(x => x.DestinationId == b.DestinationId)
                .Select(x => x.NameAr).FirstOrDefaultAsync(ct);
        if (b.TripTypeId is not null)
            tt = await _db.TripTypes.AsNoTracking().Where(x => x.TripTypeId == b.TripTypeId)
                .Select(x => x.NameAr).FirstOrDefaultAsync(ct);
        return (svc, port, dest, tt);
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:BOOKING.CREATE")]
    public async Task<IActionResult> Create([FromBody] BookingUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var b = new Booking
        {
            BookingNumber   = await _numbers.NextAsync("BOOKING", ct),
            BranchId        = branchId.Value,
            CustomerId      = req.CustomerId,
            RequestedDate   = D(req.RequestedDate),
            ServiceId       = req.ServiceId,
            PortId          = req.PortId,
            DestinationId   = req.DestinationId,
            TripTypeId      = req.TripTypeId,
            CustomerReference = B(req.CustomerReference),
            ContactId       = req.ContactId,
            Notes           = B(req.Notes),
            Status          = "Draft",
            CreatedBy       = CurrentUserId()
        };
        // 🔴 Atomicity: الحجز + بنوده في معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.Bookings.Add(b);
            await _db.SaveChangesAsync(ct);

            AddLines(b.BookingId, req.Lines!, CurrentUserId());
            await _db.SaveChangesAsync(ct);

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { id = b.BookingId, number = b.BookingNumber,
            message = $"✅ اتسجل الحجز برقم {b.BookingNumber}" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:long}")]
    [Authorize(Policy = "PERM:BOOKING.EDIT")]
    public async Task<IActionResult> Update(long id, [FromBody] BookingUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var b = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == id && !x.IsDeleted, ct);
        if (b is null) return NotFound(new { message = "الحجز مش موجود" });

        if (b.Status is not ("Draft" or "Confirmed"))
            return BadRequest(new { message = "الحجز اتحرّك للتشغيل — التعديل مقفول" });

        b.CustomerId      = req.CustomerId;
        b.RequestedDate   = D(req.RequestedDate);
        b.ServiceId       = req.ServiceId;
        b.PortId          = req.PortId;
        b.DestinationId   = req.DestinationId;
        b.TripTypeId      = req.TripTypeId;
        b.CustomerReference = B(req.CustomerReference);
        b.ContactId       = req.ContactId;
        b.Notes           = B(req.Notes);
        b.UpdatedAt       = DateTime.UtcNow;
        b.UpdatedBy       = CurrentUserId();

        // الفحص (قراءة فقط) قبل المعاملة — عشان مفيش معاملة تفتح لتحقق يرفضها
        var lockedLines = false;
        if (req.Lines is not null)
        {
            var checkLines = await _db.BookingContainerLines
                .Where(l => l.BookingId == id).ToListAsync(ct);
            if (checkLines.Count > 0)
            {
                var lineIds = checkLines.Select(l => l.BookingContainerLineId).ToList();
                lockedLines = await _db.BookingContainerDetails
                    .AnyAsync(d => lineIds.Contains(d.BookingContainerLineId), ct);
            }
        }

        if (lockedLines)
            return BadRequest(new { message = "فيه حاويات فعلية متسجلة على الحجز — السطور مقفولة" });

        // 🔴 Atomicity: استبدال السطور بالكامل — معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // السطور بتتبدّل بالكامل — بس لو مافيش حاويات فعلية متسجلة عليها
            if (req.Lines is not null)
            {
                var oldLines = await _db.BookingContainerLines
                    .Where(l => l.BookingId == id).ToListAsync(ct);
                _db.BookingContainerLines.RemoveRange(oldLines);

                AddLines(id, req.Lines, CurrentUserId());
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

    private void AddLines(long bookingId, List<BookingLineDto> lines, int userId)
    {
        var no = 0;
        foreach (var l in lines)
        {
            if (l.RequestedQty < 1) continue;
            no++;
            _db.BookingContainerLines.Add(new BookingContainerLine
            {
                BookingId       = bookingId,
                LineNo          = no,
                ContainerTypeId = l.ContainerTypeId,
                RequestedQty    = l.RequestedQty,
                AssignedQty     = 0,
                WeightKg        = l.WeightKg is > 0 ? l.WeightKg : null,
                Status          = "Pending",
                Notes           = B(l.Notes),
                CreatedBy       = userId
            });
        }
    }

    // ═══════════════ STATUS ═══════════════

    [HttpPost("{id:long}/status")]
    public async Task<IActionResult> SetStatus(long id, [FromBody] StatusRequest req, CancellationToken ct)
    {
        var to = req?.To?.Trim();
        var policy = to switch
        {
            "Confirmed" => "BOOKING.CONFIRM",
            "Cancelled" => "BOOKING.CANCEL",
            "Assigned"  => "BOOKING.EDIT",
            "Draft"     => "BOOKING.EDIT",
            _           => null
        };
        if (policy is null)
            return BadRequest(new { message = "الحالة المطلوبة مش صالحة" });

        if (!await _perms.HasAsync(CurrentUserId(), policy, ct))
            return StatusCode(403, new { message = "ليس لديك صلاحية للحركة دي" });

        var b = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == id && !x.IsDeleted, ct);
        if (b is null) return NotFound(new { message = "الحجز مش موجود" });

        if (!Allowed(b.Status, to!))
            return BadRequest(new { message = $"مش ممكن تنقل الحجز من {Ar(b.Status)} إلى {Ar(to!)}" });

        if (to == "Confirmed" && !await _db.BookingContainerLines.AnyAsync(l => l.BookingId == id, ct))
            return BadRequest(new { message = "ضيف حاوية واحدة على الأقل قبل التأكيد" });

        b.Status    = to!;
        b.UpdatedAt = DateTime.UtcNow;
        b.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = to == "Confirmed" ? "✅ اتأكد الحجز"
                                : to == "Cancelled" ? "✅ اتلغى الحجز"
                                : "✅ اتحدثت الحالة" });
    }

    public record StatusRequest(string? To);

    /// <summary>الانتقالات المسموحة — Assigned/InProgress بتيجي من العمليات مش من هنا.</summary>
    private static bool Allowed(string from, string to) => (from, to) switch
    {
        ("Draft", "Confirmed")     => true,
        ("Draft", "Cancelled")     => true,
        ("Confirmed", "Cancelled") => true,
        ("Confirmed", "Draft")     => true,
        ("Cancelled", "Draft")     => true,
        _                          => false
    };

    private static string Ar(string s) => s switch
    {
        "Draft" => "مسودة", "Confirmed" => "مؤكد", "Assigned" => "متخصص",
        "InProgress" => "قيد التنفيذ", "Completed" => "مكتمل", "Cancelled" => "ملغي", _ => s
    };

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:BOOKING.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var b = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == id && !x.IsDeleted, ct);
        if (b is null) return NotFound(new { message = "الحجز مش موجود" });

        if (b.Status is not ("Draft" or "Cancelled"))
            return BadRequest(new { message = "الحجز المؤكد بيتلغى الأول قبل الحذف" });

        b.IsDeleted = true;
        b.DeletedAt = DateTime.UtcNow;
        b.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحذف الحجز" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(BookingUpsert? r, CancellationToken ct)
    {
        if (r is null) return "البيانات مش كاملة";
        if (r.CustomerId <= 0) return "لازم تختار العميل";

        var customerOk = await _db.Customers.AnyAsync(c => c.CustomerId == r.CustomerId && !c.IsDeleted, ct);
        if (!customerOk) return "العميل مش موجود";

        if (r.ContactId is not null)
        {
            var contactOk = await _db.CustomerContacts.AnyAsync(
                c => c.CustomerContactId == r.ContactId && c.CustomerId == r.CustomerId, ct);
            if (!contactOk) return "جهة الاتصال مش تابعة للعميل ده";
        }

        if (r.ServiceId is not null && !await _db.Services.AnyAsync(x => x.ServiceId == r.ServiceId, ct))
            return "الخدمة مش موجودة";
        if (r.PortId is not null && !await _db.Ports.AnyAsync(x => x.PortId == r.PortId, ct))
            return "الميناء مش موجود";
        if (r.DestinationId is not null && !await _db.Destinations.AnyAsync(x => x.DestinationId == r.DestinationId, ct))
            return "الجهة مش موجودة";
        if (r.TripTypeId is not null && !await _db.TripTypes.AnyAsync(x => x.TripTypeId == r.TripTypeId, ct))
            return "نوع الرحلة مش موجود";

        if (r.Lines is null || r.Lines.Count == 0) return "ضيف حاوية واحدة على الأقل";

        var typeIds = r.Lines.Select(l => l.ContainerTypeId).Distinct().ToList();
        var valid = await _db.ContainerTypes.Where(t => typeIds.Contains(t.ContainerTypeId))
            .Select(t => t.ContainerTypeId).ToListAsync(ct);
        if (valid.Count != typeIds.Count) return "فيه نوع حاوية مش موجود";

        foreach (var l in r.Lines)
        {
            if (l.RequestedQty < 1) return "عدد الحاويات لازم يكون 1 أو أكتر";
            if (l.RequestedQty > 200) return "عدد الحاويات في السطر كبير بشكل غير منطقي";
            if (l.WeightKg is < 0) return "الوزن ماينفعش يكون سالب";
        }

        return null;
    }
}
