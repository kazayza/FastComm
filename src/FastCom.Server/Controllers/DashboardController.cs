using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers;

/// <summary>
/// 📊 لوحة المؤشرات — الصفحة الرئيسية.
///
/// <para>🔴 كل الأرقام بتتحسب من <b>الجداول الأساسية</b> مباشرة، مش من
/// <c>vw_OperationProfitability</c> — عشان الصفحة الرئيسية تشتغل حتى لو
/// الـ View لسه ماتحدّثتش عند العميل. الـ View هتُستخدم في صفحة التقارير.</para>
///
/// <para>مافيش كتابة هنا خالص — قراءة بس.</para>
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly FastComDbContext _db;

    public DashboardController(FastComDbContext db) => _db = db;

    /// <summary>الحالات اللي بتعتبر «العملية لسه شغالة».</summary>
    private static readonly string[] OpenOpStatuses =
    {
        "Pending", "Assigned", "DriverReceived", "InTransit", "Delivered",
        "ExpensesPending", "CustodyPending", "ReadyToClose"
    };

    /// <summary>الحالات اللي بتعتبر «الرحلة لسه شغالة».</summary>
    private static readonly string[] OpenTripStatuses = { "Planned", "Assigned", "Started" };

    /// <summary>الحالات اللي بتعتبر «العهدة لسه مفتوحة».</summary>
    private static readonly string[] OpenCustodyStatuses = { "Open", "PartiallySettled", "Submitted" };

    // ══════════════════════════════════════════════════════════════════

    /// <summary>كل أرقام الصفحة الرئيسية في طلب واحد.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var today    = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // ── العمليات ──
        var openOps = await _db.Operations.CountAsync(
            o => !o.IsDeleted && OpenOpStatuses.Contains(o.Status), ct);

        // 🔴 Operation.PlannedDate نوعها DateTime? — DateOnly مينفعش تتقارن بيها
        var todayDt = DateTime.Today;
        var delayedOps = await _db.Operations.CountAsync(
            o => !o.IsDeleted && OpenOpStatuses.Contains(o.Status)
                 && o.PlannedDate < todayDt, ct);

        var monthStartDt = monthStart.ToDateTime(TimeOnly.MinValue);
        var closedThisMonth = await _db.Operations.CountAsync(
            o => !o.IsDeleted && o.Status == "Closed"
                 && o.ActualDeliveryAt != null
                 && o.ActualDeliveryAt >= monthStartDt, ct);

        // ── الرحلات ──
        var tripsToday = await _db.Trips.CountAsync(
            t => !t.IsDeleted && OpenTripStatuses.Contains(t.Status)
                 && t.PlannedStartAt >= DateTime.Today
                 && t.PlannedStartAt < DateTime.Today.AddDays(1), ct);

        var openTrips = await _db.Trips.CountAsync(
            t => !t.IsDeleted && OpenTripStatuses.Contains(t.Status), ct);

        var delayedTrips = await _db.Trips.CountAsync(
            t => !t.IsDeleted && OpenTripStatuses.Contains(t.Status)
                 && t.PlannedStartAt < DateTime.Now, ct);

        // ── العهد ──
        var openCustodies = await _db.DriverCustodies.CountAsync(
            c => !c.IsDeleted && OpenCustodyStatuses.Contains(c.Status), ct);

        var custodyOutstanding = await _db.DriverCustodies
            .Where(c => !c.IsDeleted && OpenCustodyStatuses.Contains(c.Status))
            .SumAsync(c => (decimal?)(c.AmountIssued - c.AmountSpent - c.AmountReturned
                                      + c.AdditionalDue), ct) ?? 0m;

        // ── الفواتير والتحصيل ──
        var invoicesIssued = await _db.Invoices.CountAsync(
            i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled", ct);

        var receivables = await _db.Invoices
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled")
            .SumAsync(i => (decimal?)i.GrandTotal - i.PaidAmount, ct) ?? 0m;

        var overdueCount = await _db.Invoices.CountAsync(
            i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                 && i.DueDate != null && i.DueDate < today
                 && i.GrandTotal - i.PaidAmount > 0, ct);

        var collectedThisMonth = await _db.Payments
            .Where(p => !p.IsDeleted && p.Status == "Posted" && p.PaymentDate >= monthStart)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var revenueThisMonth = await _db.Invoices
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.InvoiceDate >= monthStart)
            .SumAsync(i => (decimal?)i.GrandTotal, ct) ?? 0m;

        // ── الخزينة ──
        var cashBalance = await BalanceAsync(ct);

        // ── الأسطول ──
        var activeDrivers = await _db.Drivers.CountAsync(d => !d.IsDeleted && d.Status == "Active", ct);
        var activeVehicles = await _db.Vehicles.CountAsync(v => !v.IsDeleted && v.Status == "Available", ct);
        var vehiclesInTrip = await _db.Vehicles.CountAsync(v => !v.IsDeleted && v.Status == "InTrip", ct);

        return Ok(new SummaryOut(
            openOps, delayedOps, closedThisMonth,
            openTrips, tripsToday, delayedTrips,
            openCustodies, custodyOutstanding,
            invoicesIssued, revenueThisMonth, collectedThisMonth, receivables, overdueCount,
            cashBalance, activeDrivers, activeVehicles, vehiclesInTrip));
    }

    /// <summary>التنبيهات — عمليات متأخرة · فواتير مستحقة · عهد · رخص بتخلص.</summary>
    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts([FromQuery] int take = 5, CancellationToken ct = default)
    {
        if (take is < 1 or > 20) take = 5;
        var today   = DateOnly.FromDateTime(DateTime.Today);
        var todayDt = DateTime.Today;      // Operation.PlannedDate نوعها DateTime?
        var soon    = today.AddDays(30);
        var list    = new List<AlertItem>();

        // 🔴 مافيش DateOnly.ToString() ولا تركيب نصوص جوه LINQ-to-Entities —
        //    بنجيب الصفوف الأول (AsNoTracking + Take) ونبني الرسالة في الذاكرة.

        // عمليات متأخرة
        var ops = await _db.Operations.AsNoTracking()
            .Where(o => !o.IsDeleted && OpenOpStatuses.Contains(o.Status) && o.PlannedDate < todayDt)
            .OrderBy(o => o.PlannedDate).Take(take)
            .Select(o => new { o.OperationNumber, o.PlannedDate })
            .ToListAsync(ct);
        foreach (var o in ops)
        {
            // PlannedDate نوعها DateTime? — الـ Where ضمن إنها مش null،
            // بس Roslyn مابيعرفش، و DateOnly.FromDateTime بتاخد DateTime.
            if (o.PlannedDate is null) continue;
            var d = DateOnly.FromDateTime(o.PlannedDate.Value);
            list.Add(new AlertItem("عملية متأخرة", o.OperationNumber, d.ToString(),
                "المتأخرة " + DaysBetween(d, today) + " يوم",
                DaysBetween(d, today), "warn", "/operations"));
        }

        // فواتير فات ميعادها
        var inv = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.DueDate != null && i.DueDate < today
                        && i.GrandTotal - i.PaidAmount > 0)
            .OrderBy(i => i.DueDate).Take(take)
            .Select(i => new { i.InvoiceNumber, i.DueDate, Due = i.GrandTotal - i.PaidAmount })
            .ToListAsync(ct);
        foreach (var i in inv)
            list.Add(new AlertItem("فاتورة فات ميعادها", i.InvoiceNumber, i.DueDate!.Value.ToString(),
                "متبقي " + i.Due.ToString("N0"),
                DaysBetween(i.DueDate.Value, today), "bad", "/invoices"));

        // عهد مفتوحة من فترة
        var cus = await _db.DriverCustodies.AsNoTracking()
            .Where(c => !c.IsDeleted && OpenCustodyStatuses.Contains(c.Status))
            .OrderBy(c => c.CustodyDate).Take(take)
            .Select(c => new { c.CustodyNumber, c.CustodyDate })
            .ToListAsync(ct);
        foreach (var c in cus)
        {
            var d = DateOnly.FromDateTime(c.CustodyDate);
            list.Add(new AlertItem("عهدة مفتوحة", c.CustodyNumber, d.ToString(),
                "مفتوحة من " + DaysBetween(d, today) + " يوم",
                DaysBetween(d, today), "info", "/custodies"));
        }

        // رخص السواقين اللي بتخلص
        var drv = await _db.Drivers.AsNoTracking()
            .Where(d => !d.IsDeleted && d.LicenseExpiryDate != null && d.LicenseExpiryDate <= soon)
            .OrderBy(d => d.LicenseExpiryDate).Take(take)
            .Select(d => new { d.FullName, d.LicenseExpiryDate })
            .ToListAsync(ct);
        foreach (var d in drv)
        {
            var done = d.LicenseExpiryDate < today;
            list.Add(new AlertItem("رخصة سائق", d.FullName, d.LicenseExpiryDate!.Value.ToString(),
                done ? "الرخصة خلصت" : "بتخلص قريب",
                done ? DaysBetween(d.LicenseExpiryDate.Value, today) : 0,
                done ? "bad" : "warn", "/drivers"));
        }

        // تأمين العربيات
        var veh = await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted && v.InsuranceExpiryDate != null && v.InsuranceExpiryDate <= soon)
            .OrderBy(v => v.InsuranceExpiryDate).Take(take)
            .Select(v => new { v.PlateNumber, v.InsuranceExpiryDate })
            .ToListAsync(ct);
        foreach (var v in veh)
        {
            var done = v.InsuranceExpiryDate < today;
            list.Add(new AlertItem("تأمين سيارة", v.PlateNumber, v.InsuranceExpiryDate!.Value.ToString(),
                done ? "التأمين خلص" : "بيخلص قريب",
                done ? DaysBetween(v.InsuranceExpiryDate.Value, today) : 0,
                done ? "bad" : "warn", "/vehicles"));
        }

        var order = new Dictionary<string, int> { ["bad"] = 0, ["warn"] = 1, ["info"] = 2 };
        return Ok(list.OrderBy(a => order.TryGetValue(a.Severity, out var k) ? k : 3)
                      .ThenByDescending(a => a.Days).Take(take * 4).ToList());
    }

    /// <summary>حركة الفلوس لآخر 6 شهور: محصّل · مصروفات · إيراد مفوتر.</summary>
    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-5);
        var startDt = start.ToDateTime(TimeOnly.MinValue);

        var rows = new List<MonthFlow>();
        for (var i = 0; i < 6; i++)
        {
            var mStart = start.AddMonths(i);
            rows.Add(new MonthFlow(mStart.ToString("yyyy-MM"), mStart.ToString("MMMM"), 0m, 0m, 0m));
        }

        // التحصيل
        var pays = await _db.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == "Posted" && p.PaymentDate >= start)
            .Select(p => new { p.PaymentDate, p.Amount })
            .ToListAsync(ct);

        // المصروفات
        var exps = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Status == "Posted" && e.ExpenseDate >= startDt)
            .Select(e => new { e.ExpenseDate, e.Amount })
            .ToListAsync(ct);

        // الإيراد المفوتر
        var invs = await _db.Invoices.AsNoTracking()
            .Where(n => !n.IsDeleted && n.Status != "Draft" && n.Status != "Cancelled"
                        && n.InvoiceDate >= start)
            .Select(n => new { n.InvoiceDate, n.GrandTotal })
            .ToListAsync(ct);

        for (var i = 0; i < rows.Count; i++)
        {
            var mStart = start.AddMonths(i);
            var mEnd   = mStart.AddMonths(1);
            rows[i] = rows[i] with
            {
                Collected = pays.Where(p => p.PaymentDate >= mStart && p.PaymentDate < mEnd)
                                .Sum(p => p.Amount),
                Expenses  = exps.Where(e => DateOnly.FromDateTime(e.ExpenseDate) >= mStart
                                            && DateOnly.FromDateTime(e.ExpenseDate) < mEnd)
                                .Sum(e => e.Amount),
                Invoiced  = invs.Where(n => n.InvoiceDate >= mStart && n.InvoiceDate < mEnd)
                                .Sum(n => n.GrandTotal)
            };
        }
        return Ok(rows);
    }

    /// <summary>أعلى 5 عملاء بالإيراد — من بنود الفواتير.</summary>
    [HttpGet("top-customers")]
    public async Task<IActionResult> TopCustomers([FromQuery] int take = 5, CancellationToken ct = default)
    {
        if (take is < 1 or > 20) take = 5;
        var from = DateOnly.FromDateTime(DateTime.Today).AddMonths(-3);

        // 🔴🔴 GroupBy + aggregates + projection في EF Core 8 **مش مضمون الترجمة**
        //    (طلع InvalidOperationException فعلًا على الجهاز). الحل المضمون:
        //    نجيب الأعمدة اللي محتاجينها بس من SQL، والتجميع كله في الذاكرة.
        //    الفترة 3 شهور وعدد الفواتير محدود — مافيش مشكلة أداء.
        var raw = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.InvoiceDate >= from)
            .Select(i => new { i.CustomerId, i.Customer!.NameAr, i.GrandTotal, i.PaidAmount })
            .ToListAsync(ct);

        var rows = raw
            .GroupBy(x => new { x.CustomerId, x.NameAr })
            .Select(g => new TopCustomer(
                g.Key.CustomerId, g.Key.NameAr,
                g.Sum(x => x.GrandTotal),
                g.Sum(x => x.PaidAmount),
                g.Count()))
            .OrderByDescending(x => x.Invoiced)
            .Take(take)
            .ToList();

        return Ok(rows);
    }

    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// رصيد الخزينة الإجمالي.
    /// 🔴 بنقرأ <c>CashBoxes.CurrentBalance</c> جاهزة — <c>trg_CashTransactions_BalanceSync</c>
    /// هو اللي بيحسبها (<c>OpeningBalance + Σ</c> بالإشارات). إعادة الحساب في الكود
    /// معناها مكانين بيحسبوا نفس الرقم وممكن يختلفوا.
    /// </summary>
    private async Task<decimal> BalanceAsync(CancellationToken ct) =>
        await _db.CashBoxes.Where(b => !b.IsDeleted)
            .SumAsync(b => (decimal?)b.CurrentBalance, ct) ?? 0m;

    private static int DaysBetween(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber;

    // ══════════════════════════════════════════════════════════════════
    //  DTOs
    // ══════════════════════════════════════════════════════════════════

    public record SummaryOut(
        int OpenOperations, int DelayedOperations, int ClosedThisMonth,
        int OpenTrips, int TripsToday, int DelayedTrips,
        int OpenCustodies, decimal CustodyOutstanding,
        int InvoicesIssued, decimal RevenueThisMonth, decimal CollectedThisMonth,
        decimal Receivables, int OverdueCount,
        decimal CashBalance,
        int ActiveDrivers, int ActiveVehicles, int VehiclesInTrip);

    public record AlertItem(string Kind, string Ref, string Date, string Detail,
                            int Days, string Severity, string Route);

    public record MonthFlow(string Month, string MonthName,
                            decimal Collected, decimal Expenses, decimal Invoiced);

    public record TopCustomer(int CustomerId, string CustomerName,
                              decimal Invoiced, decimal Paid, int Invoices);
}
