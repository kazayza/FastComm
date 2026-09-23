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
        export     = await Can("REPORT.EXPORT", ct),
        suppliers  = await Can("SUPPLIER.VIEW", ct)
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
            .Select(e => new { e.ExpenseTypeId, e.ExpenseDate, e.Amount, e.IsApproved, e.VehicleId })
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
                g.Where(x => x.IsApproved).Sum(x => x.Amount),
                g.Where(x => x.VehicleId.HasValue).Sum(x => x.Amount)))
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

    /// <summary>
    /// 📊 <c>GET api/reports/pnl?from=&amp;to=</c> — **قائمة الدخل**.
    ///
    /// 🔴 الإيراد = <c>SubTotal − DiscountTotal</c> (صافي قبل الضريبة) —
    /// **مش <c>GrandTotal</c>** لأنه شامل الضريبة، والضريبة فلوس الحكومة مش إيراد.
    ///
    /// 🔴 التكلفة المباشرة = المصروفات اللي نوعها <c>IsOperationCost = 1</c>
    /// (وقود · طرق · ميناء · انتظار · تحميل · تفريغ · أوناش · وجبات · إصلاح · غرامات · أخرى)
    /// والباقي (<c>IsOperationCost = 0</c>) = مصروفات تشغيلية/إدارية.
    ///
    /// 🔐 بصلاحية <c>REPORT.FINANCIAL</c> الموجودة — مافيش كود صلاحية جديد.
    /// </summary>
    [HttpGet("pnl")]
    public async Task<IActionResult> Pnl(string? from, string? to, CancellationToken ct)
    {
        if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();

        var (f, t, fs, ts) = Range(from, to);
        var (summary, months, costRows) = await BuildPnlAsync(f, t, ct);

        return Ok(new
        {
            range   = new { from = fs, to = ts },
            summary,
            months,
            costRows
        });
    }

    /// <summary>
    /// بيبني قائمة الدخل — مشترك بين الـ endpoint وتصدير Excel.
    /// </summary>
    private async Task<(PnlSummary Summary, List<PnlMonth> Months, List<PnlCostRow> CostRows)>
        BuildPnlAsync(DateTime f, DateTime t, CancellationToken ct)
    {
        // ── الإيرادات: فواتير معتمدة (مش مسودة ولا ملغاة) ──
        var inv = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.InvoiceDate >= DateOnly.FromDateTime(f)
                        && i.InvoiceDate <= DateOnly.FromDateTime(t))
            .Select(i => new { i.InvoiceDate, i.SubTotal, i.DiscountTotal, i.TaxTotal })
            .ToListAsync(ct);

        // ── المصروفات + نوعها (IsOperationCost بيحدد مباشر/إداري) ──
        var exp = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Status != "Cancelled"
                        && e.ExpenseDate >= f && e.ExpenseDate <= t)
            .Select(e => new { e.ExpenseDate, e.Amount, e.ExpenseType.IsOperationCost })
            .ToListAsync(ct);

        // ── أسماء أنواع المصروفات (للتفصيل) ──
        var typeId = await _db.ExpenseTypes.AsNoTracking()
            .Select(x => new { x.ExpenseTypeId, x.NameAr, x.IsOperationCost })
            .ToListAsync(ct);
        var typeName = typeId.ToDictionary(x => x.ExpenseTypeId, x => x.NameAr);

        // ── تفصيل التكلفة المباشرة حسب النوع ──
        var expDetail = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Status != "Cancelled"
                        && e.ExpenseDate >= f && e.ExpenseDate <= t
                        && e.ExpenseType.IsOperationCost)
            .Select(e => new { e.ExpenseTypeId, e.Amount })
            .ToListAsync(ct);

        var costRows = expDetail
            .GroupBy(x => x.ExpenseTypeId)
            .Select(g => new PnlCostRow(
                typeName.TryGetValue(g.Key, out var n) ? n : "—",
                g.Count(), g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        // ── تجميع شهري في الذاكرة (مافيش GroupBy في SQL — §EF translation) ──
        string Mn(int y, int m) => new DateTime(y, m, 1).ToString("MMMM yyyy", new CultureInfo("ar-EG"));

        var months = inv.Select(x => (x.InvoiceDate.Year, x.InvoiceDate.Month,
                                      Rev: x.SubTotal - x.DiscountTotal,
                                      Tax: x.TaxTotal, Cost: 0m, Opex: 0m))
            .Concat(exp.Select(x => (DateOnly.FromDateTime(x.ExpenseDate).Year,
                                     DateOnly.FromDateTime(x.ExpenseDate).Month,
                                     Rev: 0m, Tax: 0m,
                                     Cost: x.IsOperationCost ? x.Amount : 0m,
                                     Opex: x.IsOperationCost ? 0m : x.Amount)))
            .GroupBy(x => (x.Year, x.Month))
            .Select(g => new PnlMonth(
                $"{g.Key.Year}-{g.Key.Month:D2}", Mn(g.Key.Year, g.Key.Month),
                g.Sum(x => x.Rev), g.Sum(x => x.Tax), g.Sum(x => x.Cost), g.Sum(x => x.Opex)))
            .OrderBy(x => x.Month, StringComparer.Ordinal)
            .ToList();

        // ── الإجمالي ──
        var revenue = inv.Sum(x => x.SubTotal - x.DiscountTotal);
        var tax     = inv.Sum(x => x.TaxTotal);
        var cost    = exp.Where(x => x.IsOperationCost).Sum(x => x.Amount);
        var opex    = exp.Where(x => !x.IsOperationCost).Sum(x => x.Amount);

        var gross = revenue - cost;
        var net   = gross - opex;

        var summary = new PnlSummary(
            revenue, tax, cost, gross, opex, net,
            revenue != 0 ? Math.Round(gross / revenue * 100m, 2) : 0m,
            revenue != 0 ? Math.Round(net / revenue * 100m, 2) : 0m,
            inv.Count, exp.Count);

        return (summary, months, costRows);
    }

    /// <summary>
    /// ⏰ <c>GET api/reports/overdue</c> — **فواتير عدى ميعاد سدادها**.
    ///
    /// <para>
    /// 🔴 الفلتر: <c>DueDate &lt; النهاردة</c> و<c>PaymentStatus</c> مش <c>Paid</c>.
    /// <c>DaysOverdue</c> و<c>Balance</c> بيتحسبوا **في الذاكرة** —
    /// <c>DateOnly</c> ما بيتترجمش لطرح في SQL (§EF translation).
    /// </para>
    /// </summary>
    [HttpGet("overdue")]
    public async Task<IActionResult> Overdue(CancellationToken ct)
    {
        if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();

        var rows = await BuildOverdueAsync(ct);
        return Ok(new
        {
            asOf   = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            total  = rows.Sum(x => x.Balance),
            count  = rows.Count,
            rows
        });
    }

    private async Task<List<OverdueRow>> BuildOverdueAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        /* 🔴 <c>Overpaid</c> خارج — العميل دفع زيادة فمافيش متأخرات */
        var inv = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted
                        && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.DueDate != null && i.DueDate < today
                        && i.PaymentStatus != "Paid" && i.PaymentStatus != "Overpaid")
            .Select(i => new
            {
                i.InvoiceNumber, i.CustomerId, i.InvoiceDate, i.DueDate,
                i.GrandTotal, i.PaidAmount, i.PaymentStatus
            })
            .ToListAsync(ct);

        var ids = inv.Select(x => x.CustomerId).Distinct().ToList();
        var names = ids.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Customers.AsNoTracking()
                    .Where(c => ids.Contains(c.CustomerId))
                    .Select(c => new { c.CustomerId, c.NameAr }).ToListAsync(ct))
                .ToDictionary(x => x.CustomerId, x => x.NameAr);

        return inv
            .Select(x => new OverdueRow(
                x.InvoiceNumber,
                names.TryGetValue(x.CustomerId, out var n) ? n : "—",
                x.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                x.DueDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                today.DayNumber - x.DueDate.Value.DayNumber,
                x.GrandTotal, x.PaidAmount, x.GrandTotal - x.PaidAmount,
                x.PaymentStatus))
            .OrderByDescending(x => x.DaysOverdue)
            .ToList();
    }

    /// <summary>
    /// 📈 <c>GET api/reports/compare?from=&amp;to=</c> — **مقارنة الفترة الحالية بالسابقة**.
    /// الفترة السابقة = نفس عدد الأيام اللي قبل <c>from</c> على طول.
    /// </summary>
    [HttpGet("compare")]
    public async Task<IActionResult> Compare(string? from, string? to, CancellationToken ct)
    {
        if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();

        var (f, t, fs, ts) = Range(from, to);
        var rows = await BuildCompareAsync(f, t, ct);

        var days = (int)(t.Date - f.Date).TotalDays + 1;
        return Ok(new
        {
            current  = new { from = fs, to = ts, days },
            previous = new
            {
                from = f.AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to   = f.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                days
            },
            rows
        });
    }

    private async Task<List<CompareRow>> BuildCompareAsync(DateTime f, DateTime t, CancellationToken ct)
    {
        /* الفترة الحالية = [f, t] شاملة الطرفين (f من أول اليوم، t لآخر اليوم)
              ⇒ السابقة = [f.AddDays(-days), f.AddSeconds(-1)] — نفس الطول بالظبط.

              🔴 `AddSeconds(-1)` مش `AddTicks(-1)`: المصروف اللي بالظبط
              `f.AddDays(-1) 00:00:00` كان هيتحسب في الفترتين مع Ticks. */
        var days = (int)(t.Date - f.Date).TotalDays + 1;
        var pf   = f.AddDays(-days);
        var pt   = f.AddSeconds(-1);

        var (cur, _, _)  = await BuildPnlAsync(f,  t,  ct);
        var (prev, _, _) = await BuildPnlAsync(pf, pt, ct);

        static decimal Pct(decimal c, decimal p) =>
            p == 0 ? (c == 0 ? 0m : 100m) : Math.Round((c - p) / Math.Abs(p) * 100m, 2);

        return new List<CompareRow>
        {
            new("الإيرادات",              cur.Revenue,            prev.Revenue,
                cur.Revenue - prev.Revenue,                       Pct(cur.Revenue, prev.Revenue)),
            new("تكلفة التشغيل",           cur.OperationCost,      prev.OperationCost,
                cur.OperationCost - prev.OperationCost,           Pct(cur.OperationCost, prev.OperationCost)),
            new("مجمل الربح",              cur.GrossProfit,        prev.GrossProfit,
                cur.GrossProfit - prev.GrossProfit,               Pct(cur.GrossProfit, prev.GrossProfit)),
            new("المصروفات التشغيلية",     cur.OperatingExpenses,  prev.OperatingExpenses,
                cur.OperatingExpenses - prev.OperatingExpenses,   Pct(cur.OperatingExpenses, prev.OperatingExpenses)),
            new("صافي الربح",              cur.NetProfit,          prev.NetProfit,
                cur.NetProfit - prev.NetProfit,                   Pct(cur.NetProfit, prev.NetProfit)),
            new("هامش صافي الربح %",       cur.NetMargin,          prev.NetMargin,
                cur.NetMargin - prev.NetMargin,                   Pct(cur.NetMargin, prev.NetMargin)),
            new("عدد الفواتير",            cur.InvoiceCount,       prev.InvoiceCount,
                cur.InvoiceCount - prev.InvoiceCount,             Pct(cur.InvoiceCount, prev.InvoiceCount)),
        };
    }

    /// <summary>
    /// 🚢 <c>GET api/reports/ports</c> — **ربحية الموانئ والوجهات**.
    ///
    /// <para>
    /// 🔴 الإيراد = <c>Operation.RevenueNet</c> · التكلفة = مصروفات العملية المباشرة
    /// + نصيبها من تكلفة الرحلات (<c>TripCostAllocations</c>) — نفس منطق تقرير العمليات.
    /// </para>
    /// </summary>
    [HttpGet("ports")]
    public async Task<IActionResult> Ports(string? from, string? to, CancellationToken ct)
    {
        if (!await Can("REPORT.OPERATIONS", ct)) return Forbid();

        var (f, t, fs, ts) = Range(from, to);
        var ops = await LoadOpProfitAsync(f, t, ct);

        // ── الموانئ ──
        var portIds = ops.Where(x => x.PortId is not null).Select(x => x.PortId!.Value).Distinct().ToList();
        var portNames = portIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Ports.AsNoTracking().Where(x => portIds.Contains(x.PortId))
                    .Select(x => new { x.PortId, x.NameAr }).ToListAsync(ct))
                .ToDictionary(x => x.PortId, x => x.NameAr);

        var ports = ops.Where(x => x.PortId is not null)
            .GroupBy(x => x.PortId!.Value)
            .Select(g => GroupRow(portNames.TryGetValue(g.Key, out var n) ? n : "—", g))
            .OrderByDescending(x => x.Profit)
            .ToList();

        // ── الوجهات ──
        var dstIds = ops.Where(x => x.DestinationId is not null).Select(x => x.DestinationId!.Value).Distinct().ToList();
        var dstNames = dstIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Destinations.AsNoTracking().Where(x => dstIds.Contains(x.DestinationId))
                    .Select(x => new { x.DestinationId, x.NameAr }).ToListAsync(ct))
                .ToDictionary(x => x.DestinationId, x => x.NameAr);

        var dests = ops.Where(x => x.DestinationId is not null)
            .GroupBy(x => x.DestinationId!.Value)
            .Select(g => GroupRow(dstNames.TryGetValue(g.Key, out var n) ? n : "—", g))
            .OrderByDescending(x => x.Profit)
            .ToList();

        // ── التعتيق ──
        var thIds = ops.Where(x => x.TahteeqPortId is not null).Select(x => x.TahteeqPortId!.Value).Distinct().ToList();
        var thNames = thIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Ports.AsNoTracking().Where(x => thIds.Contains(x.PortId))
                    .Select(x => new { x.PortId, x.NameAr }).ToListAsync(ct))
                .ToDictionary(x => x.PortId, x => x.NameAr);

        var tahteeqs = ops.Where(x => x.TahteeqPortId is not null)
            .GroupBy(x => x.TahteeqPortId!.Value)
            .Select(g => GroupRow(thNames.TryGetValue(g.Key, out var n) ? n : "—", g))
            .OrderByDescending(x => x.Profit)
            .ToList();

        var noPort = ops.Count(x => x.PortId is null);
        return Ok(new
        {
            range   = new { from = fs, to = ts },
            ports,
            destinations = dests,
            tahteeqs,
            noPort
        });
    }

    /// <summary>
    /// 🧾 <c>GET api/reports/services</c> — **ربحية الخدمات**.
    /// </summary>
    [HttpGet("services")]
    public async Task<IActionResult> Services(string? from, string? to, CancellationToken ct)
    {
        if (!await Can("REPORT.OPERATIONS", ct)) return Forbid();

        var (f, t, fs, ts) = Range(from, to);
        var ops = await LoadOpProfitAsync(f, t, ct);

        var ids = ops.Where(x => x.ServiceId is not null).Select(x => x.ServiceId!.Value).Distinct().ToList();
        var names = ids.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Services.AsNoTracking().Where(x => ids.Contains(x.ServiceId))
                    .Select(x => new { x.ServiceId, x.NameAr }).ToListAsync(ct))
                .ToDictionary(x => x.ServiceId, x => x.NameAr);

        var rows = ops.Where(x => x.ServiceId is not null)
            .GroupBy(x => x.ServiceId!.Value)
            .Select(g => GroupRow(names.TryGetValue(g.Key, out var n) ? n : "—", g))
            .OrderByDescending(x => x.Profit)
            .ToList();

        return Ok(new
        {
            range     = new { from = fs, to = ts },
            rows,
            noService = ops.Count(x => x.ServiceId is null)
        });
    }

    /// <summary>
    /// 💵 <c>GET api/reports/cashflow</c> — **تدفق نقدي متوقع**.
    ///
    /// <para>
    /// 🔴 من <c>Invoices.DueDate</c> — الفواتير اللي لسه ما اتحصّلتش كامل،
    /// مجمّعة بالأسبوع لـ 8 أسابيع جاية + «بعد كده» + «متأخرة».
    /// </para>
    /// </summary>
    [HttpGet("cashflow")]
    public async Task<IActionResult> Cashflow(CancellationToken ct)
    {
        if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();

        const int Weeks = 8;
        var buckets = await BuildCashflowAsync(ct);
        var open    = buckets.Sum(x => x.Expected);

        return Ok(new
        {
            asOf  = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            weeks = Weeks,
            total = open,
            count = buckets.Sum(x => x.Invoices),
            rows  = buckets
        });
    }

    private async Task<List<CashWeek>> BuildCashflowAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        const int Weeks = 8;

        var inv = await _db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted
                        && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.PaymentStatus != "Paid" && i.PaymentStatus != "Overpaid")
            .Select(i => new { i.DueDate, i.GrandTotal, i.PaidAmount })
            .ToListAsync(ct);

        var open = inv.Where(x => x.GrandTotal - x.PaidAmount > 0).ToList();

        var buckets = new List<CashWeek>();

        /* ⏰ متأخرة — ميعادها عدّى */
        var overdue = open.Where(x => x.DueDate is null || x.DueDate < today).ToList();
        buckets.Add(new CashWeek("overdue", "متأخرة", overdue.Sum(x => x.GrandTotal - x.PaidAmount), overdue.Count));

        /* 📅 8 أسابيع جاية */
        for (var w = 0; w < Weeks; w++)
        {
            var ws = today.AddDays(w * 7);
            var we = today.AddDays(w * 7 + 6);
            var inW = open.Where(x => x.DueDate is not null
                                      && x.DueDate >= ws && x.DueDate <= we).ToList();

            buckets.Add(new CashWeek(
                $"w{w + 1}",
                $"{ws.ToString("dd/MM", CultureInfo.InvariantCulture)} – {we.ToString("dd/MM", CultureInfo.InvariantCulture)}",
                inW.Sum(x => x.GrandTotal - x.PaidAmount), inW.Count));
        }

        /* 🔮 بعد الأسابيع دي */
        var limit = today.AddDays(Weeks * 7);
        var later = open.Where(x => x.DueDate is not null && x.DueDate > limit).ToList();
        buckets.Add(new CashWeek("later", "بعد كده", later.Sum(x => x.GrandTotal - x.PaidAmount), later.Count));

        return buckets;
    }

    /* ═══════════ مساعدات الربحية ═══════════ */

    /// <summary>عملية + إيرادها + تكلفتها — أساس تقارير الربحية.</summary>
    private sealed record OpProfit(long OperationId, int? PortId, int? DestinationId,
        int? TahteeqPortId, int? ServiceId, decimal Revenue, decimal Cost);

    private async Task<List<OpProfit>> LoadOpProfitAsync(DateTime f, DateTime t, CancellationToken ct)
    {
        var ops = await _db.Operations.AsNoTracking()
            .Where(o => !o.IsDeleted && o.CreatedAt >= f && o.CreatedAt <= t)
            .OrderByDescending(o => o.OperationId)
            .Take(MaxRows)
            .Select(o => new { o.OperationId, o.PortId, o.DestinationId, o.TahteeqPortId, o.ServiceId, o.RevenueNet })
            .ToListAsync(ct);

        if (ops.Count == 0) return new List<OpProfit>();

        var opIds = ops.Select(o => o.OperationId).ToList();

        /* 🔴 التكلفة = مصروفات العملية المباشرة + نصيبها من تكلفة الرحلات
              (مش `ActualCost` — عشان ما نحسبش نفس الحاجة مرتين) */
        var exp = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Status != "Cancelled"
                        && e.OperationId != null && opIds.Contains(e.OperationId.Value))
            .Select(e => new { OpId = e.OperationId!.Value, e.Amount }).ToListAsync(ct);

        var alloc = await _db.TripCostAllocations.AsNoTracking()
            .Where(a => opIds.Contains(a.OperationId))
            .Select(a => new { a.OperationId, a.AllocatedAmount }).ToListAsync(ct);

        var expByOp = exp.GroupBy(x => x.OpId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var alByOp  = alloc.GroupBy(x => x.OperationId).ToDictionary(g => g.Key, g => g.Sum(x => x.AllocatedAmount));

        return ops.Select(o => new OpProfit(
            o.OperationId, o.PortId, o.DestinationId, o.TahteeqPortId, o.ServiceId,
            o.RevenueNet,
            (expByOp.TryGetValue(o.OperationId, out var e) ? e : 0m) +
            (alByOp.TryGetValue(o.OperationId, out var a) ? a : 0m)))
            .ToList();
    }

    /// <summary>يحوّل مجموعات لصفوف Excel مرتبة من الأعلى ربحًا.</summary>
    private static IEnumerable<object[]> Rows(IEnumerable<IGrouping<int, OpProfit>> groups,
        Func<int, string> nameOf) =>
        groups.Select(g => GroupRow(nameOf(g.Key), g))
              .OrderByDescending(r => r.Profit)
              .Select(r => new object[]
                  { r.Name, r.Operations, r.Revenue, r.Cost, r.Profit, r.MarginPct });

    private static ProfitRow GroupRow(string name, IEnumerable<OpProfit> g)
    {
        var rev  = g.Sum(x => x.Revenue);
        var cost = g.Sum(x => x.Cost);
        var prof = rev - cost;
        return new ProfitRow(name, g.Count(), rev, cost, prof,
            rev != 0 ? Math.Round(prof / rev * 100m, 2) : 0m);
    }

    /// <summary>
    /// 📥 <c>GET api/reports/supplier-aging</c> — **أعمار ديون الموردين**.
    ///
    /// <para>
    /// الفواتير المفتوحة (مش مسودة ولا ملغاة ولا مدفوعة) مقسّمة:
    /// <c>0–30</c> · <c>31–60</c> · <c>61–90</c> · <c>+90</c> · <c>بدون استحقاق</c>.
    /// </para>
    ///
    /// 🔐 بصلاحية <c>SUPPLIER.VIEW</c> الموجودة — مافيش كود جديد.
    /// </summary>
    [HttpGet("supplier-aging")]
    public async Task<IActionResult> SupplierAging(CancellationToken ct)
    {
        if (!await Can("SUPPLIER.VIEW", ct)) return Forbid();

        var rows = await BuildSupplierAgingAsync(ct);
        return Ok(new
        {
            asOf  = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            total = rows.Sum(x => x.Balance),
            rows
        });
    }

    private async Task<List<AgingRow>> BuildSupplierAgingAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var inv = await _db.SupplierInvoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.PaymentStatus != "Paid")
            .Select(i => new { i.SupplierId, i.DueDate, i.GrandTotal, i.PaidAmount })
            .ToListAsync(ct);

        var ids = inv.Select(x => x.SupplierId).Distinct().ToList();
        var names = ids.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Suppliers.AsNoTracking()
                    .Where(s => ids.Contains(s.SupplierId))
                    .Select(s => new { s.SupplierId, s.NameAr }).ToListAsync(ct))
                .ToDictionary(x => x.SupplierId, x => x.NameAr);

        return inv
            .Where(x => x.GrandTotal - x.PaidAmount > 0)
            .GroupBy(x => x.SupplierId)
            .Select(g =>
            {
                /* 🔴 التقييم في الذاكرة — `DateOnly` ما بيتترجمش لطرح في SQL */
                var open = g.Select(x => (
                    Bal : x.GrandTotal - x.PaidAmount,
                    Days: x.DueDate is null ? (int?)null : today.DayNumber - x.DueDate.Value.DayNumber
                )).ToList();

                decimal Sum(Func<(decimal Bal, int? Days), bool> f) =>
                    open.Where(f).Sum(x => x.Bal);

                return new AgingRow(
                    names.TryGetValue(g.Key, out var n) ? n : "—",
                    open.Count,
                    Sum(x => x.Days is not null && x.Days <= 30),
                    Sum(x => x.Days is not null && x.Days > 30 && x.Days <= 60),
                    Sum(x => x.Days is not null && x.Days > 60 && x.Days <= 90),
                    Sum(x => x.Days is not null && x.Days > 90),
                    Sum(x => x.Days is null),
                    open.Sum(x => x.Bal));
            })
            .OrderByDescending(x => x.Balance)
            .ToList();
    }

    // ══════════════════════════════════════════════════════════════════
    //  10) 💵 التدفق النقدي الفعلي  (🆕 2026-09-13)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 📥 <c>GET api/reports/cashflow-actual?from=&amp;to=&amp;cashBoxId=</c> —
    /// **قائمة التدفقات النقدية الفعلية** من <c>CashTransactions</c>.
    ///
    /// <para>🔴 ده **غير** <c>api/reports/cashflow</c> — ده الأخير توقّع مبني على
    /// <c>Invoices.DueDate</c>. هنا الحركة اللي حصلت فعلًا في الخزينة.</para>
    ///
    /// <para>🔴 <b>التحويلات بين الخزينتين</b> (<c>TransferIn</c>/<c>TransferOut</c>)
    /// <b>ما بتتحسبش</b> في الإجمالي — هي نقل فلوس من جيب لجيب مش دخل أو مصروف.
    /// بتظهر بس في جدول «الحركة حسب الخزينة».</para>
    ///
    /// <para>🔴 <c>Status == "Void"</c> مستثناة بالكامل — الحركة الملغية كأنها ما كانتش.</para>
    /// </summary>
    [HttpGet("cashflow-actual")]
    public async Task<IActionResult> CashflowActual(string? from, string? to,
        int? cashBoxId, CancellationToken ct)
    {
        if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();

        var (f, t, fs, ts) = Range(from, to);
        var res = await BuildCashflowActualAsync(f, t, fs, ts, cashBoxId, ct);
        return Ok(res);
    }

    /// <summary>بند في قسم (وارد أو صادر) من قائمة التدفقات.</summary>
    public record CashLine(string Label, int Count, decimal Amount, decimal Pct);

    /// <summary>عمود شهري في قائمة التدفقات.</summary>
    public record CashMonth(string Key, string MonthName, decimal Inflow, decimal Outflow, decimal Net);

    /// <summary>صف في جدول الحركة حسب الخزينة.</summary>
    public record CashBoxRow(string BoxName, decimal Inflow, decimal Outflow,
        decimal TransferIn, decimal TransferOut, decimal Net, decimal Closing);

    private async Task<object> BuildCashflowActualAsync(
        DateTime f, DateTime t, string fs, string ts, int? cashBoxId, CancellationToken ct)
    {
        /* 🔴 التجميع كله في الذاكرة — مافيش GroupBy داخل سلسلة EF (EF Core 8). */
        var tx = await _db.CashTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.Status == "Posted"
                        && x.TransactionDate >= f && x.TransactionDate <= t
                        && (cashBoxId == null || x.CashBoxId == cashBoxId))
            .Select(x => new
            {
                x.CashBoxId, x.TransactionType, x.Amount, x.PaymentMethodId,
                Day = (DateTime)x.TransactionDate, x.ReferenceNumber
            })
            .ToListAsync(ct);

        var methods = (await _db.PaymentMethods.AsNoTracking()
                .Select(x => new { x.PaymentMethodId, x.NameAr, x.IsCashBased }).ToListAsync(ct))
            .ToDictionary(x => x.PaymentMethodId, x => x);

        var boxes = (await _db.CashBoxes.AsNoTracking()
                .Select(x => new { x.CashBoxId, x.NameAr, x.OpeningBalance, x.CurrentBalance }).ToListAsync(ct))
            .ToDictionary(x => x.CashBoxId, x => x);

        /* ══════════ 1) الوارد والصادر — التحويلات خارج الحساب ══════════ */
        var inflow  = tx.Where(x => x.TransactionType == "Receipt").ToList();
        var outflow = tx.Where(x => x.TransactionType == "Payment").ToList();
        var totalIn  = inflow.Sum(x => x.Amount);
        var totalOut = outflow.Sum(x => x.Amount);

        /* 🔴 لازم نمرّر (المبلغ + الطريقة) مع بعض — لو مرّرنا `PaymentMethodId`
           لوحده، `g.Sum(x => x.Amount)` بيقع بـ CS1061 لأن العنصر `int?`. */
        List<CashLine> Lines(IEnumerable<(decimal Amt, int? Method)> src, string fallback)
        {
            var byKey = src
                .GroupBy(x => x.Method ?? -1)
                .Select(g => new { Key = g.Key, Count = g.Count(), Amt = g.Sum(x => x.Amt) })
                .OrderByDescending(x => x.Amt)
                .ToList();

            var grand = byKey.Sum(x => x.Amt);
            return byKey.Select(x => new CashLine(
                x.Key == -1
                    ? fallback
                    : methods.TryGetValue(x.Key, out var m) ? m.NameAr : $"طريقة #{x.Key}",
                x.Count,
                x.Amt,
                grand <= 0 ? 0 : Math.Round(x.Amt / grand * 100, 1))).ToList();
        }

        var inLines  = Lines(inflow.Select(x => (x.Amount, x.PaymentMethodId)),  "بدون طريقة محددة");
        var outLines = Lines(outflow.Select(x => (x.Amount, x.PaymentMethodId)), "بدون طريقة محددة");

        /* ══════════ 2) التصنيف حسب المصدر (البادئة في ReferenceNumber) ══════════ */
        /* ده اللي بيخلي التقرير يقول *إيه* اللي جاب الفلوس، مش بس كام. */

        var inSrc  = BySourceCore(inflow.Select(x => new RefAmt(x.Amount, x.ReferenceNumber)));
        var outSrc = BySourceCore(outflow.Select(x => new RefAmt(x.Amount, x.ReferenceNumber)));

        /* ══════════ 3) شهريًا ══════════ */
        var months = tx
            .Where(x => x.TransactionType == "Receipt" || x.TransactionType == "Payment")
            .GroupBy(x => new { x.Day.Year, x.Day.Month })
            .Select(g => new CashMonth(
                $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                MonthAr(g.Key.Month) + " " + g.Key.Year,
                g.Where(x => x.TransactionType == "Receipt").Sum(x => x.Amount),
                g.Where(x => x.TransactionType == "Payment").Sum(x => x.Amount),
                g.Where(x => x.TransactionType == "Receipt").Sum(x => x.Amount)
                    - g.Where(x => x.TransactionType == "Payment").Sum(x => x.Amount)))
            .OrderBy(x => x.Key)
            .ToList();

        /* ══════════ 4) حسب الخزينة ══════════ */
        var boxRows = tx
            .GroupBy(x => x.CashBoxId)
            .Select(g =>
            {
                var inf  = g.Where(x => x.TransactionType == "Receipt").Sum(x => x.Amount);
                var outf = g.Where(x => x.TransactionType == "Payment").Sum(x => x.Amount);
                var tin  = g.Where(x => x.TransactionType == "TransferIn").Sum(x => x.Amount);
                var tout = g.Where(x => x.TransactionType == "TransferOut").Sum(x => x.Amount);
                boxes.TryGetValue(g.Key, out var b);
                return new CashBoxRow(
                    b?.NameAr ?? $"خزينة #{g.Key}",
                    inf, outf, tin, tout,
                    inf + tin - outf - tout,
                    b?.CurrentBalance ?? 0);
            })
            .OrderByDescending(x => x.Inflow + x.Outflow)
            .ToList();

        /* ══════════ 5) رصيد أول/آخر الفترة ══════════ */
        var openBal = cashBoxId is null
            ? boxes.Values.Sum(x => x.OpeningBalance)
            : boxes.TryGetValue(cashBoxId.Value, out var ob) ? ob.OpeningBalance : 0;

        var before = await _db.CashTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == "Posted" && x.TransactionDate < f
                        && (cashBoxId == null || x.CashBoxId == cashBoxId))
            .Select(x => new { x.TransactionType, x.Amount }).ToListAsync(ct);

        var opening = openBal
            + before.Where(x => x.TransactionType == "Receipt" || x.TransactionType == "TransferIn").Sum(x => x.Amount)
            - before.Where(x => x.TransactionType == "Payment" || x.TransactionType == "TransferOut").Sum(x => x.Amount);

        var closing = opening + (totalIn - totalOut);

        return new
        {
            range  = new { from = fs, to = ts },
            opening, closing,
            totalIn, totalOut,
            net    = totalIn - totalOut,
            count  = tx.Count,
            inLines, outLines, inSrc, outSrc, months, boxes = boxRows
        };
    }

    /* 🔴 عنصر مساعد — `anonymous type` مش بيتبعت بين الدوال، و`dynamic` مرفوض
       في LINQ (CS1978). فبنحوّل لصف بسيط قبل التجميع. */
    private sealed record RefAmt(decimal Amount, string? ReferenceNumber);

    /// <summary>بيجيب أقسام القائمة بأنواع قوية للتصدير (الـ anonymous ما بيتكاستش).</summary>
    private async Task<(List<CashLine> In, List<CashLine> Out, List<CashMonth> Months)>
        CashLinesForExcelAsync(DateTime f, DateTime t, CancellationToken ct)
    {
        var tx = await _db.CashTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == "Posted"
                        && x.TransactionDate >= f && x.TransactionDate <= t)
            .Select(x => new { x.TransactionType, x.Amount, x.PaymentMethodId,
                               Day = (DateTime)x.TransactionDate })
            .ToListAsync(ct);

        var inf  = tx.Where(x => x.TransactionType == "Receipt").ToList();
        var outf = tx.Where(x => x.TransactionType == "Payment").ToList();

        /* المبالغ الحقيقية حسب الطريقة — نفس منطق BySource بس على `Receipt`/`Payment` */
        var methods = (await _db.PaymentMethods.AsNoTracking()
                .Select(x => new { x.PaymentMethodId, x.NameAr }).ToListAsync(ct))
            .ToDictionary(x => x.PaymentMethodId, x => x.NameAr);

        List<CashLine> Lines(IEnumerable<(decimal A, int? M)> src)
        {
            var g = src.GroupBy(x => x.M ?? -1)
                       .Select(x => new { K = x.Key, C = x.Count(), A = x.Sum(y => y.A) })
                       .OrderByDescending(x => x.A).ToList();
            var tot = g.Sum(x => x.A);
            return g.Select(x => new CashLine(
                x.K == -1 ? "بدون طريقة محددة" : methods.TryGetValue(x.K, out var n) ? n : $"طريقة #{x.K}",
                x.C, x.A, tot <= 0 ? 0 : Math.Round(x.A / tot * 100, 1))).ToList();
        }

        var months = tx.GroupBy(x => new { x.Day.Year, x.Day.Month })
            .Select(g => new CashMonth(
                $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                MonthAr(g.Key.Month) + " " + g.Key.Year,
                g.Where(x => x.TransactionType == "Receipt").Sum(x => x.Amount),
                g.Where(x => x.TransactionType == "Payment").Sum(x => x.Amount),
                g.Where(x => x.TransactionType == "Receipt").Sum(x => x.Amount)
                    - g.Where(x => x.TransactionType == "Payment").Sum(x => x.Amount)))
            .OrderBy(x => x.Key).ToList();

        return (Lines(inf.Select(x => (x.Amount, x.PaymentMethodId))),
                Lines(outf.Select(x => (x.Amount, x.PaymentMethodId))),
                months);
    }

    private static List<CashLine> BySourceCore(IEnumerable<RefAmt> src)
    {
        var byKey = src
            .GroupBy(SourceLabel)
            .Select(g => new { Label = g.Key, Count = g.Count(), Amt = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.Amt)
            .ToList();

        var grand = byKey.Sum(x => x.Amt);
        return byKey.Select(x => new CashLine(x.Label, x.Count, x.Amt,
            grand <= 0 ? 0 : Math.Round(x.Amt / grand * 100, 1))).ToList();
    }

    /// <summary>
    /// بيترجم <c>ReferenceNumber</c> لاسم مفهوم للمستخدم.
    /// 🔴 مافيش مصطلح محاسبي — §59/§66.
    /// </summary>
    private static string SourceLabel(RefAmt x)
    {
        var r = x.ReferenceNumber ?? "";
        var p = r.IndexOf(':');
        var k = (p < 0 ? r : r[..p]).Trim();
        return k switch
        {
            "PAY"           => "تحصيل فواتير العملاء",
            "CUST-ISSUE"    => "صرف عهد السائقين",
            "CUST-REFUND"   => "مرتجع عهد السائقين",
            "EXP"           => "مصروفات التشغيل",
            "SPAY"          => "سداد فواتير الموردين",
            "RCV"           => "سند قبض يدوي",
            "PMT"           => "سند صرف يدوي",
            ""              => "حركات بدون مرجع",
            _               => "حركات أخرى"
        };
    }

    private static string MonthAr(int m) => m switch
    {
        1 => "يناير", 2 => "فبراير", 3 => "مارس", 4 => "أبريل",
        5 => "مايو", 6 => "يونيو", 7 => "يوليو", 8 => "أغسطس",
        9 => "سبتمبر", 10 => "أكتوبر", 11 => "نوفمبر", 12 => "ديسمبر",
        _ => "?"
    };

    /// <summary>
    /// 📄 <c>GET api/reports/supplier-statement?id=</c> — **كشف حساب مورد**.
    /// فواتير (+) ودفعات (−) بالترتيب الزمني مع الرصيد الجاري.
    /// </summary>
    [HttpGet("supplier-statement")]
    public async Task<IActionResult> SupplierStatement(int id, string? from, string? to, CancellationToken ct)
    {
        if (!await Can("SUPPLIER.VIEW", ct)) return Forbid();
        if (id <= 0) return BadRequest(new { message = "لازم تختار المورد" });

        var sup = await _db.Suppliers.AsNoTracking()
            .Where(s => s.SupplierId == id && !s.IsDeleted)
            .Select(s => new { s.SupplierId, s.NameAr }).FirstOrDefaultAsync(ct);
        if (sup is null) return NotFound(new { message = "المورد مش موجود" });

        var f = DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today).AddMonths(-6);
        var t = DateOnly.TryParse(to, out var td) ? td : DateOnly.FromDateTime(DateTime.Today);

        var inv = await _db.SupplierInvoices.AsNoTracking()
            .Where(i => i.SupplierId == id && !i.IsDeleted
                        && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.InvoiceDate >= f && i.InvoiceDate <= t)
            .Select(i => new { i.SupplierInvoiceNumber, i.InvoiceDate, i.GrandTotal })
            .ToListAsync(ct);

        var pay = await _db.SupplierPayments.AsNoTracking()
            .Where(x => x.SupplierId == id && !x.IsDeleted && x.Status != "Cancelled"
                        && x.PaymentDate >= f && x.PaymentDate <= t)
            .Select(x => new { x.SupplierPaymentNumber, x.PaymentDate, x.Amount })
            .ToListAsync(ct);

        var tx = inv.Select(x => new StmtRow("فاتورة", x.SupplierInvoiceNumber,
                Iso(x.InvoiceDate), x.GrandTotal, 0m))
            .Concat(pay.Select(x => new StmtRow("دفعة", x.SupplierPaymentNumber,
                Iso(x.PaymentDate), 0m, x.Amount)))
            .OrderBy(x => x.Date, StringComparer.Ordinal)
            .ThenBy(x => x.RefNo, StringComparer.Ordinal)
            .ToList();

        decimal bal = 0m;
        var withBal = new List<StmtRowBal>(tx.Count);
        foreach (var r in tx)
        {
            bal += r.Debit - r.Credit;
            withBal.Add(new StmtRowBal(r.Kind, r.RefNo, r.Date, r.Debit, r.Credit, bal));
        }

        return Ok(new
        {
            supplierId   = sup.SupplierId,
            supplierName = sup.NameAr,
            range        = new { from = Iso(f), to = Iso(t) },
            totalDebit   = tx.Sum(x => x.Debit),
            totalCredit  = tx.Sum(x => x.Credit),
            balance      = bal,
            rows         = withBal
        });
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
        else if (report == "pnl")
        {
            if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();
            var (sm, mo, cr) = await BuildPnlAsync(f, t, ct);

            /* ── ورقة 1: القائمة ── */
            Write(wb, "قائمة الدخل", "قائمة الدخل", fs, ts,
                new[] { "البند", "المبلغ" },
                new[]
                {
                    new object[] { "الإيرادات (قبل الضريبة)",        sm.Revenue },
                    new object[] { "ضريبة القيمة المضافة المحصّلة",  sm.Tax },
                    new object[] { "(-) تكلفة التشغيل المباشرة",     sm.OperationCost },
                    new object[] { "= مجمل الربح",                   sm.GrossProfit },
                    new object[] { "هامش مجمل الربح %",              sm.GrossMargin },
                    new object[] { "(-) المصروفات التشغيلية",        sm.OperatingExpenses },
                    new object[] { "= صافي الربح",                   sm.NetProfit },
                    new object[] { "هامش صافي الربح %",              sm.NetMargin },
                    new object[] { "عدد الفواتير",                   sm.InvoiceCount },
                    new object[] { "عدد المصروفات",                  sm.ExpenseCount },
                });
            /* 🔴 `.Style.Font.Bold` خاصية — مش `SetBold()` (بترجع IFont ⇒ CS1061) */
            var p1 = wb.Worksheet(1);
            p1.Range(8, 1, 8, 2).Style.Font.Bold = true;
            p1.Range(11, 1, 11, 2).Style.Font.Bold = true;

            /* ── ورقة 2: شهريًا ── */
            Write(wb, "شهريًا", "قائمة الدخل — شهريًا", fs, ts,
                new[] { "الشهر", "الإيراد", "الضريبة", "تكلفة التشغيل",
                        "مجمل الربح", "إداري", "صافي الربح" },
                mo.Select(m => new object[]
                {
                    m.MonthName, m.Revenue, m.Tax, m.OperationCost,
                    m.Revenue - m.OperationCost, m.OperatingExpenses,
                    m.Revenue - m.OperationCost - m.OperatingExpenses
                }));

            /* ── ورقة 3: تفصيل التكلفة ── */
            Write(wb, "تفصيل التكلفة", "تكلفة التشغيل حسب النوع", fs, ts,
                new[] { "نوع المصروف", "العدد", "المبلغ" },
                cr.Select(x => new object[] { x.TypeName, x.Count, x.Amount }));

            name = $"pnl_{fs}_{ts}.xlsx";
        }
        else if (report == "overdue")
        {
            if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();
            var od = await BuildOverdueAsync(ct);

            Write(wb, "المتأخرات", "فواتير عدى ميعاد سدادها", fs, ts,
                new[] { "رقم الفاتورة", "العميل", "تاريخ الإصدار", "تاريخ الاستحقاق",
                        "أيام تأخير", "الإجمالي", "المحصّل", "المتبقي", "الحالة" },
                od.Select(x => new object[]
                {
                    x.InvoiceNumber, x.CustomerName, x.InvoiceDate, x.DueDate,
                    x.DaysOverdue, x.GrandTotal, x.PaidAmount, x.Balance, x.PaymentStatus
                }));

            var s4 = wb.Worksheet(1);
            var lr4 = od.Count + 5;
            s4.Cell(lr4, 1).Value = "الإجمالي";
            s4.Cell(lr4, 6).Value = od.Sum(x => x.GrandTotal);
            s4.Cell(lr4, 7).Value = od.Sum(x => x.PaidAmount);
            s4.Cell(lr4, 8).Value = od.Sum(x => x.Balance);
            s4.Range(lr4, 1, lr4, 9).Style.Font.Bold = true;

            name = $"overdue_{fs}_{ts}.xlsx";
        }
        else if (report == "compare")
        {
            if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();
            var cp = await BuildCompareAsync(f, t, ct);

            Write(wb, "مقارنة", "مقارنة الفترة الحالية بالسابقة", fs, ts,
                new[] { "البند", "الفترة الحالية", "الفترة السابقة", "الفرق", "نسبة التغير %" },
                cp.Select(x => new object[]
                {
                    x.Label, x.Current, x.Previous, x.Delta, x.ChangePct
                }));

            name = $"compare_{fs}_{ts}.xlsx";
        }
        else if (report == "ports" || report == "services")
        {
            if (!await Can("REPORT.OPERATIONS", ct)) return Forbid();
            var o2 = await LoadOpProfitAsync(f, t, ct);

            if (report == "ports")
            {
                var pids = o2.Where(x => x.PortId is not null).Select(x => x.PortId!.Value).Distinct().ToList();
                var pn = pids.Count == 0 ? new Dictionary<int, string>()
                    : (await _db.Ports.AsNoTracking().Where(x => pids.Contains(x.PortId))
                            .Select(x => new { x.PortId, x.NameAr }).ToListAsync(ct))
                        .ToDictionary(x => x.PortId, x => x.NameAr);
                Write(wb, "الموانئ", "ربحية الموانئ", fs, ts,
                    new[] { "الميناء", "عدد العمليات", "الإيراد", "التكلفة", "الربح", "الهامش %" },
                    Rows(o2.Where(x => x.PortId is not null).GroupBy(x => x.PortId!.Value),
                         k => pn.TryGetValue(k, out var n) ? n : "—"));

                var dids = o2.Where(x => x.DestinationId is not null).Select(x => x.DestinationId!.Value).Distinct().ToList();
                var dn = dids.Count == 0 ? new Dictionary<int, string>()
                    : (await _db.Destinations.AsNoTracking().Where(x => dids.Contains(x.DestinationId))
                            .Select(x => new { x.DestinationId, x.NameAr }).ToListAsync(ct))
                        .ToDictionary(x => x.DestinationId, x => x.NameAr);
                Write(wb, "الوجهات", "ربحية الوجهات", fs, ts,
                    new[] { "الوجهة", "عدد العمليات", "الإيراد", "التكلفة", "الربح", "الهامش %" },
                    Rows(o2.Where(x => x.DestinationId is not null).GroupBy(x => x.DestinationId!.Value),
                         k => dn.TryGetValue(k, out var n) ? n : "—"));
            }
            else
            {
                var sids = o2.Where(x => x.ServiceId is not null).Select(x => x.ServiceId!.Value).Distinct().ToList();
                var sn = sids.Count == 0 ? new Dictionary<int, string>()
                    : (await _db.Services.AsNoTracking().Where(x => sids.Contains(x.ServiceId))
                            .Select(x => new { x.ServiceId, x.NameAr }).ToListAsync(ct))
                        .ToDictionary(x => x.ServiceId, x => x.NameAr);
                Write(wb, "الخدمات", "ربحية الخدمات", fs, ts,
                    new[] { "الخدمة", "عدد العمليات", "الإيراد", "التكلفة", "الربح", "الهامش %" },
                    Rows(o2.Where(x => x.ServiceId is not null).GroupBy(x => x.ServiceId!.Value),
                         k => sn.TryGetValue(k, out var n) ? n : "—"));
            }

            name = $"{report}_{fs}_{ts}.xlsx";
        }
        else if (report == "cashflow")
        {
            if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();
            var cf = await BuildCashflowAsync(ct);

            Write(wb, "التدفق المتوقع", "تدفق نقدي متوقع", fs, ts,
                new[] { "الفترة", "المتوقع", "عدد الفواتير" },
                cf.Select(x => new object[] { x.Label, x.Expected, x.Invoices }));

            name = $"cashflow_{fs}_{ts}.xlsx";
        }
        else if (report == "supplier-aging")
        {
            if (!await Can("SUPPLIER.VIEW", ct)) return Forbid();
            var ag = await BuildSupplierAgingAsync(ct);

            Write(wb, "أعمار الموردين", "أعمار ديون الموردين", fs, ts,
                new[] { "المورد", "فواتير", "0–30 يوم", "31–60", "61–90", "أكثر من 90",
                        "بدون استحقاق", "الإجمالي" },
                ag.Select(x => new object[] { x.SupplierName, x.Invoices, x.D0_30, x.D31_60,
                    x.D61_90, x.D90Plus, x.NoDue, x.Balance }));

            var s5 = wb.Worksheet(1);
            var lr5 = ag.Count + 5;
            s5.Cell(lr5, 1).Value = "الإجمالي";
            for (var c = 3; c <= 8; c++)
                s5.Cell(lr5, c).Value = ag.Sum(x => c switch
                {
                    3 => x.D0_30, 4 => x.D31_60, 5 => x.D61_90,
                    6 => x.D90Plus, 7 => x.NoDue, _ => x.Balance
                });
            s5.Range(lr5, 1, lr5, 8).Style.Font.Bold = true;

            name = $"supplier-aging_{fs}_{ts}.xlsx";
        }
        else if (report == "cashflow-actual")
        {
            if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();
            var ca = await BuildCashflowActualAsync(f, t, fs, ts, null, ct);

            /* 🔴 `BuildCashflowActualAsync` بترجع `Task<object>` (anonymous) —
               فبنرجّع للتجميع نفسه عشان يبقى لنا نوع قوي، مش كاست. */
            var (caIn, caOut, caMonths) = await CashLinesForExcelAsync(f, t, ct);

            Write(wb, "التدفق النقدي", "قائمة التدفقات النقدية الفعلية", fs, ts,
                new[] { "البند", "عدد الحركات", "المبلغ", "%" },
                caIn.Select(x => new object[] { "وارد: " + x.Label, x.Count, x.Amount, x.Pct })
                    .Concat(caOut.Select(x => new object[]
                        { "صادر: " + x.Label, x.Count, x.Amount, x.Pct }))
                    .Concat(caMonths.Select(x => new object[]
                        { "صافي " + x.MonthName, x.Inflow > 0 ? 1 : 0, x.Net, 0m })));

            name = $"cashflow-actual_{fs}_{ts}.xlsx";
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
        /* 🔴 CS1061: `SetBold()` و`SetItalic()` بترجع `IFont` — فـ`.Font.` بعدهم مش موجودة.
           الحل: الخصائص (زي سطر 504 في نفس الملف). */
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontSize = 10;

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

    /* ═══════════ 📥 الذمم الدائنة ═══════════ */

    /// <summary>صف في أعمار ديون الموردين.</summary>
    public record AgingRow(string SupplierName, int Invoices,
        decimal D0_30, decimal D31_60, decimal D61_90, decimal D90Plus,
        decimal NoDue, decimal Balance);

    /// <summary>حركة في كشف حساب مورد (قبل الرصيد الجاري).</summary>
    public record StmtRow(string Kind, string RefNo, string Date, decimal Debit, decimal Credit);

    /// <summary>حركة + الرصيد الجاري.</summary>
    public record StmtRowBal(string Kind, string RefNo, string Date,
        decimal Debit, decimal Credit, decimal Balance);

    /* ═══════════ 🚢 الموانئ · 🧾 الخدمات · 💵 التدفق ═══════════ */

    /// <summary>صف ربحية مجمّع (ميناء / وجهة / خدمة).</summary>
    public record ProfitRow(string Name, int Operations, decimal Revenue,
        decimal Cost, decimal Profit, decimal MarginPct);

    /// <summary>أسبوع في التدفق النقدي المتوقع.</summary>
    public record CashWeek(string Key, string Label, decimal Expected, int Invoices);

    /* ═══════════ ⏰ المتأخرات + 📈 المقارنة ═══════════ */

    /// <summary>فاتورة عدى ميعاد سدادها.</summary>
    public record OverdueRow(string InvoiceNumber, string CustomerName,
        string InvoiceDate, string DueDate, int DaysOverdue,
        decimal GrandTotal, decimal PaidAmount, decimal Balance, string PaymentStatus);

    /// <summary>بند في مقارنة الفترتين.</summary>
    public record CompareRow(string Label, decimal Current, decimal Previous,
        decimal Delta, decimal ChangePct);

    /* ═══════════ 📊 قائمة الدخل ═══════════ */

    /// <summary>شهر واحد في قائمة الدخل.</summary>
    public record PnlMonth(string Month, string MonthName,
        decimal Revenue, decimal Tax, decimal OperationCost, decimal OperatingExpenses);

    /// <summary>تفصيل التكلفة المباشرة حسب نوع المصروف.</summary>
    public record PnlCostRow(string TypeName, int Count, decimal Amount);

    /// <summary>إجماليات الفترة.</summary>
    public record PnlSummary(
        decimal Revenue, decimal Tax, decimal OperationCost, decimal GrossProfit,
        decimal OperatingExpenses, decimal NetProfit,
        decimal GrossMargin, decimal NetMargin,
        int InvoiceCount, int ExpenseCount);

    public record FinSummary(decimal Invoiced, decimal Collected, decimal Expenses,
        decimal Receivable, decimal NetCash, int InvoiceCount, int PaymentCount);

    public record MonthRow(string Month, string MonthName, decimal Invoiced,
        decimal Collected, decimal Expenses, decimal Net);

    public record ExpenseRow(string TypeName, int Count, decimal Total, decimal Approved,
        decimal VehicleTotal = 0m);

    public record CustomerRow(int CustomerId, string CustomerName,
        decimal Invoiced, decimal Paid, decimal Balance);

    public record DriverRow(int DriverId, string DriverName, int Trips, int Completed,
        decimal TotalKm, decimal AvgHours, decimal CustodyIssued, int OpenCustodies);

    public record VehicleRow(int VehicleId, string PlateNumber, int Trips, int Completed,
        decimal TotalKm, decimal TripCost, int MaintCount, decimal MaintCost,
        decimal Revenue, decimal Profit, decimal CostPerKm);

    public record FleetTotals(int Trips, int Completed, decimal TotalKm,
        decimal MaintCost, int Custodies, decimal CustodyOut);

    /* ═══════════════ المرحلة 7 — العهد + حساب العربية ═══════════════ */

    public record CustodySummaryRow(string OwnerName, string OwnerType,
        int CustodyCount, decimal Issued, decimal Spent, decimal Returned, decimal Remaining);

    public record CustodyDetailRow(long CustodyId, string CustodyNumber, string OwnerName,
        string OwnerType, string? TripNumber, string Status, DateTime CustodyDate,
        decimal Issued, decimal Spent, decimal Returned, decimal Remaining);

    public record CustodyReportOut(List<CustodySummaryRow> Summary, List<CustodyDetailRow> Details);

    /// <summary>
    /// تقرير العهد — ملخص لكل سائق/أمين عهدة + تفاصيل كل عهدة.
    /// </summary>
    [HttpGet("custodies")]
    public async Task<IActionResult> Custodies(string? from, string? to,
        [FromQuery] string? ownerType, [FromQuery] int? ownerId, CancellationToken ct)
    {
        if (!await Can("REPORT.FINANCIAL", ct)) return Forbid();

        var (f, t, _, _) = Range(from, to);

        var q = _db.DriverCustodies.AsNoTracking()
            .Where(c => !c.IsDeleted && c.CreatedAt >= f && c.CreatedAt <= t);

        if (ownerType is "Driver" or "Employee") q = q.Where(c => c.OwnerType == ownerType);
        if (ownerId is not null)                 q = q.Where(c => c.OwnerId == ownerId);

        var rows = await q
            .OrderByDescending(c => c.CustodyId)
            .Take(MaxRows)
            .Select(c => new
            {
                c.CustodyId,
                c.CustodyNumber,
                c.OwnerType,
                c.OwnerId,
                c.Status,
                c.CreatedAt,
                c.AmountIssued,
                c.AmountSpent,
                c.AmountReturned,
                TripNumber = c.Trip != null ? c.Trip.TripNumber : null
            })
            .ToListAsync(ct);

        /* أسماء الملاك */
        var driverIds = rows.Where(r => r.OwnerType == "Driver").Select(r => r.OwnerId).Distinct().ToList();
        var empIds    = rows.Where(r => r.OwnerType == "Employee").Select(r => r.OwnerId).Distinct().ToList();

        var driverNames = driverIds.Count == 0 ? new Dictionary<int, string>()
            : (await _db.Drivers.AsNoTracking().Where(d => driverIds.Contains(d.DriverId))
                .Select(d => new { d.DriverId, d.FullName }).ToListAsync(ct))
              .ToDictionary(x => x.DriverId, x => x.FullName);

        var empNames = empIds.Count == 0 ? new Dictionary<int, string>()
            : (await _db.Employees.AsNoTracking().Where(e => empIds.Contains(e.EmployeeId))
                .Select(e => new { e.EmployeeId, FullName = e.FullNameAr }).ToListAsync(ct))
              .ToDictionary(x => x.EmployeeId, x => x.FullName);

        string NameOf(string type, int id) => type == "Driver"
            ? (driverNames.TryGetValue(id, out var dn) ? dn : "—")
            : (empNames.TryGetValue(id, out var en) ? en : "—");

        var details = rows.Select(r => new CustodyDetailRow(
            r.CustodyId, r.CustodyNumber, NameOf(r.OwnerType, r.OwnerId), r.OwnerType,
            r.TripNumber, r.Status, r.CreatedAt,
            r.AmountIssued, r.AmountSpent, r.AmountReturned,
            r.AmountIssued - r.AmountSpent - r.AmountReturned))
            .ToList();

        var summary = rows
            .GroupBy(r => (r.OwnerType, r.OwnerId))
            .Select(g => new CustodySummaryRow(
                NameOf(g.Key.OwnerType, g.Key.OwnerId), g.Key.OwnerType,
                g.Count(), g.Sum(x => x.AmountIssued), g.Sum(x => x.AmountSpent),
                g.Sum(x => x.AmountReturned),
                g.Sum(x => x.AmountIssued) - g.Sum(x => x.AmountSpent) - g.Sum(x => x.AmountReturned)))
            .OrderByDescending(x => x.Remaining)
            .ToList();

        return Ok(new CustodyReportOut(summary, details));
    }

    public record VehicleAccountRow(int VehicleId, string PlateNumber,
        decimal Freight, decimal Custody, decimal Expenses, decimal Payments, decimal Net);

    /// <summary>
    /// تقرير حساب العربية — ملخص لكل عربية في الفترة.
    /// </summary>
    [HttpGet("vehicle-accounts")]
    public async Task<IActionResult> VehicleAccounts(string? from, string? to, CancellationToken ct)
    {
        if (!await Can("REPORT.FLEET", ct)) return Forbid();

        var (f, t, _, _) = Range(from, to);

        var vehicles = await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted)
            .OrderBy(v => v.PlateNumber)
            .Select(v => new { v.VehicleId, v.PlateNumber })
            .ToListAsync(ct);

        var vehIds = vehicles.Select(v => v.VehicleId).ToList();

        var freight = (await _db.Trips.AsNoTracking()
            .Where(x => !x.IsDeleted && vehIds.Contains(x.VehicleId) &&
                        x.ActualEndAt != null && x.ActualEndAt >= f && x.ActualEndAt <= t)
            .GroupBy(x => x.VehicleId)
            .Select(g => new { VehicleId = g.Key, Total = g.Sum(x => (decimal?)x.FreightAmount) })
            .ToListAsync(ct)).ToDictionary(x => x.VehicleId, x => x.Total ?? 0);

        var custody = (await _db.DriverCustodies.AsNoTracking()
            .Where(c => !c.IsDeleted && c.OwnerType == "Driver" &&
                        c.Trip != null && vehIds.Contains(c.Trip.VehicleId) &&
                        c.Trip.ActualEndAt != null && c.Trip.ActualEndAt >= f && c.Trip.ActualEndAt <= t)
            .GroupBy(c => c.Trip!.VehicleId)
            .Select(g => new { VehicleId = g.Key, Total = g.Sum(x => (decimal?)x.AmountIssued) })
            .ToListAsync(ct)).ToDictionary(x => x.VehicleId, x => x.Total ?? 0);

        var expenses = (await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.VehicleId != null && vehIds.Contains(e.VehicleId.Value) &&
                        e.ExpenseDate >= f && e.ExpenseDate <= t && e.Status != "Cancelled")
            .GroupBy(e => e.VehicleId!.Value)
            .Select(g => new { VehicleId = g.Key, Total = g.Sum(x => (decimal?)x.Amount) })
            .ToListAsync(ct)).ToDictionary(x => x.VehicleId, x => x.Total ?? 0);

        var payments = (await _db.CashTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.VehicleId != null && vehIds.Contains(x.VehicleId.Value) &&
                        x.Status == "Posted" && x.TransactionDate >= f && x.TransactionDate <= t)
            .GroupBy(x => x.VehicleId!.Value)
            .Select(g => new { VehicleId = g.Key, Total = g.Sum(x => (decimal?)x.Amount) })
            .ToListAsync(ct)).ToDictionary(x => x.VehicleId, x => x.Total ?? 0);

        var rows = vehicles.Select(v =>
        {
            var fr = freight.TryGetValue(v.VehicleId, out var a) ? a : 0m;
            var cu = custody.TryGetValue(v.VehicleId, out var b) ? b : 0m;
            var ex = expenses.TryGetValue(v.VehicleId, out var c) ? c : 0m;
            var pa = payments.TryGetValue(v.VehicleId, out var d) ? d : 0m;
            return new VehicleAccountRow(v.VehicleId, v.PlateNumber, fr, cu, ex, pa, fr - cu - ex - pa);
        })
        .Where(r => r.Freight != 0 || r.Custody != 0 || r.Expenses != 0 || r.Payments != 0)
        .OrderByDescending(r => r.Net)
        .ToList();

        return Ok(rows);
    }
}
