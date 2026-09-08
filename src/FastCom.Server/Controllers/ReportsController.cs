using System.Globalization;
using System.Security.Claims;
using ClosedXML.Excel;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers;

/// <summary>
/// 📊 التقارير — عمليات / مالي / أسطول + تصدير Excel.
///
/// <para>🔴 <b>كل التجميع بيتم في الذاكرة.</b> بنجيب أعمدة قليلة من SQL
/// بـ <c>ToListAsync</c> وبعدين <c>GroupBy</c>/<c>Sum</c>/<c>OrderBy</c> في C# —
/// لأن EF Core 8 بيرفض ترجمة <c>GroupBy</c> + <c>OrderBy</c> + <c>Take</c>
/// في سلسلة واحدة (بينفجر وقت التشغيل مش وقت الـ Build).</para>
///
/// <para>🔴 مافيش <c>GroupBy</c> داخل سلسلة EF في الملف ده خالص.</para>
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly IPermissionService _perms;

    public ReportsController(FastComDbContext db, IPermissionService perms)
    {
        _db    = db;
        _perms = perms;
    }

    private const int MaxRangeDays = 366;
    private const int MaxRows      = 2000;

    /// <summary>العمليات اللي لسه شغالة (نفس تعريف الداشبورد).</summary>
    private static readonly string[] OpenOpStatuses =
    {
        "Pending", "Assigned", "DriverReceived", "InTransit", "Delivered",
        "ExpensesPending", "CustodyPending", "ReadyToClose"
    };

    private static readonly string[] OpenCustodyStatuses = { "Open", "PartiallySettled", "Submitted" };

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private async Task<bool> Can(string code, CancellationToken ct) =>
        await _perms.HasAsync(CurrentUserId(), code, ct);

    /// <summary>بيعامل <c>from</c>/<c>to</c> كنصوص ISO ويظبطهم في حدود معقولة.</summary>
    private static (DateTime f, DateTime t, string fs, string ts) Range(string? from, string? to)
    {
        var t = DateOnly.TryParse(to, out var td) ? td : DateOnly.FromDateTime(DateTime.Today);
        var f = DateOnly.TryParse(from, out var fd) ? fd : t.AddMonths(-3);
        if (f > t) (f, t) = (t, f);
        if (t.ToDateTime(TimeOnly.MinValue) - f.ToDateTime(TimeOnly.MinValue) > TimeSpan.FromDays(MaxRangeDays))
            f = t.AddMonths(-3);

        // 🔴 من آخر اليوم — عشان الحركات اللي حصلت النهاردة تدخل في التقرير
        return (f.ToDateTime(TimeOnly.MinValue),
                t.ToDateTime(TimeOnly.MaxValue),
                f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    // ══════════════════════════════════════════════════════════════════
    //  الصلاحيات
    // ══════════════════════════════════════════════════════════════════

    [HttpGet("can")]
    public async Task<IActionResult> Permissions(CancellationToken ct) => Ok(new
    {
        operations = await Can("REPORT.OPERATIONS", ct),
        financial  = await Can("REPORT.FINANCIAL", ct),
        fleet      = await Can("REPORT.FLEET", ct),
        export     = await Can("REPORT.EXPORT", ct)
    });

    // ══════════════════════════════════════════════════════════════════
    //  1) العمليات
    // ══════════════════════════════════════════════════════════════════

    [HttpGet("operations")]
    public async Task<IActionResult> Operations(string? from, string? to,
        int? customerId, string? status, CancellationToken ct)
    {
        if (!await Can("REPORT.OPERATIONS", ct)) return Forbid();

        var (f, t, fs, ts) = Range(from, to);
        var (rows, tot) = await BuildOperationsAsync(f, t, fs, ts, customerId, status, ct);
        return Ok(new { range = new { from = fs, to = ts }, totals = tot, rows });
    }

    private async Task<(List<OpRow> Rows, OpTotals Tot)> BuildOperationsAsync(
        DateTime f, DateTime t, string fs, string ts,
        int? customerId, string? status, CancellationToken ct)
    {
        var ops = await _db.Operations.AsNoTracking()
            .Where(o => !o.IsDeleted && o.CreatedAt >= f && o.CreatedAt <= t)
            .Where(o => customerId == null || o.CustomerId == customerId)
            .Where(o => status == null || o.Status == status)
            .OrderByDescending(o => o.OperationId)
            .Take(MaxRows)
            .Select(o => new
            {
                o.OperationId, o.OperationNumber, o.CustomerId, o.Status,
                o.PlannedDate, o.ActualDeliveryAt,
                o.RevenueNet, o.RevenueTax, o.ActualCost, o.EstimatedCost
            })
            .ToListAsync(ct);

        if (ops.Count == 0)
            return (new List<OpRow>(), new OpTotals(0, 0, 0, 0, 0, 0, 0, 0, 0));

        var opIds = ops.Select(o => o.OperationId).ToList();
        var custIds = ops.Select(o => o.CustomerId).Distinct().ToList();

        // ── اسم العميل ──
        var cust = (await _db.Customers.AsNoTracking()
                .Where(c => custIds.Contains(c.CustomerId))
                .Select(c => new { c.CustomerId, c.NameAr }).ToListAsync(ct))
            .ToDictionary(x => x.CustomerId, x => x.NameAr);

        // ── التكلفة المباشرة: مصروفات مربوطة بالعملية ──
        var exp = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Status != "Cancelled"
                        && e.OperationId != null && opIds.Contains(e.OperationId.Value))
            .Select(e => new { OpId = e.OperationId!.Value, e.Amount }).ToListAsync(ct);

        // ── نصيب العملية من تكلفة الرحلات ──
        var alloc = await _db.TripCostAllocations.AsNoTracking()
            .Where(a => opIds.Contains(a.OperationId))
            .Select(a => new { a.OperationId, a.AllocatedAmount }).ToListAsync(ct);

        // ── المفوتر: من بنود الفواتير (عشان مانعدّش فاتورة أكتر من مرة) ──
        var invItems = await _db.InvoiceItems.AsNoTracking()
            .Where(ii => ii.OperationId != null && opIds.Contains(ii.OperationId.Value)
                         && !ii.Invoice.IsDeleted && ii.Invoice.Status != "Cancelled")
            .Select(ii => new { OpId = ii.OperationId!.Value, ii.InvoiceId, Amount = ii.LineTotal ?? 0m })
            .ToListAsync(ct);

        // ── المحصّل: من رأس الفاتورة (PaidAmount بيحافظ عليه الـ trigger) ──
        var invIds = invItems.Select(x => x.InvoiceId).Distinct().ToList();
        // 🔴 CS0173: الـ ternary لازم يكون له نوع واحد — anonymous type مالوش اسم،
        //    فعرّفنا record صغير عشان الفرعين يلتقوا.
        var head = invIds.Count == 0
            ? new List<InvoiceHead>()
            : (await _db.Invoices.AsNoTracking()
                    .Where(i => invIds.Contains(i.InvoiceId))
                    .Select(i => new { i.InvoiceId, i.InvoiceNumber, i.PaidAmount })
                    .ToListAsync(ct))
                .Select(i => new InvoiceHead(i.InvoiceId, i.InvoiceNumber, i.PaidAmount))
                .ToList();

        var paidByInv = head.ToDictionary(x => x.InvoiceId, x => (No: x.InvoiceNumber, Paid: x.PaidAmount));

        var rows = new List<OpRow>(ops.Count);
        decimal tRev = 0, tCost = 0, tProfit = 0, tInv = 0, tCol = 0;
        int open = 0, notInv = 0;

        foreach (var o in ops)
        {
            var direct  = exp.Where(x => x.OpId == o.OperationId).Sum(x => x.Amount);
            var tripCs  = alloc.Where(x => x.OperationId == o.OperationId).Sum(x => x.AllocatedAmount);
            var cost    = direct + tripCs;
            var profit  = o.RevenueNet - cost;
            var margin  = o.RevenueNet > 0 ? profit / o.RevenueNet * 100m : 0m;

            var lines = invItems.Where(x => x.OpId == o.OperationId).ToList();
            var invoiced = lines.Sum(x => x.Amount);
            var collected = lines.Select(x => x.InvoiceId).Distinct()
                .Sum(id => paidByInv.TryGetValue(id, out var h) ? h.Paid : 0m);
            var invNo = lines.Select(x => x.InvoiceId).Distinct()
                .Select(id => paidByInv.TryGetValue(id, out var h) ? h.No : null)
                .FirstOrDefault(x => x is not null);

            var isOpen = OpenOpStatuses.Contains(o.Status);
            if (isOpen) open++;
            if (invoiced == 0 && o.RevenueNet > 0) notInv++;

            tRev += o.RevenueNet; tCost += cost; tProfit += profit;
            tInv += invoiced; tCol += collected;

            rows.Add(new OpRow(o.OperationId, o.OperationNumber,
                cust.TryGetValue(o.CustomerId, out var cn) ? cn : "—",
                o.Status, isOpen,
                o.PlannedDate is null ? null : DateOnly.FromDateTime(o.PlannedDate.Value).ToString("yyyy-MM-dd"),
                o.ActualDeliveryAt is null ? null : DateOnly.FromDateTime(o.ActualDeliveryAt.Value).ToString("yyyy-MM-dd"),
                o.RevenueNet, direct, tripCs, cost, profit, Math.Round(margin, 2),
                invoiced, collected, o.RevenueNet - collected, invNo));
        }

        var tot = new OpTotals(ops.Count, tRev, tCost, tProfit,
            tRev > 0 ? Math.Round(tProfit / tRev * 100m, 2) : 0m, tInv, tCol, open, notInv);
        return (rows, tot);
    }

    // ══════════════════════════════════════════════════════════════════
    //  2) المالي
    // ══════════════════════════════════════════════════════════════════

    [HttpGet("financial")]
    public async Task<IActionResult> Financial(string? from, string? to, CancellationToken ct)
    {
        if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();

        var (f, t, fs, ts) = Range(from, to);
        var (months, expenses, custs, receivable, summary) =
            await BuildFinancialAsync(f, t, fs, ts, ct);
        return Ok(new { range = new { from = fs, to = ts }, summary, months, expenses, customers = custs, receivable });
    }

    private async Task<(List<MonthRow>, List<ExpenseRow>, List<CustomerRow>, decimal, FinSummary)>
        BuildFinancialAsync(DateTime f, DateTime t, string fs, string ts, CancellationToken ct)
    {
        // ── الفواتير ──
        var inv = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.InvoiceDate >= DateOnly.FromDateTime(f)
                        && i.InvoiceDate <= DateOnly.FromDateTime(t))
            .Select(i => new { i.CustomerId, i.InvoiceDate, i.GrandTotal, i.PaidAmount })
            .ToListAsync(ct);

        // ── الدفعات ──
        var pay = await _db.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status != "Cancelled"
                        && p.PaymentDate >= DateOnly.FromDateTime(f)
                        && p.PaymentDate <= DateOnly.FromDateTime(t))
            .Select(p => new { p.CustomerId, p.PaymentDate, p.Amount })
            .ToListAsync(ct);

        // ── المصروفات ──
        var expAll = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Status != "Cancelled"
                        && e.ExpenseDate >= f && e.ExpenseDate <= t)
            .Select(e => new { e.ExpenseTypeId, e.ExpenseDate, e.Amount, e.IsApproved })
            .ToListAsync(ct);

        var custIds = inv.Select(x => x.CustomerId)
                         .Concat(pay.Select(x => x.CustomerId)).Distinct().ToList();
        var custName = custIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Customers.AsNoTracking()
                    .Where(c => custIds.Contains(c.CustomerId))
                    .Select(c => new { c.CustomerId, c.NameAr }).ToListAsync(ct))
                .ToDictionary(x => x.CustomerId, x => x.NameAr);

        var typeIds = expAll.Select(x => x.ExpenseTypeId).Distinct().ToList();
        var typeName = typeIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.ExpenseTypes.AsNoTracking()
                    .Where(e => typeIds.Contains(e.ExpenseTypeId))
                    .Select(e => new { e.ExpenseTypeId, e.NameAr }).ToListAsync(ct))
                .ToDictionary(x => x.ExpenseTypeId, x => x.NameAr);

        string Mn(int y, int m) => new DateTime(y, m, 1).ToString("MMMM yyyy", new CultureInfo("ar-EG"));

        // ── التدفق الشهري (تجميع في الذاكرة) ──
        var months = inv.Select(x => (x.InvoiceDate.Year, x.InvoiceDate.Month, Inv: x.GrandTotal,
                                      Paid: 0m, Exp: 0m))
            .Concat(pay.Select(x => (x.PaymentDate.Year, x.PaymentDate.Month, Inv: 0m,
                                     Paid: x.Amount, Exp: 0m)))
            .Concat(expAll.Select(x => (DateOnly.FromDateTime(x.ExpenseDate).Year,
                                        DateOnly.FromDateTime(x.ExpenseDate).Month,
                                        Inv: 0m, Paid: 0m, Exp: x.Amount)))
            .GroupBy(x => (x.Year, x.Month))
            .Select(g => new MonthRow($"{g.Key.Year}-{g.Key.Month:D2}", Mn(g.Key.Year, g.Key.Month),
                g.Sum(x => x.Inv), g.Sum(x => x.Paid), g.Sum(x => x.Exp),
                g.Sum(x => x.Paid) - g.Sum(x => x.Exp)))
            .OrderBy(x => x.Month, StringComparer.Ordinal)
            .ToList();

        // ── المصروفات حسب النوع ──
        var expenses = expAll
            .GroupBy(x => x.ExpenseTypeId)
            .Select(g => new ExpenseRow(
                typeName.TryGetValue(g.Key, out var n) ? n : "—",
                g.Count(), g.Sum(x => x.Amount),
                g.Where(x => x.IsApproved).Sum(x => x.Amount)))
            .OrderByDescending(x => x.Total)
            .ToList();

        // ── رصيد العملاء ──
        var custs = custIds.Select(id =>
            {
                var billed = inv.Where(x => x.CustomerId == id).Sum(x => x.GrandTotal);
                var paidC  = pay.Where(x => x.CustomerId == id).Sum(x => x.Amount);
                return (Id: id, billed, paidC);
            })
            .Where(x => x.billed != 0 || x.paidC != 0)
            .Select(x => new CustomerRow(x.Id,
                custName.TryGetValue(x.Id, out var n) ? n : "—",
                x.billed, x.paidC, x.billed - x.paidC))
            .OrderByDescending(x => x.Balance)
            .ToList();

        // 🔴 رصيد المدينين = كل الفواتير المفتوحة **مش بس الفترة المختارة**
        var receivable = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.GrandTotal > i.PaidAmount)
            .Select(i => i.GrandTotal - i.PaidAmount)
            .SumAsync(ct);

        var sum = new FinSummary(
            inv.Sum(x => x.GrandTotal),
            pay.Sum(x => x.Amount),
            expAll.Sum(x => x.Amount),
            receivable,
            pay.Sum(x => x.Amount) - expAll.Sum(x => x.Amount),
            inv.Count, pay.Count);

        return (months, expenses, custs, receivable, sum);
    }

    // ══════════════════════════════════════════════════════════════════
    //  3) الأسطول
    // ══════════════════════════════════════════════════════════════════

    [HttpGet("fleet")]
    public async Task<IActionResult> Fleet(string? from, string? to, CancellationToken ct)
    {
        if (!await Can("REPORT.FLEET", ct)) return Forbid();

        var (f, t, fs, ts) = Range(from, to);
        var (drivers, vehicles, tot) = await BuildFleetAsync(f, t, ct);
        return Ok(new { range = new { from = fs, to = ts }, totals = tot, drivers, vehicles });
    }

    private async Task<(List<DriverRow>, List<VehicleRow>, FleetTotals)>
        BuildFleetAsync(DateTime f, DateTime t, CancellationToken ct)
    {
        var trips = await _db.Trips.AsNoTracking()
            .Where(x => !x.IsDeleted && x.PlannedStartAt >= f && x.PlannedStartAt <= t)
            .OrderByDescending(x => x.TripId)
            .Take(MaxRows)
            .Select(x => new
            {
                x.TripId, x.DriverId, x.VehicleId, x.Status,
                x.ActualStartAt, x.ActualEndAt, x.TotalDistanceKm, x.StartOdometer, x.EndOdometer
            })
            .ToListAsync(ct);

        var tripIds = trips.Select(x => x.TripId).ToList();

        // ── إيرادات العمليات المنفّذة في الرحلة ──
        var revByTrip = new Dictionary<long, decimal>();
        if (tripIds.Count > 0)
        {
            var links = await _db.TripOperations.AsNoTracking()
                .Where(x => tripIds.Contains(x.TripId))
                .Select(x => new { x.TripId, x.OperationId }).ToListAsync(ct);

            var oids = links.Select(x => x.OperationId).Distinct().ToList();
            var rev = oids.Count == 0
                ? new Dictionary<long, decimal>()
                : (await _db.Operations.AsNoTracking()
                        .Where(o => oids.Contains(o.OperationId) && !o.IsDeleted)
                        .Select(o => new { o.OperationId, o.RevenueNet }).ToListAsync(ct))
                    .ToDictionary(x => x.OperationId, x => x.RevenueNet);

            revByTrip = links
                .GroupBy(x => x.TripId)
                .ToDictionary(g => g.Key,
                              g => g.Select(x => x.OperationId).Distinct()
                                    .Sum(id => rev.TryGetValue(id, out var r) ? r : 0m));
        }

        // ── تكلفة الرحلات ──
        var costByTrip = new Dictionary<long, decimal>();
        if (tripIds.Count > 0)
        {
            var ca = await _db.TripCostAllocations.AsNoTracking()
                .Where(x => tripIds.Contains(x.TripId))
                .Select(x => new { x.TripId, x.AllocatedAmount }).ToListAsync(ct);
            // في الذاكرة (ca قائمة C# مش IQueryable)
            foreach (var x in ca)
                costByTrip[x.TripId] =
                    (costByTrip.TryGetValue(x.TripId, out var v) ? v : 0m) + x.AllocatedAmount;
        }

        // ── العُهد ──
        var custody = await _db.DriverCustodies.AsNoTracking()
            .Where(c => !c.IsDeleted && c.OwnerType == "Driver"
                        && c.CustodyDate >= f && c.CustodyDate <= t)
            .Select(c => new { c.OwnerId, c.AmountIssued, c.Status }).ToListAsync(ct);

        var dIds = trips.Select(x => x.DriverId).Distinct().ToList();
        var driverName = dIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Drivers.AsNoTracking()
                    .Where(d => dIds.Contains(d.DriverId))
                    .Select(d => new { d.DriverId, d.FullName }).ToListAsync(ct))
                .ToDictionary(x => x.DriverId, x => x.FullName);

        var vIds = trips.Select(x => x.VehicleId).Distinct().ToList();
        var vehPlate = vIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Vehicles.AsNoTracking()
                    .Where(v => vIds.Contains(v.VehicleId))
                    .Select(v => new { v.VehicleId, v.PlateNumber }).ToListAsync(ct))
                .ToDictionary(x => x.VehicleId, x => x.PlateNumber);

        // ── الصيانة في الفترة ──
        var maintAll = await _db.VehicleMaintenances.AsNoTracking()
            .Where(m => !m.IsDeleted
                        && m.MaintenanceDate >= DateOnly.FromDateTime(f)
                        && m.MaintenanceDate <= DateOnly.FromDateTime(t))
            .Select(m => new { m.VehicleId, m.Cost }).ToListAsync(ct);

        var drivers = dIds.Select(id =>
            {
                var ts = trips.Where(x => x.DriverId == id).ToList();
                return new DriverRow(id,
                    driverName.TryGetValue(id, out var n) ? n : "—",
                    ts.Count,
                    ts.Count(x => x.Status == "Completed"),
                    ts.Sum(x => x.TotalDistanceKm ?? 0m),
                    Math.Round(ts.Average(x => x.ActualStartAt is not null && x.ActualEndAt is not null
                        ? (decimal?)(decimal)(x.ActualEndAt.Value - x.ActualStartAt.Value).TotalHours
                        : null) ?? 0m, 1),
                    custody.Where(x => x.OwnerId == id).Sum(x => x.AmountIssued),
                    custody.Count(x => x.OwnerId == id && OpenCustodyStatuses.Contains(x.Status)));
            })
            .OrderByDescending(x => x.Trips)
            .ToList();

        var vehicles = vIds.Select(id =>
            {
                var ts = trips.Where(x => x.VehicleId == id).ToList();
                var km = ts.Sum(x => x.TotalDistanceKm ?? 0m);
                if (km == 0)
                {
                    var odo = ts.Where(x => x.StartOdometer is not null && x.EndOdometer is not null).ToList();
                    if (odo.Count > 0) km = odo.Sum(x => x.EndOdometer!.Value - x.StartOdometer!.Value);
                }
                var cost = costByTrip.Where(kv => ts.Any(x => x.TripId == kv.Key)).Sum(kv => kv.Value);
                var revT = revByTrip.Where(kv => ts.Any(x => x.TripId == kv.Key)).Sum(kv => kv.Value);

                return new VehicleRow(id,
                    vehPlate.TryGetValue(id, out var p) ? p : "—",
                    ts.Count, ts.Count(x => x.Status == "Completed"),
                    km, cost,
                    maintAll.Where(x => x.VehicleId == id).Count(),
                    maintAll.Where(x => x.VehicleId == id).Sum(x => x.Cost),
                    revT, revT - cost,
                    km > 0 ? Math.Round(cost / km, 2) : 0m);
            })
            .OrderByDescending(x => x.Trips)
            .ToList();

        var tot = new FleetTotals(
            trips.Count,
            trips.Count(x => x.Status == "Completed"),
            trips.Sum(x => x.TotalDistanceKm ?? 0m),
            maintAll.Sum(x => x.Cost),
            custody.Count(x => OpenCustodyStatuses.Contains(x.Status)),
            custody.Where(x => OpenCustodyStatuses.Contains(x.Status)).Sum(x => x.AmountIssued));

        return (drivers, vehicles, tot);
    }

    // ══════════════════════════════════════════════════════════════════
    //  4) تصدير Excel
    // ══════════════════════════════════════════════════════════════════

    [HttpGet("excel")]
    public async Task<IActionResult> Excel(string report, string? from, string? to,
        int? customerId, string? status, CancellationToken ct)
    {
        if (!await Can("REPORT.EXPORT", ct)) return Forbid();

        report = (report ?? "").Trim().ToLowerInvariant();
        var (f, t, fs, ts) = Range(from, to);

        using var wb = new XLWorkbook();
        string name;

        if (report == "operations")
        {
            if (!await Can("REPORT.OPERATIONS", ct)) return Forbid();
            var (rows, tot) = await BuildOperationsAsync(f, t, fs, ts, customerId, status, ct);
            var cols = new[] { "رقم العملية", "العميل", "الحالة", "تاريخ مخطط", "تاريخ التسليم",
                "الإيراد", "تكلفة مباشرة", "تكلفة رحلات", "إجمالي التكلفة", "صافي الربح",
                "هامش %", "المفوتر", "المحصّل", "المتبقي", "رقم الفاتورة" };
            var data = rows.Select(r => new object[] {
                r.OperationNumber, r.CustomerName, r.Status, r.PlannedDate ?? "—",
                r.DeliveredAt ?? "—", r.Revenue, r.DirectCost, r.AllocatedTripCost, r.TotalCost,
                r.Profit, r.MarginPct, r.Invoiced, r.Collected, r.Due, r.InvoiceNumber ?? "—" });
            Write(wb, "العمليات", "تقرير العمليات", fs, ts, cols, data);

            var s = wb.Worksheet(1);
            var lr = rows.Count + 5;
            s.Cell(lr, 1).Value = "الإجمالي";
            s.Cell(lr, 6).Value  = tot.Revenue;
            s.Cell(lr, 9).Value  = tot.Cost;
            s.Cell(lr, 10).Value = tot.Profit;
            s.Cell(lr, 11).Value = tot.MarginPct;
            s.Cell(lr, 12).Value = tot.Invoiced;
            s.Cell(lr, 13).Value = tot.Collected;
            s.Range(lr, 1, lr, cols.Length).Style.Font.Bold = true;
            name = $"operations_{fs}_{ts}.xlsx";
        }
        else if (report == "financial")
        {
            if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();
            var (months, expenses, custs, _, _) = await BuildFinancialAsync(f, t, fs, ts, ct);

            Write(wb, "التدفق الشهري", "التدفق النقدي الشهري", fs, ts,
                new[] { "الشهر", "المفوتر", "المحصّل", "المصروفات", "الصافي" },
                months.Select(m => new object[] { m.MonthName, m.Invoiced, m.Collected, m.Expenses, m.Net }));

            Write(wb, "المصروفات", "المصروفات حسب النوع", fs, ts,
                new[] { "نوع المصروف", "العدد", "الإجمالي", "المعتمد" },
                expenses.Select(e => new object[] { e.TypeName, e.Count, e.Total, e.Approved }));

            Write(wb, "أرصدة العملاء", "أرصدة العملاء", fs, ts,
                new[] { "العميل", "المفوتر", "المحصّل", "الرصيد" },
                custs.Select(c => new object[] { c.CustomerName, c.Invoiced, c.Paid, c.Balance }));

            name = $"financial_{fs}_{ts}.xlsx";
        }
        else if (report == "fleet")
        {
            if (!await Can("REPORT.FLEET", ct)) return Forbid();
            var (drs, vehs, _) = await BuildFleetAsync(f, t, ct);

            Write(wb, "السائقون", "أداء السائقين", fs, ts,
                new[] { "السائق", "الرحلات", "المكتملة", "المسافة (كم)", "متوسط الساعات",
                        "العهدة المصروفة", "عهد مفتوحة" },
                drs.Select(d => new object[] { d.DriverName, d.Trips, d.Completed,
                    d.TotalKm, d.AvgHours, d.CustodyIssued, d.OpenCustodies }));

            Write(wb, "السيارات", "أداء السيارات", fs, ts,
                new[] { "رقم اللوحة", "الرحلات", "المكتملة", "المسافة (كم)", "تكلفة التشغيل",
                        "صيانة (عدد)", "صيانة (جنيه)", "الإيراد", "الربح", "تكلفة/كم" },
                vehs.Select(v => new object[] { v.PlateNumber, v.Trips, v.Completed, v.TotalKm,
                    v.TripCost, v.MaintCount, v.MaintCost, v.Revenue, v.Profit, v.CostPerKm }));

            name = $"fleet_{fs}_{ts}.xlsx";
        }
        else
        {
            return BadRequest(new { message = "نوع التقرير غير معروف" });
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    /// <summary>بيكتب ورقة Excel — عناوين عربية، أعمدة مظبوطة، ومحاذاة يمين.</summary>
    private static void Write(XLWorkbook wb, string sheetName, string title,
        string from, string to, string[] cols, IEnumerable<object[]> data)
    {
        var ws = wb.AddWorksheet(sheetName);
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = title;
        ws.Cell(2, 1).Value = $"الفترة: {from} → {to}";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Style.Font.SetItalic().Font.SetFontSize(10);

        var hr = 4;
        for (var c = 0; c < cols.Length; c++)
        {
            var cell = ws.Cell(hr, c + 1);
            cell.Value = cols[c];
            cell.Style.Font.SetBold();
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#16233A");
            cell.Style.Font.FontColor = XLColor.FromHtml("#FFFFFF");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var r = hr + 1;
        foreach (var row in data)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case decimal d: cell.SetValue(d);  break;
                    case int i:     cell.SetValue(i);  break;
                    case string sx: cell.Value = sx;   break;
                    case null:      cell.Value = "—";  break;
                    default:        cell.Value = row[c]?.ToString() ?? "—"; break;
                }
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (row[c] is string) cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
            r++;
        }

        if (r == hr + 1) ws.Cell(r, 1).Value = "مافيش بيانات في الفترة دي";

        for (var c = 1; c <= cols.Length; c++)
            ws.Column(c).Width = Math.Max(12, Math.Min(34, cols[c - 1].Length * 2 + 8));

        ws.SheetView.FreezeRows(hr);
    }

    // ══════════════════════════════════════════════════════════════════
    //  DTOs
    // ══════════════════════════════════════════════════════════════════

    /// <summary>رأس فاتورة — record صغير عشان الـ ternary يبقى له نوع واحد (CS0173).</summary>
    private sealed record InvoiceHead(long InvoiceId, string InvoiceNumber, decimal PaidAmount);

    public record OpRow(long OperationId, string OperationNumber, string CustomerName,
        string Status, bool IsOpen, string? PlannedDate, string? DeliveredAt,
        decimal Revenue, decimal DirectCost, decimal AllocatedTripCost, decimal TotalCost,
        decimal Profit, decimal MarginPct, decimal Invoiced, decimal Collected,
        decimal Due, string? InvoiceNumber);

    public record OpTotals(int Count, decimal Revenue, decimal Cost, decimal Profit,
        decimal MarginPct, decimal Invoiced, decimal Collected, int Open, int NotInvoiced);

    public record FinSummary(decimal Invoiced, decimal Collected, decimal Expenses,
        decimal Receivable, decimal NetCash, int InvoiceCount, int PaymentCount);

    public record MonthRow(string Month, string MonthName, decimal Invoiced,
        decimal Collected, decimal Expenses, decimal Net);

    public record ExpenseRow(string TypeName, int Count, decimal Total, decimal Approved);

    public record CustomerRow(int CustomerId, string CustomerName,
        decimal Invoiced, decimal Paid, decimal Balance);

    public record DriverRow(int DriverId, string DriverName, int Trips, int Completed,
        decimal TotalKm, decimal AvgHours, decimal CustodyIssued, int OpenCustodies);

    public record VehicleRow(int VehicleId, string PlateNumber, int Trips, int Completed,
        decimal TotalKm, decimal TripCost, int MaintCount, decimal MaintCost,
        decimal Revenue, decimal Profit, decimal CostPerKm);

    public record FleetTotals(int Trips, int Completed, decimal TotalKm,
        decimal MaintCost, int Custodies, decimal CustodyOut);
}
