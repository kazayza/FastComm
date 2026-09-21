using System.Collections.Concurrent;
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
///
/// <para>🚀 V4: إضافة Trends (مقارنة شهرية) · توزيع حالات العمليات · حالة الأسطول ·
/// آخر الأنشطة (من AuditLogs) · أعمار الديون — + Cache خفيف (45 ثانية)
/// عشان القاعدة البعيدة ماتتضربش مع كل فتح للصفحة.</para>
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly FastComDbContext _db;

    public DashboardController(FastComDbContext db) => _db = db;

    // ── 🚀 Cache خفيف في الذاكرة — بيمنع تكرار نفس الاستعلامات خلال ثواني قليلة ──
    private static readonly ConcurrentDictionary<string, (DateTime At, object Payload)> Cache = new();
    private const int CacheSeconds = 45;

    private static bool TryHit(string key, out object payload)
    {
        payload = null!;
        if (Cache.TryGetValue(key, out var hit) && (DateTime.UtcNow - hit.At).TotalSeconds < CacheSeconds)
        {
            payload = hit.Payload;
            return true;
        }
        return false;
    }

    private static void Put(string key, object payload) => Cache[key] = (DateTime.UtcNow, payload);

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

    /// <summary>حركات مش مهمة لسجل «آخر الأنشطة» على الرئيسية.</summary>
    private static readonly string[] SilentActions = { "Login", "Logout", "LoginFailed" };

    // ══════════════════════════════════════════════════════════════════

    /// <summary>كل أرقام الصفحة الرئيسية في طلب واحد.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        if (TryHit("summary", out var hitS)) return Ok(hitS);

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

        var payloadS = new SummaryOut(
            openOps, delayedOps, closedThisMonth,
            openTrips, tripsToday, delayedTrips,
            openCustodies, custodyOutstanding,
            invoicesIssued, revenueThisMonth, collectedThisMonth, receivables, overdueCount,
            cashBalance, activeDrivers, activeVehicles, vehiclesInTrip);
        Put("summary", payloadS);
        return Ok(payloadS);
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
        if (TryHit("monthly", out var hitM)) return Ok(hitM);

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
        Put("monthly", rows);
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
    //  🚀 V4 — Endpoints جديدة
    // ══════════════════════════════════════════════════════════════════

    /// <summary>مقارنة الشهر الحالي بالشهر الماضي — مفوتر · محصّل · مصروفات · عمليات مغلقة.</summary>
    [HttpGet("trends")]
    public async Task<IActionResult> Trends(CancellationToken ct)
    {
        if (TryHit("trends", out var hitT)) return Ok(hitT);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var cur   = new DateOnly(today.Year, today.Month, 1);
        var prev  = cur.AddMonths(-1);
        var next  = cur.AddMonths(1);
        var curStartDt  = cur.ToDateTime(TimeOnly.MinValue);
        var prevStartDt = prev.ToDateTime(TimeOnly.MinValue);
        var nextStartDt = next.ToDateTime(TimeOnly.MinValue);

        // مفوتر — الفواتير (InvoiceDate نوعها DateOnly)
        var invs = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.InvoiceDate >= prev)
            .Select(i => new { i.InvoiceDate, i.GrandTotal })
            .ToListAsync(ct);
        var invCur  = invs.Where(i => i.InvoiceDate >= cur).Sum(i => i.GrandTotal);
        var invPrev = invs.Where(i => i.InvoiceDate < cur).Sum(i => i.GrandTotal);

        // محصّل — المدفوعات (PaymentDate نوعها DateOnly)
        var pays = await _db.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == "Posted" && p.PaymentDate >= prev)
            .Select(p => new { p.PaymentDate, p.Amount })
            .ToListAsync(ct);
        var colCur  = pays.Where(p => p.PaymentDate >= cur).Sum(p => p.Amount);
        var colPrev = pays.Where(p => p.PaymentDate < cur).Sum(p => p.Amount);

        // مصروفات (ExpenseDate نوعها DateTime — زي الـ Monthly)
        var exps = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Status == "Posted" && e.ExpenseDate >= prevStartDt)
            .Select(e => new { e.ExpenseDate, e.Amount })
            .ToListAsync(ct);
        var expCur  = exps.Where(e => e.ExpenseDate >= curStartDt).Sum(e => e.Amount);
        var expPrev = exps.Where(e => e.ExpenseDate < curStartDt).Sum(e => e.Amount);

        // عمليات مغلقة فعليًا (ActualDeliveryAt نوعها DateTime?)
        var closedCur = await _db.Operations.CountAsync(
            o => !o.IsDeleted && o.Status == "Closed"
                 && o.ActualDeliveryAt != null
                 && o.ActualDeliveryAt >= curStartDt && o.ActualDeliveryAt < nextStartDt, ct);
        var closedPrev = await _db.Operations.CountAsync(
            o => !o.IsDeleted && o.Status == "Closed"
                 && o.ActualDeliveryAt != null
                 && o.ActualDeliveryAt >= prevStartDt && o.ActualDeliveryAt < curStartDt, ct);

        var payloadT = new TrendsOut(
            new TrendPoint(invCur, invPrev, PctChg(invCur, invPrev)),
            new TrendPoint(colCur, colPrev, PctChg(colCur, colPrev)),
            new TrendPoint(expCur, expPrev, PctChg(expCur, expPrev)),
            new TrendPoint(closedCur, closedPrev, PctChg(closedCur, closedPrev)));
        Put("trends", payloadT);
        return Ok(payloadT);
    }

    /// <summary>توزيع حالات العمليات — للـ Donut. (تجميع في الذاكرة — نفس نهج top-customers)</summary>
    [HttpGet("operation-status-breakdown")]
    public async Task<IActionResult> OperationStatusBreakdown(CancellationToken ct)
    {
        if (TryHit("ops-breakdown", out var hitB)) return Ok(hitB);
        var raw = await _db.Operations.AsNoTracking()
            .Where(o => !o.IsDeleted)
            .Select(o => o.Status)
            .ToListAsync(ct);
        var payloadB = raw.GroupBy(s => s)
            .Select(g => new StatusCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();
        Put("ops-breakdown", payloadB);
        return Ok(payloadB);
    }

    /// <summary>حالة الأسطول — للـ Gauge. (تجميع في الذاكرة)</summary>
    [HttpGet("fleet-status")]
    public async Task<IActionResult> FleetStatus(CancellationToken ct)
    {
        if (TryHit("fleet", out var hitF)) return Ok(hitF);
        var raw = await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted)
            .Select(v => v.Status)
            .ToListAsync(ct);
        var payloadF = raw.GroupBy(s => s)
            .Select(g => new StatusCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();
        Put("fleet", payloadF);
        return Ok(payloadF);
    }

    /// <summary>آخر الأنشطة — من AuditLogs (من غير Login/Logout) + اسم المستخدم + وقت محلي.</summary>
    [HttpGet("recent-activity")]
    public async Task<IActionResult> RecentActivity([FromQuery] int take = 10, CancellationToken ct = default)
    {
        if (take is < 1 or > 30) take = 10;
        if (TryHit("activity", out var hitA)) return Ok(hitA);

        var logs = await _db.AuditLogs.AsNoTracking()
            .Where(l => !SilentActions.Contains(l.Action))
            .OrderByDescending(l => l.AuditLogId)
            .Take(take)
            .Select(l => new { l.UserId, l.Action, l.EntityType, l.EntityId, l.Description, l.CreatedAt })
            .ToListAsync(ct);

        // 🔴🔴 قاموس أسماء المستخدمين — نفس أسلوب AuditController:
        //    الـ join المباشر على AspNetUsers طلّع «النظام» لكل الصفوف على
        //    الداتابيز الحقيقية (اختبرناه)، والقاموس مضمون وأخفّ على الاستعلام.
        var ids = logs.Where(l => l.UserId is not null)
                      .Select(l => l.UserId!.Value)
                      .Distinct()
                      .ToList();

        var nameById = new Dictionary<int, string>();
        if (ids.Count > 0)
        {
            var us = await _db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.UserName })
                .ToListAsync(ct);

            /* 🔴 CS8620: UserName نوعه string? في IdentityUser */
            foreach (var u in us)
                nameById[u.Id] = string.IsNullOrWhiteSpace(u.FullName)
                    ? (u.UserName ?? "")
                    : u.FullName;
        }

        // 🔴 CreatedAt متخزنة UTC (DEFAULT SYSUTCDATETIME) — بنحوّلها لتوقيت مصر للعرض
        var payloadA = logs.Select(a => new ActivityItem(
                a.Action,
                a.EntityType,
                a.EntityId ?? "",
                a.Description ?? "",
                a.UserId is not null
                    && nameById.TryGetValue(a.UserId.Value, out var un)
                    && !string.IsNullOrWhiteSpace(un)
                        ? un
                        : "النظام",
                TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(a.CreatedAt, DateTimeKind.Utc), Tz)
                    .ToString("yyyy-MM-dd HH:mm")))
            .ToList();
        Put("activity", payloadA);
        return Ok(payloadA);
    }

    /// <summary>أعمار الديون — من تاريخ الاستحقاق (أو تاريخ الفاتورة لو مفيش استحقاق).</summary>
    [HttpGet("receivables-aging")]
    public async Task<IActionResult> ReceivablesAging(CancellationToken ct)
    {
        if (TryHit("aging", out var hitG)) return Ok(hitG);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var raw = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.GrandTotal - i.PaidAmount > 0)
            .Select(i => new { i.DueDate, i.InvoiceDate, Due = i.GrandTotal - i.PaidAmount })
            .ToListAsync(ct);

        var current = 0m; var d30 = 0m; var d60 = 0m; var d90 = 0m; var d90p = 0m;
        foreach (var i in raw)
        {
            var refDate = i.DueDate ?? i.InvoiceDate;   // مفيش استحقاق؟ بترجع لتاريخ الفاتورة
            var days = today.DayNumber - refDate.DayNumber;
            if (days <= 0)       current += i.Due;
            else if (days <= 30) d30  += i.Due;
            else if (days <= 60) d60  += i.Due;
            else if (days <= 90) d90  += i.Due;
            else                 d90p += i.Due;
        }

        var payloadG = new AgingOut(current, d30, d60, d90, d90p,
                                    current + d30 + d60 + d90 + d90p);
        Put("aging", payloadG);
        return Ok(payloadG);
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

    /// <summary>نسبة التغيير % — لو القديم صفر والجديد فيه حاجة = 100%، لو الاتنين صفر = null.</summary>
    private static decimal? PctChg(decimal cur, decimal prev)
        => prev <= 0 ? (cur > 0 ? 100m : null) : Math.Round((cur - prev) / prev * 100m, 1);

    /// <summary>توقيت مصر — CreatedAt متخزنة UTC والعرض محلي.</summary>
    private static readonly TimeZoneInfo Tz = GetTz();
    private static TimeZoneInfo GetTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch { return TimeZoneInfo.Utc; }
    }

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

    // ── 🚀 V4 ──

    public record TrendPoint(decimal Current, decimal Previous, decimal? ChangePct);

    public record TrendsOut(TrendPoint Invoiced, TrendPoint Collected,
                            TrendPoint Expenses, TrendPoint ClosedOps);

    public record StatusCount(string Status, int Count);

    public record ActivityItem(string Action, string EntityType, string EntityId,
                               string Description, string UserName, string At);

    public record AgingOut(decimal Current, decimal D30, decimal D60, decimal D90,
                           decimal D90Plus, decimal Total);
}

