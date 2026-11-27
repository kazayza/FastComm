using System.Globalization;
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
    private readonly IAuditService _audit;
    private readonly INumberingService _numbers;
    private readonly IPermissionService _perms;

    public OperationsController(FastComDbContext db, INumberingService numbers, IPermissionService perms, IAuditService audit)
    { _db = db; _audit = audit; _numbers = numbers; _perms = perms; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /* 🔴 رؤية مبالغ العمليات — **بالفحص الحقيقي مش بالـ claim**.
       الكود القديم كان `User.HasClaim("perm", "OPERATION.VIEW_FINANCIALS")`،
       و`grep -rn 'Claim("perm"' src/FastCom.Server` بيطلّع **الصلاحيات بتتقرا بس —
       مفيش أي مكان بيحطها في الـ JWT** ⇒ الشرط كان `false` دايمًا،
       يعني الفحص كان شغّال لـ ADMIN بس. (`TokenService` بيحط `role` و Claims
       الـ claims بتاعة Identity، مش `perm`.)
       `IPermissionService` بيقرأ من `AppRolePermissions` (كاش 60 ثانية) — وده
       نفس الباس المستخدم في باقي الـ Controllers.
       async ⇒ بتتنادى مرة في أول كل action وتتحط في local، لأن EF
       ما ينفعش يترجم `await` جوه `Select`. */
    private Task<bool> HasFinAsync(CancellationToken ct)
    {
        if (User.IsInRole("ADMIN")) return Task.FromResult(true);   /* ADMIN فوق الفحص كله */
        return _perms.HasAsync(CurrentUserId(), "OPERATION.VIEW_FINANCIALS", ct);
    }

    private static DateTime? Dt(string? s) =>
        DateTime.TryParse(s, out var d) ? d : null;

    private async Task<int?> ResolveBranchIdAsync(CancellationToken ct)
    {
        if (int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0) return b;
        return await _db.Branches.AsNoTracking()
            .OrderBy(x => x.BranchId).Select(x => (int?)x.BranchId).FirstOrDefaultAsync(ct);
    }

    // ═══════════════ DTOs ═══════════════

    /* #83 — أبعاد السطر بقت اختيارية في الآخر: النداء الموضعي القديم
       (new(svcId, desc, 1, price, 0, tax, null)) يفضل يشتغل من غير تعديل */
    public record RevenueLineDto(int ServiceId, string? Description, decimal Quantity,
        decimal UnitPrice, decimal Discount, int? TaxRateId, long? PriceRuleId,
        int? PortId = null, int? DestinationId = null, int? TahteeqPortId = null, int? TripTypeId = null);

    public record OperationUpsert(long? BookingId, int CustomerId, int? ServiceId, int? PortId,
        int? DestinationId, int? TahteeqPortId, int? TripTypeId, string? PlannedDate, decimal EstimatedCost,
        string? Notes, List<RevenueLineDto>? RevenueLines);

    public record ListItem(long OperationId, string OperationNumber, string? BookingNumber,
        string? BookingReference,
        string CustomerName, string? ServiceName, string? PortName, string? DestinationName,
        string? TahteeqPortName,
        DateTime? PlannedDate, string Status, decimal RevenueNet, decimal RevenueTax,
        decimal EstimatedCost, decimal ActualCost, DateTime CreatedAt,
        long? TripId, string? TripNumber,
        /* #90 — مجموع «يتحمّله العميل». 🔴 **مش إيراد** — للعِلم بس */
        decimal BillableRevenue = 0m);

    /* #83 — بترجع أبعاد السطر عشان الشاشة تعبّيها وقت التعديل */
    public record RevenueLineItem(long OperationRevenueItemId, int ServiceId, string ServiceName,
        string? Description, decimal Quantity, decimal UnitPrice, decimal Discount,
        int? TaxRateId, decimal TaxRate, decimal LineNet, decimal LineTax, long? PriceRuleId,
        int? PortId = null, int? DestinationId = null, int? TahteeqPortId = null, int? TripTypeId = null);

    public record Detail(long OperationId, string OperationNumber, long? BookingId,
        string? BookingNumber, int CustomerId, string CustomerName, int? ServiceId, int? PortId,
        int? DestinationId, int? TahteeqPortId, string? TahteeqPortName, int? TripTypeId, string? PlannedDate, decimal EstimatedCost,
        string? Notes, string Status, decimal RevenueNet, decimal RevenueTax, decimal ActualCost,
        DateTime CreatedAt, string? AgentReference = null,
        /* #90 */ decimal BillableRevenue = 0m);

    public record DetailResponse(Detail Operation, List<RevenueLineItem> RevenueLines);

    /* #95 — 🔴 `UnitPrice = null` = مفيش قاعدة مطابقة في قوائم الأسعار.
       السعر **من جدول الأسعار بس** (قاعدة العميل أو التعريفة العامة) —
       **مش من `Services.DefaultSellingPrice`**. */
    public record PriceSuggestion(decimal? UnitPrice, decimal? CostPrice, int? TaxRateId,
        decimal TaxRate, long? PriceRuleId, string Source);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q,
        [FromQuery] int take = 200, CancellationToken ct = default)
    {
        if (take is < 1 or > 500) take = 200;
        var hasFin = await HasFinAsync(ct);

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
                o.Booking != null ? o.Booking.BookingReference : null,
                o.Customer.NameAr,
                o.Service != null ? o.Service.NameAr : null,
                o.Port != null ? o.Port.NameAr : null,
                o.Destination != null ? o.Destination.NameAr : null,
                o.TahteeqPort != null ? o.TahteeqPort.NameAr : null,
                o.PlannedDate, o.Status,
                hasFin ? o.RevenueNet : 0m, hasFin ? o.RevenueTax : 0m,
                hasFin ? o.EstimatedCost : 0m, hasFin ? o.ActualCost : 0m, o.CreatedAt,
                _db.TripOperations
                    .Where(to => to.OperationId == o.OperationId && to.Status != "Cancelled")
                    .Select(to => (long?)to.TripId).FirstOrDefault(),
                _db.TripOperations
                    .Where(to => to.OperationId == o.OperationId && to.Status != "Cancelled")
                    .Select(to => to.Trip.TripNumber).FirstOrDefault(),
                hasFin ? o.BillableRevenue : 0m))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ شاشة طلبات النقل الموحدة: كل الطلبات ═══════════════

    public record RequestItem(long OperationId, string OperationNumber,
        long? BookingId, string? BookingNumber, string? AgentName, string? AgentReference,
        string CustomerName, string? ServiceName, string? PortName, string? DestinationName,
        string? TahteeqPortName, DateTime? PlannedDate, string Status,
        int ContainersQty, int TripsCount,
        decimal RevenueNet, decimal RevenueTax, decimal ActualCost, DateTime CreatedAt,
        decimal BillableRevenue,
        long? StartTripId, string? StartTripNumber, string? StartTripDriverName, string? StartTripVehiclePlate,
        long? CompleteTripId, string? CompleteTripNumber, string? CompleteTripDriverName, string? CompleteTripVehiclePlate,
        bool CanCloseOp);

    [HttpGet("requests")]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> Requests(int take = 300, CancellationToken ct = default)
    {
        if (take <= 0 || take > 1000) take = 300;
        var hasFin = await HasFinAsync(ct);

        var rows = await _db.Operations.AsNoTracking()
            .Where(o => !o.IsDeleted)
            .OrderByDescending(o => o.OperationId)
            .Take(take)
            .Select(o => new RequestItem(
                o.OperationId, o.OperationNumber,
                o.BookingId,
                o.Booking != null ? o.Booking.BookingNumber : null,
                o.Booking != null && o.Booking.ShippingAgent != null ? o.Booking.ShippingAgent.NameAr : null,
                o.Booking != null ? o.Booking.BookingReference : null,
                o.Customer.NameAr,
                o.Service != null ? o.Service.NameAr : null,
                o.Port != null ? o.Port.NameAr : null,
                o.Destination != null ? o.Destination.NameAr : null,
                o.TahteeqPort != null ? o.TahteeqPort.NameAr : null,
                o.PlannedDate, o.Status,
                o.Booking != null ? o.Booking.BookingContainerLines.Sum(l => l.RequestedQty) : 0,
_db.TripOperations.Count(to => to.OperationId == o.OperationId && to.Status != "Cancelled"),
                hasFin ? o.RevenueNet : 0m, hasFin ? o.RevenueTax : 0m, hasFin ? o.ActualCost : 0m, o.CreatedAt,
                hasFin ? o.BillableRevenue : 0m,
                _db.TripOperations
                    .Where(to => to.OperationId == o.OperationId && to.Status != "Cancelled"
                                 && (to.Trip.Status == "Planned" || to.Trip.Status == "Assigned"))
                    .OrderBy(to => to.Trip.PlannedStartAt ?? DateTime.MaxValue).ThenBy(to => to.TripId)
                    .Select(to => (long?)to.TripId).FirstOrDefault(),
                _db.TripOperations
                    .Where(to => to.OperationId == o.OperationId && to.Status != "Cancelled"
                                 && (to.Trip.Status == "Planned" || to.Trip.Status == "Assigned"))
                    .OrderBy(to => to.Trip.PlannedStartAt ?? DateTime.MaxValue).ThenBy(to => to.TripId)
                    .Select(to => to.Trip.TripNumber).FirstOrDefault(),
                _db.TripOperations
                    .Where(to => to.OperationId == o.OperationId && to.Status != "Cancelled"
                                 && (to.Trip.Status == "Planned" || to.Trip.Status == "Assigned"))
                    .OrderBy(to => to.Trip.PlannedStartAt ?? DateTime.MaxValue).ThenBy(to => to.TripId)
                    .Select(to => to.Trip.Driver != null ? to.Trip.Driver.FullName : null).FirstOrDefault(),
                _db.TripOperations
                    .Where(to => to.OperationId == o.OperationId && to.Status != "Cancelled"
                                 && (to.Trip.Status == "Planned" || to.Trip.Status == "Assigned"))
                    .OrderBy(to => to.Trip.PlannedStartAt ?? DateTime.MaxValue).ThenBy(to => to.TripId)
                    .Select(to => to.Trip.Vehicle != null ? to.Trip.Vehicle.PlateNumber : null).FirstOrDefault(),
                _db.TripOperations
                    .Where(to => to.OperationId == o.OperationId && to.Status != "Cancelled"
                                 && to.Trip.Status == "Started")
                    .OrderBy(to => to.Trip.ActualStartAt ?? DateTime.MaxValue).ThenBy(to => to.TripId)
                    .Select(to => (long?)to.TripId).FirstOrDefault(),
                _db.TripOperations
                    .Where(to => to.OperationId == o.OperationId && to.Status != "Cancelled"
                                 && to.Trip.Status == "Started")
                    .OrderBy(to => to.Trip.ActualStartAt ?? DateTime.MaxValue).ThenBy(to => to.TripId)
                    .Select(to => to.Trip.TripNumber).FirstOrDefault(),
                _db.TripOperations
                    .Where(to => to.OperationId == o.OperationId && to.Status != "Cancelled"
                                 && to.Trip.Status == "Started")
                    .OrderBy(to => to.Trip.ActualStartAt ?? DateTime.MaxValue).ThenBy(to => to.TripId)
                    .Select(to => to.Trip.Driver != null ? to.Trip.Driver.FullName : null).FirstOrDefault(),
                _db.TripOperations
                    .Where(to => to.OperationId == o.OperationId && to.Status != "Cancelled"
                                 && to.Trip.Status == "Started")
                    .OrderBy(to => to.Trip.ActualStartAt ?? DateTime.MaxValue).ThenBy(to => to.TripId)
                    .Select(to => to.Trip.Vehicle != null ? to.Trip.Vehicle.PlateNumber : null).FirstOrDefault(),
                (o.Status == "Delivered" || o.Status == "ExpensesPending" ||
                 o.Status == "CustodyPending" || o.Status == "ReadyToClose")
                && _db.TripOperations.Any(to => to.OperationId == o.OperationId && to.Status != "Cancelled")
                && !_db.TripOperations.Any(to => to.OperationId == o.OperationId && to.Status != "Cancelled"
                                 && to.Trip.Status != "Completed" && to.Trip.Status != "Cancelled")))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ONE ═══════════════

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var hasFin = await HasFinAsync(ct);

        var o = await _db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية مش موجودة" });

        var customerName = await _db.Customers.AsNoTracking()
            .Where(c => c.CustomerId == o.CustomerId).Select(c => c.NameAr)
            .FirstOrDefaultAsync(ct) ?? "";

        string? bookingNumber = null;
        string? agentReference = null;
        if (o.BookingId is not null)
        {
            var bk = await _db.Bookings.AsNoTracking()
                .Where(b => b.BookingId == o.BookingId)
                .Select(b => new { b.BookingNumber, b.BookingReference })
                .FirstOrDefaultAsync(ct);
            bookingNumber  = bk?.BookingNumber;
            agentReference = bk?.BookingReference;
        }

        string? tahteeqPortName = null;
        if (o.TahteeqPortId is not null)
            tahteeqPortName = await _db.Ports.AsNoTracking()
                .Where(x => x.PortId == o.TahteeqPortId).Select(x => x.NameAr)
                .FirstOrDefaultAsync(ct);

        var lines = await _db.OperationRevenueItems.AsNoTracking()
            .Where(r => r.OperationId == id)
            .OrderBy(r => r.OperationRevenueItemId)
            .Select(r => new RevenueLineItem(
                r.OperationRevenueItemId, r.ServiceId, r.Service.NameAr, r.Description,
                r.Quantity,
                hasFin ? r.UnitPrice : 0m, hasFin ? r.Discount : 0m,
                r.TaxRateId, r.TaxRate,
                hasFin ? (r.LineNet ?? 0) : 0m, hasFin ? (r.LineTax ?? 0) : 0m,
                r.PriceRuleId,
                r.PortId, r.DestinationId, r.TahteeqPortId, r.TripTypeId))
            .ToListAsync(ct);

        var detail = new Detail(o.OperationId, o.OperationNumber, o.BookingId, bookingNumber,
            o.CustomerId, customerName, o.ServiceId, o.PortId, o.DestinationId,
            o.TahteeqPortId, tahteeqPortName, o.TripTypeId,
            o.PlannedDate?.ToString("yyyy-MM-ddTHH:mm"), hasFin ? o.EstimatedCost : 0m, o.Notes, o.Status,
            hasFin ? o.RevenueNet : 0m, hasFin ? o.RevenueTax : 0m, hasFin ? o.ActualCost : 0m,
            o.CreatedAt, agentReference, hasFin ? o.BillableRevenue : 0m);

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
        [FromQuery] int? tahteeqPortId, [FromQuery] int? tripTypeId, [FromQuery] int? containerTypeId,
        [FromQuery] string? date, CancellationToken token = default)
    {
        if (customerId <= 0 || serviceId <= 0)
            return BadRequest(new { message = "العميل والخدمة مطلوبين" });

        var on = DateOnly.TryParse(date, out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow);

        // الأبعاد الاختيارية: القاعدة الفاضية (NULL) = "أي قيمة" → بتطبق على كل الحالات.
        // 🔴 قاعدة تابعة لقائمة أسعار: القائمة لازم تكون سارية هي كمان — قفل القائمة
        //    (ValidTo) أو تعطيلها أو حذفها بيوقف قواعدها فورًا.
        /* #83 — بنجيب قواعد العميل **والتعريفة العامة** (CustomerId = NULL) مع بعض،
           والترتيب تحت بيخلّي قاعدة العميل تكسب دايمًا */
        var rules = await _db.CustomerPriceRules.AsNoTracking()
            .Where(r => !r.IsDeleted && r.IsActive &&
                        (r.CustomerId == customerId || r.CustomerId == null) && r.ServiceId == serviceId &&
                        r.ValidFrom <= on && (r.ValidTo == null || r.ValidTo >= on) &&
                        (r.PriceListId == null || _db.PriceLists.Any(p =>
                            p.PriceListId == r.PriceListId && !p.IsDeleted && p.IsActive &&
                            p.ValidFrom <= on && (p.ValidTo == null || p.ValidTo >= on))))
            .ToListAsync(token);

        /* 🔴 البُعد اللي الطلب ما حدّدوش: القاعدة اللي محدّداه **ما تطابقش** —
           قبل كده كانت بتطابق وبتكسب بترتيب الأخصّية، فطلب ناقص كان بياخد
           سعر قاعدة مش بتاعته. القاعدة العامة (البُعد فاضي) بس هي اللي تطابق. */
        var match = rules
            .Where(r => r.PortId == null || (portId != null && r.PortId == portId))
            .Where(r => r.DestinationId == null || (destinationId != null && r.DestinationId == destinationId))
            .Where(r => r.TahteeqPortId == null || (tahteeqPortId != null && r.TahteeqPortId == tahteeqPortId))
            .Where(r => r.TripTypeId == null || (tripTypeId != null && r.TripTypeId == tripTypeId))
            .Where(r => r.ContainerTypeId == null || (containerTypeId != null && r.ContainerTypeId == containerTypeId))
            // القاعدة الأخص (أبعاد معبّاة أكتر) تكسب، وبعدين الأولوية (الرقم الأصغر أعلى)
            /* #83 — قاعدة العميل أولًا (حتى لو العامة أخص بالأبعاد)، وبعدين الأخص */
            .OrderByDescending(r => r.CustomerId is not null)
            .ThenByDescending(r => (r.PortId is not null ? 1 : 0) + (r.DestinationId is not null ? 1 : 0)
                                  + (r.TahteeqPortId is not null ? 1 : 0)
                                  + (r.TripTypeId is not null ? 1 : 0) + (r.ContainerTypeId is not null ? 1 : 0))
            .ThenBy(r => r.Priority)
            .ThenByDescending(r => r.ValidFrom)
            .FirstOrDefault();

        var service = await _db.Services.AsNoTracking()
            .Where(s => s.ServiceId == serviceId)
            /* #95 — DefaultSellingPrice ما بقتش مستخدمة: السعر من قوائم الأسعار بس */
            .Select(s => new { s.TaxRateId })
            .FirstOrDefaultAsync(token);
        if (service is null) return NotFound(new { message = "الخدمة مش موجودة" });

        /* #95 — 🔴 السعر من **قوائم الأسعار بس**. لو مفيش قاعدة مطابقة ⇒
           `unit = null` والشاشة بتقول «مفيش قاعدة سعر مطابقة».
           كان بيرجع `Services.DefaultSellingPrice` — وده غلط: سعر الخدمة
           سعر افتراضي للتعريف، مش سعر بيع من قائمة أسعار. */
        decimal? unit; decimal? cost; int? taxId; long? ruleId; string source;

        if (match is not null)
        {
            unit = match.UnitPrice; cost = match.CostPrice;
            taxId = match.TaxRateId ?? service.TaxRateId;
            ruleId = match.CustomerPriceRuleId;
            source = match.CustomerId is null ? "التعريفة العامة" : "قاعدة تسعير العميل";
        }
        else
        {
            unit = null; cost = null;
            taxId = service.TaxRateId; ruleId = null;
            /* #96 — 🔴 «مفيش قاعدة مطابقة» لوحدها ما بتفيدش: المستخدم ما يعرفش
               القاعدة محتاجة إيه، وبيفضل يدوّر في قاعدة البيانات بالساعات.
               بنقول السبب بالحرف — أبعاد محدّدة، قائمة مش سارية، أو مفيش قواعد. */
            source = await ExplainNoMatchAsync(serviceId, rules, token);
        }

        // آخر احتياط: ضريبة نوع الرحلة (الترانزيت مثلًا معفاة)
        if (taxId is null && tripTypeId is not null)
            taxId = await _db.TripTypes.AsNoTracking()
                .Where(t => t.TripTypeId == tripTypeId).Select(t => t.DefaultTaxRateId)
                .FirstOrDefaultAsync(token);

        var rate = await RateOfAsync(taxId, on, token);

        return Ok(new PriceSuggestion(unit, cost, taxId, rate, ruleId, source));
    }

    /* #96 — 🔴 ليه مفيش سعر؟
       `valid` = القواعد اللي عدّت من فلتر السريان والقائمة:
         · فيها حاجة  ⇒ السبب أكيد الأبعاد (القاعدة محدّدة على خط سير تاني).
         · فاضية      ⇒ بندوّر في الجدول كله: فيه قواعد والقائمة مقفولة، ولا مفيش أصلًا. */
    private async Task<string> ExplainNoMatchAsync(int serviceId,
        List<CustomerPriceRule> valid, CancellationToken token)
    {
        var near = valid.OrderByDescending(r => r.CustomerId is not null).FirstOrDefault();
        if (near is not null)
            return "فيه قاعدة سعر " + near.UnitPrice.ToString("N2", CultureInfo.InvariantCulture)
                 + " بس محدّدة على: " + await DimNamesAsync(near, token)
                 + " — ظبطها في الطلب، أو سيب الأبعاد فاضية في القاعدة";

        var anyRules = await _db.CustomerPriceRules.AsNoTracking()
            .CountAsync(r => r.ServiceId == serviceId && !r.IsDeleted && r.IsActive, token);
        if (anyRules > 0)
            return "فيه " + anyRules
                 + " قاعدة سعر للخدمة دي بس قائمة الأسعار اللي فيها مش سارية (مقفولة أو خارج التاريخ)";

        return "مفيش أي قاعدة سعر للخدمة دي في قوائم الأسعار";
    }

    /* أسماء الأبعاد اللي القاعدة محدّداها — عشان الرسالة تبقى مفهومة من غير ما يدخل SQL */
    private async Task<string> DimNamesAsync(CustomerPriceRule r, CancellationToken token)
    {
        var parts = new List<string>();
        if (r.PortId is int p)
            parts.Add("التحميل = " + (await _db.Ports.AsNoTracking()
                .Where(x => x.PortId == p).Select(x => x.NameAr).FirstOrDefaultAsync(token) ?? "؟"));
        if (r.DestinationId is int d)
            parts.Add("الوجهة = " + (await _db.Destinations.AsNoTracking()
                .Where(x => x.DestinationId == d).Select(x => x.NameAr).FirstOrDefaultAsync(token) ?? "؟"));
        if (r.TahteeqPortId is int t)
            parts.Add("التعتيق = " + (await _db.Ports.AsNoTracking()
                .Where(x => x.PortId == t).Select(x => x.NameAr).FirstOrDefaultAsync(token) ?? "؟"));
        if (r.TripTypeId is int tt)
            parts.Add("نوع الرحلة = " + (await _db.TripTypes.AsNoTracking()
                .Where(x => x.TripTypeId == tt).Select(x => x.NameAr).FirstOrDefaultAsync(token) ?? "؟"));
        if (r.ContainerTypeId is int c)
            parts.Add("الحاوية = " + (await _db.ContainerTypes.AsNoTracking()
                .Where(x => x.ContainerTypeId == c).Select(x => x.NameAr).FirstOrDefaultAsync(token) ?? "؟"));
        return parts.Count == 0 ? "(بلا أبعاد)" : string.Join(" · ", parts);
    }

    // ═══════════════ ضمان الحاويات (تكلفة على الشركة — غير مسترد) ═══════════════

    public record GuaranteeLine(int ContainerTypeId, string ContainerTypeName, int Qty,
        decimal? GuaranteePerContainer, decimal LineTotal);
    public record GuaranteeEstimate(decimal Total, List<GuaranteeLine> Lines,
        int? ExpenseTypePortId, int? ExpenseTypeReeferId, int? ExpenseTypeGenId,
        int? SupplierId, string? SupplierName);

    [HttpGet("guarantee-estimate")]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> GuaranteeEstimateCalc(
        [FromQuery] long operationId, CancellationToken ct = default)
    {
        // 🔴 الضمان = لكل حاوية خارجة على البوكينج — المبلغ من سطر الحجز (وافتراضيه من نوع الحاوية)
        //    خدمة من مورد (Bookings.GuaranteeSupplierId) — تكلفة على الشركة، غير مستردة
        var bookingId = await _db.Operations.AsNoTracking()
            .Where(o => o.OperationId == operationId && !o.IsDeleted)
            .Select(o => o.BookingId)
            .FirstOrDefaultAsync(ct);
        if (bookingId is null) return NotFound(new { message = "العملية مش موجودة أو بدون حجز" });

        /* مورد الضمان من البوكينج */
        var booking = await _db.Bookings.AsNoTracking()
            .Where(x => x.BookingId == bookingId.Value)
            .Select(x => new { x.GuaranteeSupplierId })
            .FirstOrDefaultAsync(ct);
        string? supplierName = null;
        if (booking?.GuaranteeSupplierId is not null)
            supplierName = await _db.Suppliers.AsNoTracking()
                .Where(s => s.SupplierId == booking.GuaranteeSupplierId)
                .Select(s => s.NameAr).FirstOrDefaultAsync(ct);

        var lines = await _db.BookingContainerLines.AsNoTracking()
            .Where(l => l.BookingId == bookingId.Value)
            .Select(l => new
            {
                l.ContainerTypeId,
                Qty = l.AssignedQty > 0 ? l.AssignedQty : l.RequestedQty,
                l.GuaranteePerContainer
            })
            .ToListAsync(ct);

        var types = await _db.ContainerTypes.AsNoTracking()
            .Select(c => new { c.ContainerTypeId, c.NameAr, c.GuaranteeAmount })
            .ToListAsync(ct);

        /* ضمان السطر = المحفوظ على السطر، ولو فاضي يرجع لافتراضي نوع الحاوية */
        var result = lines
            .Select(l =>
            {
                var t = types.FirstOrDefault(x => x.ContainerTypeId == l.ContainerTypeId);
                var per = l.GuaranteePerContainer ?? t?.GuaranteeAmount;
                return new { l.ContainerTypeId, Name = t?.NameAr ?? "—", l.Qty, Per = per };
            })
            .GroupBy(x => new { x.ContainerTypeId, x.Name, x.Per })
            .Select(g => new GuaranteeLine(g.Key.ContainerTypeId, g.Key.Name,
                g.Sum(x => x.Qty), g.Key.Per,
                g.Key.Per.HasValue ? g.Key.Per.Value * g.Sum(x => x.Qty) : 0m))
            .OrderByDescending(x => x.LineTotal)
            .ToList();

        // أنواع مصروف الضمان الجاهزة — الشاشة تختار منهم عند إنشاء المصروف
        var guarTypes = await _db.ExpenseTypes.AsNoTracking()
            .Where(e => e.IsActive && !e.IsDeleted &&
                        (e.Code == "GUAR-PORT" || e.Code == "GUAR-REEFER" || e.Code == "GUAR-GEN"))
            .Select(e => new { e.ExpenseTypeId, e.Code })
            .ToListAsync(ct);

        return Ok(new GuaranteeEstimate(
            result.Sum(x => x.LineTotal), result,
            guarTypes.FirstOrDefault(x => x.Code == "GUAR-PORT")?.ExpenseTypeId,
            guarTypes.FirstOrDefault(x => x.Code == "GUAR-REEFER")?.ExpenseTypeId,
            guarTypes.FirstOrDefault(x => x.Code == "GUAR-GEN")?.ExpenseTypeId,
            booking?.GuaranteeSupplierId, supplierName));
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

        /* 🔴 حجز ↔ عملية = 1:1 (قرار المالك): الحجز الواحد له عملية واحدة
           هي ملف الشغلانة، وجواها كل رحلاته */
        if (req.BookingId is not null &&
            await _db.Operations.AnyAsync(x => x.BookingId == req.BookingId && !x.IsDeleted, ct))
            return BadRequest(new { message = "الحجز ده له عملية بالفعل — العملية الواحدة هي ملف الحجز، افتحها واشتغل جواها" });

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
            TahteeqPortId   = req.TahteeqPortId,
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

            /* حاويات الحجز الفعلية تتنسخ لعملية — عملية من غير حاوياتها
               أساس التوزيع بعدد الحاويات وملف العملية بيبقوا فاضيين */
            if (req.BookingId is not null)
                await CopyBookingContainersAsync(o.OperationId, req.BookingId.Value, ct);

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

        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Create, "Operation", null, description: "إنشاء عملية", ct: ct);

        return Ok(new { id = o.OperationId, number = o.OperationNumber,
            message = $"✅ اتفتحت العملية برقم {o.OperationNumber}" });
    }

    // ═══════════════ إنشاء سريع: حجز الوكيل + عمليته في خطوة واحدة ═══════════════

    public record QuickLineDto(int ContainerTypeId, int RequestedQty, decimal? GuaranteePerContainer,
        decimal? WeightKg = null, string? Notes = null);

    public record QuickCreateRequest(
        int? ShippingAgentId, string? BookingReference, string? BLNumber,
        int CustomerId, int? ServiceId, int? PortId, int? DestinationId, int? TahteeqPortId,
        int? TripTypeId, string? Date, int? GuaranteeSupplierId, string? Notes,
        decimal? UnitPrice, List<QuickLineDto>? Lines,
        int? EndCustomerId = null, string? EndCustomerName = null, int? ContactId = null, string? CustomerReference = null,
        List<QuickTripDto>? Trips = null,
        List<QuickDetailDto>? ContainerDetails = null,
        /* #83 — سطور الإيراد كاملة. لو مبعوتة، UnitPrice بيتجاهل */
        List<RevenueLineDto>? RevenueLines = null);

    /* تفصيلة حاوية فردية (برقمها) — نفس حقول تفاصيل الحجز الأساسية،
       وLineIndex هو موضع السطر الأم داخل مصفوفة Lines المرسلة */
    public record QuickDetailDto(long? ContainerId, string? ContainerNumber, string? SealNumber,
        string? ShippingLine, string? BLNumber, string? BookingReference, decimal? WeightKg, string? Notes,
        int? LineIndex = null);

    /* #78 — الرحلة بقت تشيل خط سيرها هي (سحب/وجهة/تعتيق).
       لو المستخدم سيبهم فاضيين → الرحلة ترث خط سير العملية زي قبل كده بالظبط. */
    public record QuickTripDto(int DriverId, int VehicleId, int? TrailerId,
        decimal? FreightAmount, string? PlannedStartAt,
        int? PortId = null, int? DestinationId = null, int? TahteeqPortId = null);

    [HttpPost("quick-create")]
    [Authorize(Policy = "PERM:OPERATION.CREATE")]
    [Authorize(Policy = "PERM:BOOKING.CREATE")]
    public async Task<IActionResult> QuickCreate([FromBody] QuickCreateRequest req, CancellationToken ct)
    {
        /* رقم حجز الوكيل هو أساس الشغلانة (قرار المالك) — من غيره مفيش إنشاء */
        if (string.IsNullOrWhiteSpace(req.BookingReference))
            return BadRequest(new { message = "رقم حجز الوكيل مطلوب — هو أساس الشغلانة" });
        if (req.CustomerId <= 0)
            return BadRequest(new { message = "العميل مطلوب" });
        var lines = (req.Lines ?? new()).Where(l => l.ContainerTypeId > 0 && l.RequestedQty >= 1).ToList();
        if (lines.Count == 0)
            return BadRequest(new { message = "ضيف سطر حاويات واحد على الأقل (نوع + عدد)" });

        /* نفس حجز الوكيل ما يتدخلش مرتين */
        var dup = await _db.Bookings.AnyAsync(b => !b.IsDeleted &&
            b.ShippingAgentId == req.ShippingAgentId &&
            b.BookingReference == req.BookingReference.Trim(), ct);
        if (dup)
            return BadRequest(new { message = "رقم حجز الوكيل ده متسجل قبل كده لنفس الوكيل — راجع الحجوزات" });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        /* رحلات التعيين وقت التسجيل (اختيارية): نفس قاعدة تعارض الموارد بتاعة
           شاشة الرحلات — عربية/سائق محجوزين في نفس اليوم أو طالعين على الطريق */
        var qTrips = (req.Trips ?? new()).Where(t => t.DriverId > 0 && t.VehicleId > 0).ToList();
        var seenVeh = new HashSet<(int, DateTime)>();
        var seenDrv = new HashSet<(int, DateTime)>();
        foreach (var t in qTrips)
        {
            var when = Dt(t.PlannedStartAt) ?? Dt(req.Date);
            if (when is not null)
            {
                if (!seenVeh.Add((t.VehicleId, when.Value.Date)) || !seenDrv.Add((t.DriverId, when.Value.Date)))
                    return BadRequest(new { message = "نفس العربية أو السائق متكررين في يوم واحد جوه نفس الطلب" });
                var conflict = await QuickTripConflictAsync(t.DriverId, t.VehicleId, when, ct);
                if (conflict is not null) return BadRequest(new { message = conflict });
            }
        }

        var date = DateOnly.TryParse(req.Date, out var dd) ? dd : DateOnly.FromDateTime(DateTime.Today);

        var b = new Booking
        {
            BookingNumber       = await _numbers.NextAsync("BOOKING", ct),
            BranchId            = branchId.Value,
            CustomerId          = req.CustomerId,
            RequestedDate       = date,
            ServiceId           = req.ServiceId,
            PortId              = req.PortId,
            DestinationId       = req.DestinationId,
            TahteeqPortId       = req.TahteeqPortId,
            TripTypeId          = req.TripTypeId,
            BookingReference    = B(req.BookingReference),
            BLNumber            = B(req.BLNumber),
            EndCustomerId       = req.EndCustomerId,
            EndCustomerName     = req.EndCustomerId is null ? B(req.EndCustomerName) : null,
            ContactId           = req.ContactId,
            CustomerReference   = B(req.CustomerReference),
            ShippingAgentId     = req.ShippingAgentId,
            GuaranteeSupplierId = req.GuaranteeSupplierId,
            Notes               = B(req.Notes),
            /* حجز الوكيل موجود فعليًا بره — بييجي مؤكد جاهز للشغل */
            Status              = "Confirmed",
            CreatedBy           = CurrentUserId()
        };

        var o = new Operation
        {
            OperationNumber = await _numbers.NextAsync("OPERATION", ct),
            BranchId        = branchId.Value,
            CustomerId      = req.CustomerId,
            ServiceId       = req.ServiceId,
            PortId          = req.PortId,
            DestinationId   = req.DestinationId,
            TahteeqPortId   = req.TahteeqPortId,
            TripTypeId      = req.TripTypeId,
            PlannedDate     = date.ToDateTime(TimeOnly.MinValue),
            Notes           = B(req.Notes),
            Status          = "Pending",
            CreatedBy       = CurrentUserId()
        };

        /* أرقام الرحلات تُجلب هنا — قبل فتح المعاملة — مثل رقمي الحجز والعملية:
           خدمة الترقيم تنفّذ أمرها على الاتصال دون ربطه بالمعاملة الجارية،
           فاستدعاؤها داخل المعاملة يُسقط الإنشاء كله بخطأ عام */
        var tripNumbers = new List<string>();
        foreach (var _ in qTrips)
            tripNumbers.Add(await _numbers.NextAsync("TRIP", ct));

        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.Bookings.Add(b);
            await _db.SaveChangesAsync(ct);

            var no = 0;
            var createdLines = new List<BookingContainerLine>();
            foreach (var l in lines)
            {
                var line = new BookingContainerLine
                {
                    BookingId       = b.BookingId,
                    LineNo          = ++no,
                    ContainerTypeId = l.ContainerTypeId,
                    RequestedQty    = l.RequestedQty,
                    AssignedQty     = 0,
                    WeightKg        = l.WeightKg is > 0 ? l.WeightKg : null,
                    Notes           = B(l.Notes),
                    GuaranteePerContainer = l.GuaranteePerContainer is > 0 ? l.GuaranteePerContainer : null,
                    Status          = "Pending",
                    CreatedBy       = CurrentUserId()
                };
                _db.BookingContainerLines.Add(line);
                createdLines.Add(line);
            }
            await _db.SaveChangesAsync(ct);

            /* تفاصيل الحاويات الفردية (بأرقامها) — تُربط بسطورها بفهرس Lines،
               بنفس قواعد شاشة الحجز الأساسية: سطر بلا رقم وبلا حاوية يُتخطى */
            if (req.ContainerDetails is { Count: > 0 })
            {
                foreach (var d in req.ContainerDetails)
                {
                    if (string.IsNullOrWhiteSpace(d.ContainerNumber) && d.ContainerId is null) continue;
                    var parent = (d.LineIndex is int ix && ix >= 0 && ix < createdLines.Count)
                        ? createdLines[ix]
                        : (createdLines.Count == 1 ? createdLines[0] : null);
                    if (parent is null) continue;
                    _db.BookingContainerDetails.Add(new BookingContainerDetail
                    {
                        BookingContainerLineId = parent.BookingContainerLineId,
                        ContainerId            = d.ContainerId,
                        ContainerNumberText    = B(d.ContainerNumber),
                        SealNumber             = B(d.SealNumber),
                        ShippingLine           = B(d.ShippingLine),
                        Blnumber               = B(d.BLNumber),
                        BookingReference       = B(d.BookingReference),
                        WeightKg               = d.WeightKg is > 0 ? d.WeightKg : null,
                        Status                 = "Pending",
                        Notes                  = B(d.Notes),
                        CreatedBy              = CurrentUserId()
                    });
                }
            }

            o.BookingId = b.BookingId;
            _db.Operations.Add(o);
            await _db.SaveChangesAsync(ct);

            /* #83 — الشاشة بقت بتبعت سطور الإيراد كلها (إنشاء وتعديل).
               السلوك القديم (سطر واحد من UnitPrice) فضل احتياطي لأي منادي تاني */
            var hasRevLines = req.RevenueLines is { Count: > 0 };
            if (hasRevLines || (req.ServiceId is not null && req.UnitPrice is > 0))
            {
                if (hasRevLines)
                {
                    AddRevenueLines(o.OperationId, req.RevenueLines!, CurrentUserId());
                }
                else if (req.ServiceId is int svcId && req.UnitPrice is > 0)
                {
                    var desc = await BuildRouteDescAsync(req.PortId, req.DestinationId, req.TahteeqPortId, ct);
                    var svcTax = await _db.Services.AsNoTracking()
                        .Where(s => s.ServiceId == svcId).Select(s => s.TaxRateId).FirstOrDefaultAsync(ct);
                    AddRevenueLines(o.OperationId,
                        /* #102 — أبعاد الطلب بتتمرر للسطر الاحتياطي، كان بيولد فاضي */
                        new List<RevenueLineDto> { new(svcId, desc, 1, req.UnitPrice.Value, 0, svcTax, null,
                            req.PortId, req.DestinationId, req.TahteeqPortId, req.TripTypeId) },
                        CurrentUserId());
                }
                /* TaxRate snapshot لازم يتملأ قبل الحفظ، والـ trigger بيعيد حساب الإيراد */
                await FillTaxRateSnapshotsAsync(ct);
                await _db.SaveChangesAsync(ct);
            }

            /* رحلات التعيين: بتتخلق مربوطة بالعملية.
               #78 — خط السير بقى من الرحلة نفسها لو المستخدم حدده، وإلا يرث من العملية */
            var seq = 0;
            foreach (var t in qTrips)
            {
                var trip = new Trip
                {
                    TripNumber     = tripNumbers[seq],
                    BranchId       = branchId.Value,
                    DriverId       = t.DriverId,
                    VehicleId      = t.VehicleId,
                    TrailerId      = t.TrailerId,
                    TripTypeId     = o.TripTypeId,
                    PortId         = t.PortId        ?? o.PortId,
                    DestinationId  = t.DestinationId ?? o.DestinationId,
                    TahteeqPortId  = t.TahteeqPortId ?? o.TahteeqPortId,
                    PlannedStartAt = Dt(t.PlannedStartAt) ?? o.PlannedDate,
                    FreightAmount  = t.FreightAmount is > 0 ? t.FreightAmount : null,
                    Status         = "Planned",
                    CreatedBy      = CurrentUserId()
                };
                _db.Trips.Add(trip);
                await _db.SaveChangesAsync(ct);

                _db.TripOperations.Add(new TripOperation
                {
                    TripId      = trip.TripId,
                    OperationId = o.OperationId,
                    SequenceNo  = ++seq,
                    Status      = "Assigned",
                    CreatedBy   = CurrentUserId()
                });
                await _db.SaveChangesAsync(ct);
            }

            /* الحاويات الفعلية تُنسخ للعملية فورًا — مثل إنشاء العملية الأساسي —
               فتظهر في ملف العملية من أول فتح */
            await CopyBookingContainersAsync(o.OperationId, b.BookingId, ct);

            await TouchBookingAsync(b.BookingId, ct);
            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Create, "Operation", null,
            description: $"إنشاء سريع — حجز وكيل {req.BookingReference}", ct: ct);

        return Ok(new
        {
            bookingId = b.BookingId, bookingNumber = b.BookingNumber,
            operationId = o.OperationId, operationNumber = o.OperationNumber,
            message = $"✅ اتفتح ملف العملية {o.OperationNumber} على حجز الوكيل {req.BookingReference}"
        });
    }

    /* نفس قاعدة TripsController.ResourceConflictAsync — منسوخة هنا عشان الإنشاء
       السريع ما يفتحش رحلات متعارضة (الكنترولر التاني خاص بيه) */
    private async Task<string?> QuickTripConflictAsync(int driverId, int vehicleId,
        DateTime? plannedStart, CancellationToken ct)
    {
        var active = await _db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status != "Cancelled" && t.Status != "Completed" &&
                        (t.VehicleId == vehicleId || t.DriverId == driverId))
            .Select(t => new { t.TripNumber, t.VehicleId, t.DriverId, t.PlannedStartAt, t.ActualStartAt, t.ActualEndAt })
            .ToListAsync(ct);

        foreach (var t in active)
        {
            var onRoad = t.ActualStartAt is not null && t.ActualEndAt is null;
            var sameDay = plannedStart is not null && t.PlannedStartAt is not null &&
                          t.PlannedStartAt.Value.Date == plannedStart.Value.Date;
            if (!onRoad && !sameDay) continue;

            if (t.VehicleId == vehicleId)
                return $"العربية دي محجوزة على الرحلة {t.TripNumber}" +
                       (onRoad ? " — بدأت ولسه ما رجعتش" : $" في نفس اليوم ({plannedStart:yyyy-MM-dd})");
            return $"السائق ده على الرحلة {t.TripNumber}" +
                   (onRoad ? " — بدأت ولسه ما رجعتش" : $" في نفس اليوم ({plannedStart:yyyy-MM-dd})");
        }
        return null;
    }

    private async Task<string?> BuildRouteDescAsync(int? portId, int? destId, int? tahId, CancellationToken ct)
    {
        async Task<string?> Name(int? id, bool isPort) => id is null ? null :
            isPort
                ? await _db.Ports.AsNoTracking().Where(p => p.PortId == id).Select(p => p.NameAr).FirstOrDefaultAsync(ct)
                : await _db.Destinations.AsNoTracking().Where(p => p.DestinationId == id).Select(p => p.NameAr).FirstOrDefaultAsync(ct);

        var from = await Name(portId, true);
        var to   = await Name(destId, false);
        var tah  = await Name(tahId, true);
        if (from is null && to is null && tah is null) return null;
        var s = from is not null && to is not null ? $"من {from} إلى {to}"
              : from is not null ? $"من {from}"
              : to is not null ? $"إلى {to}" : "";
        if (tah is not null) s = s.Length > 0 ? $"{s} — تعتيق {tah}" : $"تعتيق {tah}";
        return s;
    }

    // ═══════════════ ملف العملية: رحلاتها وحاوياتها ═══════════════

    /* #78 — خط سير كل رحلة بقى بيرجع مع الرحلات، عشان شاشة ملف العملية
       تعرف توزّع سطور الإيراد على الوجهات المختلفة */
    /* #79 — DriverId اتضاف عشان المصروف السريع يعرف يربط المصروف بسائق الرحلة */
    /* #82 — المبيت والتحويلة بيتقرأوا مع الرحلة عشان التعديل ما يمسحهمش */
    /* #102 — المقطورة ونوع الرحلة وملاحظاتها بييجوا مع الرحلة لنفس السبب:
       أي حقل بيتبعت في تحديث الرحلة ومش بييجي في الرد بيتمسح عند أي حفظ.
       والترتيب بقى (SequenceNo ثم TripId) عشان تعادل التسلسل ما يبدّلش
       ترتيب رجوع الرحلات بين تحميل وآخر فيتلخبط إقرانها بسطور الحاويات */
    public record OpTripLine(long TripId, string TripNumber, int DriverId, string DriverName, int VehicleId, string VehiclePlate,
        string Status, DateTime? PlannedStartAt, decimal? FreightAmount, int SequenceNo,
        int? PortId, int? DestinationId, int? TahteeqPortId,
        string? PortName, string? DestName, string? TahteeqName,
        decimal? OvernightAmount = null, decimal? TransferAmount = null,
        int? TrailerId = null, int? TripTypeId = null, string? Notes = null);

    [HttpGet("{id:long}/trips")]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> OperationTrips(long id, CancellationToken ct)
    {
        var hasFin = await HasFinAsync(ct);

        var rows = await _db.TripOperations.AsNoTracking()
            .Where(to => to.OperationId == id)
            .OrderBy(to => to.SequenceNo)
            .ThenBy(to => to.TripId)
            .Select(to => new OpTripLine(
                to.Trip.TripId, to.Trip.TripNumber, to.Trip.DriverId, to.Trip.Driver.FullName, to.Trip.VehicleId, to.Trip.Vehicle.PlateNumber,
                to.Trip.Status, to.Trip.PlannedStartAt,
                hasFin ? to.Trip.FreightAmount : null, to.SequenceNo,
                to.Trip.PortId, to.Trip.DestinationId, to.Trip.TahteeqPortId,
                to.Trip.Port        != null ? to.Trip.Port.NameAr        : null,
                to.Trip.Destination != null ? to.Trip.Destination.NameAr : null,
                to.Trip.TahteeqPort != null ? to.Trip.TahteeqPort.NameAr : null,
                to.Trip.OvernightAmount, to.Trip.TransferAmount,
                to.Trip.TrailerId, to.Trip.TripTypeId, to.Trip.Notes))
            .ToListAsync(ct);
        return Ok(rows);
    }

    public record OpContainerLine(long OperationContainerId, string? ContainerNumber, string? TypeName,
        string Status, string? BLNumber);

    public record BookingLineView(string? TypeName, int RequestedQty, decimal? WeightKg,
        decimal? GuaranteePerContainer, string? Notes);

    /* سطور الحاويات كما سُجلت في الحجز (النوع والعدد والوزن والضمان) —
       تُعرض في ملف العملية بجانب الحاويات الفعلية */
    [HttpGet("{id:long}/booking-lines")]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> BookingLines(long id, CancellationToken ct)
    {
        var hasFin = await HasFinAsync(ct);

        var o = await _db.Operations.AsNoTracking()
            .Where(x => x.OperationId == id && !x.IsDeleted)
            .Select(x => new { x.BookingId }).FirstOrDefaultAsync(ct);
        if (o is null) return NotFound(new { message = "العملية غير موجودة" });
        if (o.BookingId is null) return Ok(new List<BookingLineView>());

        var rows = await _db.BookingContainerLines.AsNoTracking()
            .Where(l => l.BookingId == o.BookingId.Value)
            .OrderBy(l => l.LineNo)
            .Select(l => new BookingLineView(
                l.ContainerType != null ? l.ContainerType.NameAr : null,
                l.RequestedQty, l.WeightKg,
                hasFin ? l.GuaranteePerContainer : null, l.Notes))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{id:long}/containers")]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> OperationContainers(long id, CancellationToken ct)
    {
        var rows = await _db.OperationContainers.AsNoTracking()
            .Where(oc => oc.OperationId == id)
            .OrderBy(oc => oc.MovementSequence)
            .Select(oc => new OpContainerLine(
                oc.OperationContainerId,
                oc.BookingContainerDetail != null ? oc.BookingContainerDetail.ContainerNumberText
                    : oc.Container != null ? oc.Container.ContainerNumber : null,
                oc.BookingContainerDetail != null && oc.BookingContainerDetail.BookingContainerLine != null
                    ? oc.BookingContainerDetail.BookingContainerLine.ContainerType.NameAr
                    : oc.Container != null && oc.Container.ContainerType != null ? oc.Container.ContainerType.NameAr : null,
                oc.Status, oc.Blnumber))
            .ToListAsync(ct);
        return Ok(rows);
    }

    public record OpContainerAdd(string? ContainerNumber, int ContainerTypeId, string? BLNumber);
    public record OpContainerEdit(string? BLNumber);

    /* إضافة حاوية مباشرة من ملف العملية: سجل الحاوية الرئيسي بيتعمل لو جديد
       (ملكية خط ملاحي زي ما فورم الحاويات بيبدأ)، والسطر بيتربط بالعملية */
    [HttpPost("{id:long}/containers")]
    [Authorize(Policy = "PERM:OPERATION.EDIT")]
    public async Task<IActionResult> AddOperationContainer(long id, [FromBody] OpContainerAdd req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ContainerNumber))
            return BadRequest(new { message = "رقم الحاوية مطلوب" });
        if (req.ContainerTypeId <= 0)
            return BadRequest(new { message = "اختار نوع الحاوية" });

        var o = await _db.Operations.FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية مش موجودة" });
        if (o.Status is "Closed" or "Cancelled")
            return BadRequest(new { message = "العملية مقفولة أو ملغاة — مفيش تعديل عليها" });

        var number = req.ContainerNumber.Trim().ToUpperInvariant();
        var container = await _db.Containers.FirstOrDefaultAsync(c => c.ContainerNumber == number, ct);
        if (container is null)
        {
            container = new Container
            {
                ContainerNumber = number,
                ContainerTypeId = req.ContainerTypeId,
                OwnerType       = "ShippingLine",
                CreatedBy       = CurrentUserId()
            };
            _db.Containers.Add(container);
            await _db.SaveChangesAsync(ct);
        }
        else if (container.ContainerTypeId != req.ContainerTypeId)
        {
            container.ContainerTypeId = req.ContainerTypeId;
            _db.Containers.Update(container);
            await _db.SaveChangesAsync(ct);
        }

        var seq = await _db.OperationContainers.CountAsync(oc => oc.OperationId == id, ct);
        _db.OperationContainers.Add(new OperationContainer
        {
            OperationId      = id,
            ContainerId      = container.ContainerId,
            MovementSequence = seq + 1,
            Blnumber         = B(req.BLNumber),
            Status           = "Assigned",
            CreatedBy        = CurrentUserId()
        });
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتضافت الحاوية {number} لملف العملية" });
    }

    /* تعديل بوليصة سطر حاوية من ملف العملية (الرقم نفسه بيانات مستند الوكيل —
       بيتحرر من الحجز/سجل الحاويات) */
    [HttpPut("{id:long}/containers/{cid:long}")]
    [Authorize(Policy = "PERM:OPERATION.EDIT")]
    public async Task<IActionResult> EditOperationContainer(long id, long cid, [FromBody] OpContainerEdit req, CancellationToken ct)
    {
        var oc = await _db.OperationContainers
            .FirstOrDefaultAsync(x => x.OperationContainerId == cid && x.OperationId == id, ct);
        if (oc is null) return NotFound(new { message = "سطر الحاوية مش موجود" });
        oc.Blnumber = B(req.BLNumber);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحفظ تعديل البوليصة" });
    }

    /* مزامنة حاويات الحجز اللي اتضافت بعد إنشاء العملية — النسخ الأصلي بيحصل
       وقت الإنشاء بس، وده بيجيب أي تفاصيل حاويات جديدة من غير تكرار القديم */
    [HttpPost("{id:long}/sync-containers")]
    [Authorize(Policy = "PERM:OPERATION.EDIT")]
    public async Task<IActionResult> SyncContainers(long id, CancellationToken ct)
    {
        var o = await _db.Operations.FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية مش موجودة" });
        if (o.BookingId is null)
            return BadRequest(new { message = "العملية دي من غير حجز — مفيش مصدر للمزامنة" });

        var copiedIds = await _db.OperationContainers.AsNoTracking()
            .Where(oc => oc.OperationId == id && oc.BookingContainerDetailId != null)
            .Select(oc => oc.BookingContainerDetailId!.Value).ToListAsync(ct);

        var missing = await _db.BookingContainerDetails.AsNoTracking()
            .Where(d => d.BookingContainerLine.BookingId == o.BookingId &&
                        !copiedIds.Contains(d.BookingContainerDetailId))
            .OrderBy(d => d.BookingContainerDetailId)
            .Select(d => new { d.BookingContainerDetailId, d.ContainerId, d.Blnumber })
            .ToListAsync(ct);

        var seq = await _db.OperationContainers.CountAsync(oc => oc.OperationId == id, ct);
        foreach (var d in missing)
        {
            _db.OperationContainers.Add(new OperationContainer
            {
                OperationId              = id,
                ContainerId              = d.ContainerId,
                BookingContainerDetailId = d.BookingContainerDetailId,
                MovementSequence         = ++seq,
                Blnumber                 = d.Blnumber,
                Status                   = "Assigned",
                CreatedBy                = CurrentUserId()
            });
        }
        if (missing.Count > 0) await _db.SaveChangesAsync(ct);

        /* رسالة صادقة: «لا جديد» تختلف عن «لا توجد أرقام حاويات مسجلة أصلًا» */
        var totalDetails = missing.Count > 0 ? -1
            : await _db.BookingContainerDetails.AsNoTracking()
                .CountAsync(d => d.BookingContainerLine.BookingId == o.BookingId, ct);

        return Ok(new
        {
            added = missing.Count,
            message = missing.Count > 0
                ? $"✅ اتزامنت {missing.Count} حاوية جديدة من الحجز"
                : totalDetails == 0
                    ? "لا توجد أرقام حاويات مسجلة في الحجز حتى الآن — سجّلها من شاشة الطلب، أو أضف الحاويات مباشرة من هذا الملف"
                    : "الحاويات متزامنة بالفعل — مفيش جديد في الحجز"
        });
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
            {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Update, "Operation", null, description: "تعديل عملية", ct: ct);
            return BadRequest(new { message = "العملية مقفولة أو ملغية — ماينفعش تتعدل" });
            }

        o.CustomerId    = req.CustomerId;
        o.ServiceId     = req.ServiceId;
        o.PortId        = req.PortId;
        o.DestinationId = req.DestinationId;
        o.TahteeqPortId = req.TahteeqPortId;
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

        return Ok(new { message = "✅ تم حفظ التعديل" });
    }

    /* نسخ حاويات الحجز الفعلية (BookingContainerDetails) إلى حاويات العملية —
       كل تفصيلة حاوية بتبقى صف حركة على العملية مربوط بمصدره */
    private async Task CopyBookingContainersAsync(long operationId, long bookingId, CancellationToken ct)
    {
        var details = await _db.BookingContainerDetails.AsNoTracking()
            .Where(d => d.BookingContainerLine.BookingId == bookingId)
            .OrderBy(d => d.BookingContainerDetailId)
            .Select(d => new { d.BookingContainerDetailId, d.ContainerId, d.Blnumber })
            .ToListAsync(ct);

        var seq = 0;
        foreach (var d in details)
        {
            _db.OperationContainers.Add(new OperationContainer
            {
                OperationId              = operationId,
                ContainerId              = d.ContainerId,
                BookingContainerDetailId = d.BookingContainerDetailId,
                MovementSequence         = ++seq,
                Blnumber                 = d.Blnumber,
                Status                   = "Assigned",
                CreatedBy                = CurrentUserId()
            });
        }
        if (details.Count > 0) await _db.SaveChangesAsync(ct);
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
                /* #83 — خط سير السطر ونوع رحلته بيتحفظوا (كانوا للعرض بس) */
                PortId        = l.PortId,
                DestinationId = l.DestinationId,
                TahteeqPortId = l.TahteeqPortId,
                TripTypeId    = l.TripTypeId,
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
            return BadRequest(new { message = "طلب غير صالح" });

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
        /* 🔴 أي تغيير حالة عملية → إعادة اشتقاق حالة الحجز
              (كان بيتنادى بس عند الإلغاء — عشان كده الحجوزات ما كانتش بتتحرك) */
        await TouchBookingAsync(o.BookingId, ct);
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
            {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Close, "Operation", null, description: "إقفال عملية", ct: ct);
            return BadRequest(new { message = $"حالة العملية «{o.Status}» لا تسمح بالإقفال — الإقفال من الحالات الجاهزة للإقفال بس" });
            }

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

        /* 🔴 إغلاق العملية ممكن يخلّي الحجز **مكتمل** */
        await TouchBookingAsync(o.BookingId, ct);

        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ تم اغلاق العملية" });
    }

    [HttpPost("{id:long}/reopen")]
    [Authorize(Policy = "PERM:OPERATION.REOPEN")]
    public async Task<IActionResult> Reopen(long id, CancellationToken ct)
    {
        var o = await _db.Operations.FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية غير موجودة" });
        if (o.Status is not "Closed") return BadRequest(new { message = "العملية غير مقفولة" });

        o.Status   = "Delivered";
        o.ClosedAt = null;
        o.ClosedBy = null;
        o.UpdatedAt = DateTime.UtcNow;
        o.UpdatedBy = CurrentUserId();

        /* 🔴 فتح عملية مقفولة → الحجز يرجع «قيد التنفيذ» بدل «مكتمل» */
        await TouchBookingAsync(o.BookingId, ct);

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Reopen, "Operation", null, description: "إعادة فتح عملية", ct: ct);

        return Ok(new { message = "✅ تم اعاده فتح العمليه مرة اخرى" });
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

    /* 🔴 حالات الحجز **مشروقة من العمليات المرتبطة بيه** — مش بتتغيّر بإيد:
          Draft      → المستخدم (مسودة)
          Confirmed  → المستخدم (تأكيد)  ·  Cancelled → المستخدم (إلغاء)
          Assigned   → فيه عمليات لسه ما بدأتش
          InProgress → فيه عملية اشتغلت فعلًا
          Completed  → كل العمليات اتقفلت                                          */
    private static readonly string[] StartedOp =
        { "DriverReceived", "InTransit", "Delivered", "ExpensesPending",
          "CustodyPending", "ReadyToClose" };

    private async Task TouchBookingAsync(long? bookingId, CancellationToken ct)
    {
        if (bookingId is null) return;

        var b = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b is null) return;

        /* 🔴 الحجز الملغي أو المكتمل **مايتحرّكش من هنا** — عشان
              إلغاء عملية ما يعيدش حجز مقفول للحياة. */
        if (b.Status is "Cancelled" or "Completed") return;

        /* استعلام واحد — والتقييم في الذاكرة عشان ما ندخلش في مشاكل
              ترجمة GroupBy/Any المتداخلة (مسجّلة في §EF translation). */
        var ops = await _db.Operations.AsNoTracking()
            .Where(o => o.BookingId == bookingId && !o.IsDeleted && o.Status != "Cancelled")
            .Select(o => o.Status)
            .ToListAsync(ct);

        var next = ops.Count == 0
            ? "Confirmed"
            : ops.All(st => st == "Closed")
                ? "Completed"
                : ops.Any(st => StartedOp.Contains(st))
                    ? "InProgress"
                    : "Assigned";

        /* مافيش عملية أصلاً → نرجّعه مؤكد (اللى أكده المستخدم) */
        if (ops.Count == 0 && b.Status is not ("Draft" or "Confirmed")) next = "Confirmed";
        if (b.Status == next) return;

        b.Status    = next;
        b.UpdatedAt = DateTime.UtcNow;
        b.UpdatedBy = CurrentUserId();
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:OPERATION.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var o = await _db.Operations.FirstOrDefaultAsync(x => x.OperationId == id && !x.IsDeleted, ct);
        if (o is null) return NotFound(new { message = "العملية غير موجودة" });

        if (o.Status is not ("Pending" or "Cancelled"))
            {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Delete, "Operation", null, description: "حذف عملية", ct: ct);
            return BadRequest(new { message = "حذف العملية للمعلّقة أو الملغية بس" });
            }

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

        return Ok(new { message = "✅ تم حذف العملية" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(OperationUpsert? r, CancellationToken ct)
    {
        if (r is null) return "البيانات غير كاملة";
        if (r.CustomerId <= 0) return "لابد من اختيار العميل";

        if (!await _db.Customers.AnyAsync(c => c.CustomerId == r.CustomerId && !c.IsDeleted, ct))
            return "العميل غير موجود";

        if (r.BookingId is not null)
        {
            var bk = await _db.Bookings.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookingId == r.BookingId && !b.IsDeleted, ct);
            if (bk is null) return "الحجز غير موجود";
            if (bk.CustomerId != r.CustomerId) return "الحجز غير تابع لهذا العميل";
        }

        if (r.ServiceId is not null && !await _db.Services.AnyAsync(x => x.ServiceId == r.ServiceId, ct))
            return "الخدمة غير موجودة";
        if (r.PortId is not null && !await _db.Ports.AnyAsync(x => x.PortId == r.PortId, ct))
            return "الميناء غير موجود";
        if (r.DestinationId is not null && !await _db.Destinations.AnyAsync(x => x.DestinationId == r.DestinationId, ct))
            return "الجهة غير موجودة";
        if (r.TahteeqPortId is not null && !await _db.Ports.AnyAsync(x => x.PortId == r.TahteeqPortId, ct))
            return "ميناء التعتيق غير موجود";
        if (r.TripTypeId is not null && !await _db.TripTypes.AnyAsync(x => x.TripTypeId == r.TripTypeId, ct))
            return "نوع الرحلة غير موجود";

        if (r.EstimatedCost < 0) return "التكلفة التقديرية ماينفعش تكون سالبة";

        if (r.RevenueLines is not null)
        {
            var svcIds = r.RevenueLines.Select(l => l.ServiceId).Distinct().ToList();
            var found = await _db.Services.Where(s => svcIds.Contains(s.ServiceId))
                .Select(s => s.ServiceId).ToListAsync(ct);
            if (found.Count != svcIds.Count) return "فيه خدمة في سطور الإيراد غير موجودة";

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
