using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Auth;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Finance;

/// <summary>
/// العهود — فلوس بتتسلّم لسائق أو موظف على رحلة، وبيتصرف منها، والباقي يت رد.
/// <para>🔴 <c>AmountSpent / AmountReturned / AdditionalDue</c> محسوبة بـ <c>trg_CustodyTransactions_Sync</c>
/// من جدول الحركات — <b>ماتكتبهاش من الكود أبدًا</b>.</para>
/// <para>الحالة: <c>Open → PartiallySettled → Submitted → Approved → Closed</c>
/// (الـ trigger بيكتب Open/PartiallySettled، وإحنا بنكتب Submitted/Approved/Closed).</para>
/// </summary>
[ApiController]
[Route("api/custodies")]
[Authorize]
public class CustodiesController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly IAuditService _audit;
    private readonly INumberingService _numbers;
    private readonly IPermissionService _perms;
    private readonly ISettingsService _settings;
    private readonly ICashBook _cash;

    public CustodiesController(FastComDbContext db, INumberingService numbers, IPermissionService perms,
                               ISettingsService settings, ICashBook cash, IAuditService audit)
    { _db = db; _audit = audit; _numbers = numbers; _perms = perms; _settings = settings; _cash = cash; }

    /* 🔴 كود طريقة الدفع «نقدي» — لصرف العهدة ورد الباقي.
       العهدة **كاش** بطبيعتها (فلوس بتتسلّم بالإيد)، فمش محتاجين طريقة دفع من المستخدم. */
    private async Task<int?> CashMethodIdAsync(CancellationToken ct) =>
        await _db.PaymentMethods.AsNoTracking()
            .Where(m => m.Code == "CASH" && m.IsActive)
            .Select(m => (int?)m.PaymentMethodId)
            .FirstOrDefaultAsync(ct);

    /* ═══ عهدة تشغيل عامة ═══
       عهدة موظف (رئيسية) من غير رحلة: الموظف بيصرف منها على عمليات ورحلات
       مختلفة، وفلوسها بتخرج من **خزينة التشغيل اللي باسمه** وردّها بيرجع لها. */
    private static bool IsGeneralOps(DriverCustody c) =>
        c.IsPrimary && c.OwnerType == "Employee" && c.TripId is null;

    /// <summary>خزينة التشغيل المفتوحة باسم الموظف (الصندوق اللي هو مسؤول عنه).</summary>
    private async Task<CashBox?> EmployeeBoxAsync(int employeeId, CancellationToken ct) =>
        await _db.CashBoxes.AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive && b.Status == "Open"
                        && b.ResponsibleEmployeeId == employeeId)
            .OrderBy(b => b.CashBoxId)
            .FirstOrDefaultAsync(ct);

    /// <summary>صرف/رد عهدة عامة فشل في خزينة الموظف — الإنشاء كله بيتلغي.</summary>
    private sealed class GeneralCustodyBoxException : Exception;

    /// <summary>
    /// رد الباقي تلقائيًا لخزينة الموظف (قرار المالك): حركة «رد باقي» + إيصال
    /// خزينة بنفس مرجع الرد اليدوي. يُستدعى داخل معاملة الاعتماد/الإغلاق —
    /// وفشل الإيصال يرمي استثناءً يلغي المعاملة كلها فلا يضيع الباقي.
    /// </summary>
    private async Task<decimal> AutoRefundRemainderAsync(
        DriverCustody c, CashBox empBox, string source, CancellationToken ct)
    {
        var remaining = c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned;
        if (remaining <= 0) return 0;

        var tx = new CustodyTransaction
        {
            CustodyId       = c.CustodyId,
            TransactionType = "Refund",
            Amount          = remaining,
            TransactionDate = DateTime.Today,
            Notes           = source == "Approve" ? "رد باقي تلقائي عند الاعتماد" : "رد باقي تلقائي عند الإغلاق",
            CreatedBy       = CurrentUserId()
        };
        _db.CustodyTransactions.Add(tx);
        await _db.SaveChangesAsync(ct);

        var ok = await _cash.ReceiptAsync(new CashEntry(
            ReferenceNumber : $"CUST-REFUND:{c.CustodyNumber}:{tx.CustodyTransactionId}",
            Amount          : remaining,
            PaymentMethodId : await CashMethodIdAsync(ct) ?? 0,
            CustodyId       : c.CustodyId,
            Description     : $"رد باقي عهدة {c.CustodyNumber} (تلقائي)",
            Date            : DateOnly.FromDateTime(DateTime.Today),
            CashBoxId       : empBox.CashBoxId), ct);
        if (!ok) throw new GeneralCustodyBoxException();
        return remaining;
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static DateTime Dt(string? s) => DateTime.TryParse(s, out var d) ? d : DateTime.UtcNow;

    /// <summary>اسم الحالة بالعربي — لرسائل الرفض الواضحة.</summary>
    private static string StatusAr(string? s) => s switch
    {
        "Open"             => "مفتوحة",
        "PartiallySettled" => "مسوّاة جزئيًا",
        "Submitted"        => "متقدّمة للتسوية",
        "Approved"         => "معتمدة",
        "Closed"           => "مقفولة",
        _                  => s ?? "غير معروفة"
    };

    private async Task<int?> ResolveBranchIdAsync(CancellationToken ct)
    {
        if (int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0) return b;
        return await _db.Branches.AsNoTracking()
            .OrderBy(x => x.BranchId).Select(x => (int?)x.BranchId).FirstOrDefaultAsync(ct);
    }

    // ═══════════════ DTOs ═══════════════

    public record CustodyCreate(long? TripId, string OwnerType, int OwnerId,
        decimal AmountIssued, string? CustodyDate, string? Notes,
        bool IsPrimary = true, long? ParentCustodyId = null,
        /* 🔴 Freight = سلفة من نولون النقله / Road = مصاريف طريق / NULL = عامة */
        string? AllocationType = null,
        /* #61 — خزينة صرف العهدة الرئيسية — null = الخزينة الافتراضية */
        int? CashBoxId = null);

    public record TxRequest(string? Type, decimal Amount, string? TxDate, long? ExpenseId, string? Notes);

    public record ApproveRequest(bool AddAdditional);

    public record ListItem(long CustodyId, string CustodyNumber, DateTime CustodyDate,
        string OwnerType, string OwnerName, long? TripId, string? TripNumber, string? DriverName,
        decimal AmountIssued, decimal AmountSpent, decimal AmountReturned, decimal AdditionalDue,
        decimal Remaining, string Status, int TxCount,
        bool IsPrimary, long? ParentCustodyId, string? ParentCustodyNumber, int SubCount,
        decimal Distributed, string? AllocationType);

    public record Detail(long CustodyId, string CustodyNumber, DateTime CustodyDate,
        string OwnerType, int OwnerId, string OwnerName, long? TripId, string? TripNumber,
        string? DriverName, decimal AmountIssued, decimal AmountSpent, decimal AmountReturned,
        decimal AdditionalDue, decimal Remaining, string Status, string? Notes,
        DateTime? ClosedAt, bool IsPrimary, long? ParentCustodyId, string? ParentCustodyNumber);

    public record TxLine(long CustodyTransactionId, string TransactionType, DateTime TransactionDate,
        decimal Amount, long? ExpenseId, string? ExpenseNumber, string? Notes,
        string? ExpenseTypeName = null, string? ExpenseDescription = null,
        string? OperationNumber = null, string? TripNumber = null,
        string? DriverName = null, string? VehiclePlate = null);

    public record CustodyExpense(long ExpenseId, string ExpenseNumber, string TypeName,
        decimal Amount, DateTime ExpenseDate, string? Description, string PaymentStatus,
        long? OperationId = null, string? OperationNumber = null,
        long? TripId = null, string? TripNumber = null,
        bool IsFreightAdvance = false, decimal? BillableAmount = null, int? DriverId = null,
        string? DriverName = null, string? VehiclePlate = null);

    public record TripOpt(long TripId, string TripNumber, string Status, int DriverId, string DriverName);

    public record Opt(int Id, string Label);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:CUSTODY.VIEW")]
    public async Task<IActionResult> List(string? status, string? owner, long? tripId,
        string? q, int take = 300, CancellationToken ct = default)
    {
        if (take <= 0 || take > 1000) take = 300;

        var query = _db.DriverCustodies.AsNoTracking().Where(c => !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(owner))  query = query.Where(c => c.OwnerType == owner);
        if (tripId is not null)                 query = query.Where(c => c.TripId == tripId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(c => c.CustodyNumber.Contains(s) ||
                                     (c.Trip != null && c.Trip.TripNumber.Contains(s)));
        }

        var rows = await query
            .OrderByDescending(c => c.CustodyId)
            .Take(take)
            .Select(c => new ListItem(
                c.CustodyId, c.CustodyNumber, c.CustodyDate, c.OwnerType,
                c.OwnerType == "Driver"
                    ? _db.Drivers.Where(d => d.DriverId == c.OwnerId).Select(d => d.FullName).FirstOrDefault() ?? "—"
                    : _db.Employees.Where(e => e.EmployeeId == c.OwnerId).Select(e => e.FullNameAr).FirstOrDefault() ?? "—",
                c.TripId, c.Trip != null ? c.Trip.TripNumber : null,
                c.Trip != null ? c.Trip.Driver.FullName : null,
                c.AmountIssued, c.AmountSpent, c.AmountReturned, c.AdditionalDue,
                c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned,
                c.Status,
                _db.CustodyTransactions.Count(t => t.CustodyId == c.CustodyId),
                c.IsPrimary, c.ParentCustodyId,
                c.ParentCustody != null ? c.ParentCustody.CustodyNumber : null,
                _db.DriverCustodies.Count(sc => sc.ParentCustodyId == c.CustodyId && !sc.IsDeleted),
                /* #61 — إجمالي اللي اتوزع من العهدة دي على سواقين */
                _db.DriverCustodies.Where(sc => sc.ParentCustodyId == c.CustodyId && !sc.IsDeleted)
                    .Sum(sc => (decimal?)sc.AmountIssued) ?? 0,
                c.AllocationType))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ #61 — خزائن متاحة لصرف العهدة ═══════════════

    public record CashBoxOpt(int CashBoxId, string NameAr, string Code, decimal CurrentBalance,
        int? ResponsibleEmployeeId = null);

    [HttpGet("cash-boxes")]
    [Authorize(Policy = "PERM:CUSTODY.CREATE")]
    public async Task<IActionResult> CashBoxOptions(CancellationToken ct) =>
        Ok(await _db.CashBoxes.AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive && b.Status == "Open")
            .OrderBy(b => b.CashBoxId)
            .Select(b => new CashBoxOpt(b.CashBoxId, b.NameAr, b.Code, b.CurrentBalance, b.ResponsibleEmployeeId))
            .ToListAsync(ct));

    // ═══════════════ #61 — تسوية سلفة النولون مقابل نولون الرحلة ═══════════════

    [HttpPost("{id:long}/freight-settle")]
    [Authorize(Policy = "PERM:CUSTODY.SETTLE")]
    public async Task<IActionResult> FreightSettle(long id, CancellationToken ct)
    {
        /* الفكرة: سلفة النولون (AllocationType=Freight) فلوس مدفوعة مقدّمًا من نولون النقله.
           التسوية بتسجل إن السلفة اتخصمت مقابل نولون الرحلة — **بدون حركة خزينة جديدة**
           (الفلوس خرجت مرة واحدة وقت الصرف). الباقي على السائق يرده أو يتصرف كمصروف. */
        var c = await _db.DriverCustodies
            .FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.OwnerType != "Driver")
            return BadRequest(new { message = "تسوية النولون لسلف السواقين بس" });
        if (c.AllocationType != "Freight")
            return BadRequest(new { message = "هذه العهدة ليست نولون نقلة — التسوية خاصة بالعُهد المصنفة «نولون نقلة»" });
        if (c.TripId is null)
            return BadRequest(new { message = "العهدة مش مربوطة برحلة — اربطها برحلة الأول" });
        if (c.Status is not ("Open" or "PartiallySettled"))
            return BadRequest(new { message = $"حالة العهدة {StatusAr(c.Status)} — التسوية من عهدة مفتوحة بس" });

        var trip = await _db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TripId == c.TripId && !t.IsDeleted, ct);
        if (trip is null) return BadRequest(new { message = "الرحلة المرتبطة بالعهدة مش موجودة" });
        var freight = trip.FreightAmount ?? 0;
        if (freight <= 0)
            return BadRequest(new { message = $"الرحلة {trip.TripNumber} مافيش عليها نولون مسجل — سجل نولون النقله الأول" });

        var alreadySettled = await _db.CustodyTransactions.AsNoTracking()
            .Where(t => t.CustodyId == c.CustodyId && t.TransactionType == "FreightSettle")
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0;

        /* نتسوى بحد أقصى نولون الرحلة — ولو السلفة أكبر من النولون، الزيادة تفضل على السائق */
        var settleAmount = Math.Min(c.AmountIssued, freight) - alreadySettled;
        if (settleAmount <= 0)
            return BadRequest(new { message = $"السلفة مسوّاة بالكامل مقابل نولون الرحلة {trip.TripNumber}" });

        _db.CustodyTransactions.Add(new CustodyTransaction
        {
            CustodyId       = c.CustodyId,
            TransactionType = "FreightSettle",
            Amount          = settleAmount,
            TransactionDate = DateTime.Now,
            Notes           = $"تسوية نولون النقلة مقابل نولون الرحلة {trip.TripNumber} ({freight:N2})",
            CreatedBy       = CurrentUserId()
        });
        await _db.SaveChangesAsync(ct);   // الـ trigger بيحسب الباقي والحالة

        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Update, "Custody", c.CustodyId.ToString(),
            description: $"تسوية نولون {c.CustodyNumber} مقابل الرحلة {trip.TripNumber} بمبلغ {settleAmount:N2}", ct: ct);

        var remaining = c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned - settleAmount;
        return Ok(new { message = $"اتسوت السلفة بمبلغ {settleAmount:N2} ج.م مقابل نولون الرحلة {trip.TripNumber}", remaining });
    }

    // ═══════════════ DETAIL ═══════════════

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:CUSTODY.VIEW")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var c = await _db.DriverCustodies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });

        var owner = c.OwnerType == "Driver"
            ? await _db.Drivers.AsNoTracking().Where(d => d.DriverId == c.OwnerId)
                  .Select(d => d.FullName).FirstOrDefaultAsync(ct)
            : await _db.Employees.AsNoTracking().Where(e => e.EmployeeId == c.OwnerId)
                  .Select(e => e.FullNameAr).FirstOrDefaultAsync(ct);

        var trip = await _db.Trips.AsNoTracking()
            .Where(t => t.TripId == c.TripId)
            .Select(t => new { t.TripNumber, Driver = t.Driver.FullName })
            .FirstOrDefaultAsync(ct);

        var txs = await _db.CustodyTransactions.AsNoTracking()
            .Where(t => t.CustodyId == id)
            .OrderBy(t => t.TransactionDate).ThenBy(t => t.CustodyTransactionId)
            .Select(t => new TxLine(t.CustodyTransactionId, t.TransactionType, t.TransactionDate,
                t.Amount, t.ExpenseId, t.Expense != null ? t.Expense.ExpenseNumber : null, t.Notes,
                t.Expense != null ? t.Expense.ExpenseType.NameAr : null,
                t.Expense != null ? t.Expense.Description : null,
                t.Expense != null && t.Expense.Operation != null ? t.Expense.Operation.OperationNumber : null,
                t.Expense != null && t.Expense.Trip != null ? t.Expense.Trip.TripNumber : null,
                t.Expense != null && t.Expense.Driver != null ? t.Expense.Driver.FullName : (t.Expense != null && t.Expense.Trip != null ? t.Expense.Trip.Driver.FullName : null),
                t.Expense != null && t.Expense.Vehicle != null ? t.Expense.Vehicle.PlateNumber : (t.Expense != null && t.Expense.Trip != null && t.Expense.Trip.Vehicle != null ? t.Expense.Trip.Vehicle.PlateNumber : null)))
            .ToListAsync(ct);

        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => e.CustodyId == id && !e.IsDeleted)
            .OrderByDescending(e => e.ExpenseId)
            .Select(e => new CustodyExpense(e.ExpenseId, e.ExpenseNumber, e.ExpenseType.NameAr,
                e.Amount, e.ExpenseDate, e.Description, e.PaymentStatus,
                e.OperationId, e.Operation != null ? e.Operation.OperationNumber : null,
                e.TripId, e.Trip != null ? e.Trip.TripNumber : null,
                e.ExpenseType.IsFreightAdvance, e.BillableAmount, e.DriverId,
                e.Driver != null ? e.Driver.FullName : (e.Trip != null ? e.Trip.Driver.FullName : null),
                e.Vehicle != null ? e.Vehicle.PlateNumber : (e.Trip != null && e.Trip.Vehicle != null ? e.Trip.Vehicle.PlateNumber : null)))
            .ToListAsync(ct);

        string? parentNumber = null;
        if (c.ParentCustodyId is not null)
            parentNumber = await _db.DriverCustodies.AsNoTracking()
                .Where(pc => pc.CustodyId == c.ParentCustodyId)
                .Select(pc => pc.CustodyNumber).FirstOrDefaultAsync(ct);

        var detail = new Detail(c.CustodyId, c.CustodyNumber, c.CustodyDate, c.OwnerType, c.OwnerId,
            owner ?? "—", c.TripId, trip?.TripNumber, trip?.Driver,
            c.AmountIssued, c.AmountSpent, c.AmountReturned, c.AdditionalDue,
            c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned,
            c.Status, c.Notes, c.ClosedAt, c.IsPrimary, c.ParentCustodyId, parentNumber);

        return Ok(new { Custody = detail, Transactions = txs, Expenses = expenses });
    }

    // ═══════════════ ربط مصروف عند التسوية (عهدة تشغيل عامة) ═══════════════

    public record LinkRequest(long? OperationId, long? TripId);

    /// <summary>
    /// ربط مصروف العهدة العامة بعمليته (أو سلفة النولون برحلتها) — بيحصل وقت
    /// التسوية من تفاصيل العهدة. العهدة لازم تكون لسه مفتوحة والمصروف بتاعها.
    /// </summary>
    [HttpPost("{id:long}/expenses/{expenseId:long}/link")]
    [Authorize(Policy = "PERM:CUSTODY.SETTLE")]
    public async Task<IActionResult> LinkExpense(long id, long expenseId,
        [FromBody] LinkRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { message = "البيانات مش كاملة" });

        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (!IsGeneralOps(c)) return BadRequest(new { message = "الربط عند التسوية خاص بعهد التشغيل العامة" });
        if (c.Status is not ("Open" or "PartiallySettled"))
            return BadRequest(new { message = "العهدة اتقدّمت للتسوية — الربط لازم يتم قبل التقديم" });

        var e = await _db.Expenses.FirstOrDefaultAsync(
            x => x.ExpenseId == expenseId && x.CustodyId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف مش موجود في العهدة دي" });
        if (e.Status == "Cancelled") return BadRequest(new { message = "المصروف ملغي — ما بيتربطش" });

        var isAdvance = await _db.ExpenseTypes.AsNoTracking()
            .Where(t => t.ExpenseTypeId == e.ExpenseTypeId)
            .Select(t => t.IsFreightAdvance).FirstOrDefaultAsync(ct);

        if (isAdvance)
        {
            if (req.TripId is null) return BadRequest(new { message = "نولون النقلة يجب أن يُربط برحلة" });
            var trip = await _db.Trips.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TripId == req.TripId && !t.IsDeleted, ct);
            if (trip is null) return BadRequest(new { message = "الرحلة مش موجودة" });
            e.TripId      = trip.TripId;
            e.DriverId    = trip.DriverId;
            e.OperationId = null;
        }
        else
        {
            if (req.OperationId is null) return BadRequest(new { message = "اختار العملية اللي المصروف عليها" });
            var op = await _db.Operations.AsNoTracking()
                .FirstOrDefaultAsync(o => o.OperationId == req.OperationId && !o.IsDeleted, ct);
            if (op is null) return BadRequest(new { message = "العملية مش موجودة" });
            if (op.Status == "Closed") return BadRequest(new { message = "العملية مقفولة — ما بيتربطش عليها مصروفات" });
            e.OperationId = op.OperationId;
        }

        e.UpdatedAt = DateTime.UtcNow;
        e.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "اتربط المصروف" });
    }

    /// <summary>عمليات مفتوحة لقائمة ربط التسوية — بصلاحية العهود.</summary>
    [HttpGet("operations")]
    [Authorize(Policy = "PERM:CUSTODY.VIEW")]
    public async Task<IActionResult> Operations(CancellationToken ct) =>
        Ok(await _db.Operations.AsNoTracking()
            .Where(o => !o.IsDeleted && o.Status != "Closed" && o.Status != "Cancelled")
            .OrderByDescending(o => o.OperationId)
            .Take(300)
            .Select(o => new OpOpt(o.OperationId, o.OperationNumber,
                o.Customer != null ? o.Customer.NameAr : "—", o.Status))
            .ToListAsync(ct));

    public record OpOpt(long OperationId, string OperationNumber, string CustomerName, string Status);

    // ═══════════════ TRIPS (دروبداون) ═══════════════

    /// <summary>الرحلات المتاحة لفتح عهدة عليها — مخصصة بصلاحية العهود.</summary>
    [HttpGet("trips")]
    [Authorize(Policy = "PERM:CUSTODY.VIEW")]
    public async Task<IActionResult> Trips(CancellationToken ct) =>
        Ok(await _db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status != "Cancelled")
            .OrderByDescending(t => t.TripId)
            .Take(200)
            .Select(t => new TripOpt(t.TripId, t.TripNumber, t.Status, t.DriverId, t.Driver.FullName))
            .ToListAsync(ct));

    /// <summary>موظفين للعهد — بصلاحية العهود أو الموظفين (المحاسب مامعاهوش EMPLOYEE.VIEW).</summary>
    [HttpGet("employees")]
    [Authorize]
    public async Task<IActionResult> Employees(CancellationToken ct)
    {
        var uid = CurrentUserId();
        if (!await _perms.HasAsync(uid, "CUSTODY.VIEW", ct) &&
            !await _perms.HasAsync(uid, "EMPLOYEE.VIEW", ct))
            return Forbid();

        return Ok(await _db.Employees.AsNoTracking()
            .Where(e => !e.IsDeleted && e.EmploymentStatus != "Terminated")
            .OrderBy(e => e.FullNameAr)
            .Select(e => new Opt(e.EmployeeId, e.FullNameAr))
            .ToListAsync(ct));
    }

    // ═══════════════ CREATE — صرف عهدة ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:CUSTODY.CREATE")]
    public async Task<IActionResult> Create([FromBody] CustodyCreate req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { message = "البيانات مش كاملة" });
        if (req.AmountIssued < 0) return BadRequest(new { message = "مبلغ العهدة مينفعش يكون سالب" });
        /* 🔴 كان hard-coded 10,000,000 — بقى من `CUSTODY.MAX_AMOUNT`.
           0 = من غير سقف. */
        var custodyMax = await _settings.GetDecimalAsync(SettingKeys.CustodyMaxAmount,
                                                       SettingDefaults.CustodyMaxAmount, ct);
        if (custodyMax > 0 && req.AmountIssued > custodyMax)
        {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Create, "Custody", null,
                description: $"محاولة إنشاء عهدة فوق السقف ({req.AmountIssued:N2} > {custodyMax:N2})", ct: ct);
            return BadRequest(new { message = $"أقصى مبلغ للعهدة {custodyMax:N2} — المطلوب {req.AmountIssued:N2}" });
        }
        if (req.OwnerId <= 0) return BadRequest(new { message = "اختار صاحب العهدة" });

        /* ═══ سير العمل الجديد (قرار المالك): العهدة لموظف فقط ═══
           لا عهدة لسائق، ولا عهدة على رحلة، ولا عهد فرعية/توزيعات —
           ما يُدفع للسائق يُسجل بند صرف على عهدة الموظف نفسه. */
        if (req.OwnerType != "Employee")
            return BadRequest(new { message = "العهدة تُصرف لموظف فقط — لا توجد عهدة باسم سائق" });
        if (req.TripId is not null)
            return BadRequest(new { message = "العهدة لا ترتبط برحلة — تُصرف عامة للموظف، ومصروفاتها تُربط بعملياتها عند التسوية" });
        if (!req.IsPrimary || req.ParentCustodyId is not null)
            return BadRequest(new { message = "لا توجد عهد فرعية — التوزيع على السائقين يُسجل كبند على عهدة الموظف نفسه" });
        if (req.AmountIssued <= 0)
            return BadRequest(new { message = "مبلغ العهدة لازم يكون أكبر من صفر" });

        if (!await _db.Employees.AnyAsync(e => e.EmployeeId == req.OwnerId && !e.IsDeleted, ct))
            return BadRequest(new { message = "الموظف مش موجود" });

        // عهدة واحدة غير مقفولة لكل موظف
        var dupGen = await _db.DriverCustodies.AsNoTracking().AnyAsync(x =>
            !x.IsDeleted && x.OwnerType == "Employee" &&
            x.OwnerId == req.OwnerId && x.Status != "Closed", ct);
        if (dupGen)
            return BadRequest(new { message = "الموظف ده عنده عهدة لسه ما اتصفّتش — يصفّيها الأول قبل ما يستلم واحدة تانية" });

        /* ═══ التمويل: خزينة الموظف الخاصة إجبارية ═══
           الصرف يخرج منها هي، وأي اختيار خزينة في الطلب بيتجاهل،
           وفشل الصرف منها يلغي إنشاء العهدة بالكامل. */
        var empBox = await EmployeeBoxAsync(req.OwnerId, ct);
        if (empBox is null)
            return BadRequest(new { message = "مفيش خزينة تشغيل مفتوحة باسم الموظف ده — اعمل صندوق واربطه بيه (المسؤول) وموّله بتحويل من الخزينة الرئيسية الأول" });
        if (empBox.CurrentBalance < req.AmountIssued)
            return BadRequest(new { message = $"رصيد خزينة الموظف «{empBox.NameAr}» {empBox.CurrentBalance:N2} مش كفاية لصرف عهدة {req.AmountIssued:N2} — حوّل له من الخزينة الرئيسية الأول" });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var c = new DriverCustody
        {
            CustodyNumber    = await _numbers.NextAsync("CUSTODY", ct),
            BranchId         = branchId.Value,
            TripId           = null,
            OwnerType        = "Employee",
            OwnerId          = req.OwnerId,
            CustodyDate      = Dt(req.CustodyDate),
            AmountIssued     = req.AmountIssued,
            Status           = "Open",
            Notes            = B(req.Notes),
            CreatedBy        = CurrentUserId(),
            IsPrimary        = true,
            ParentCustodyId  = null,
            AllocationType   = null
        };
        // 🔴 Atomicity: العهدة + سطر «صرف» في الدفتر — معاملة واحدة
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.DriverCustodies.Add(c);
            await _db.SaveChangesAsync(ct);

            // سطر «صرف» في الدفتر — للتوثيق بس، الأرقام بيحسبها الـ trigger من حركات الصرف والرد
            if (req.AmountIssued > 0)
            {
                _db.CustodyTransactions.Add(new CustodyTransaction
                {
                    CustodyId       = c.CustodyId,
                    TransactionType = "Issue",
                    Amount          = req.AmountIssued,
                    TransactionDate = c.CustodyDate,
                    Notes           = "صرف العهدة",
                    CreatedBy       = CurrentUserId()
                });
                await _db.SaveChangesAsync(ct);

                /* 🔴 الخزينة — صرف العهدة يخرج من **خزينة الموظف نفسه** فقط
                   (CashBoxId صريح)، وفشل الصرف يلغي إنشاء العهدة بالكامل:
                   عهدة من غير رجل خزينة معناها فلوس بره الدفاتر.
                   المصروفات المربوطة بالعهدة **مابتعملش** حركة خزينة تانية —
                   وإلا هتتحسب مرتين. */
                var paid = await _cash.PaymentAsync(new CashEntry(
                    ReferenceNumber : $"CUST-ISSUE:{c.CustodyNumber}",
                    Amount          : c.AmountIssued,
                    PaymentMethodId : await CashMethodIdAsync(ct) ?? 0,
                    CustodyId       : c.CustodyId,
                    Description     : $"صرف عهدة {c.CustodyNumber}",
                    Date            : DateOnly.FromDateTime(c.CustodyDate),
                    CashBoxId       : empBox.CashBoxId), ct);
                if (paid) await _db.SaveChangesAsync(ct);
                else throw new GeneralCustodyBoxException();
            }

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (GeneralCustodyBoxException)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            return BadRequest(new { message = "تعذّر صرف العهدة من خزينة الموظف — الإنشاء اتلغى بالكامل. راجع إن الصندوق مفتوح ونشط" });
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { id = c.CustodyId, number = c.CustodyNumber,
            message = $"اتفتحت العهدة برقم {c.CustodyNumber}" });
    }

    // ═══════════════ حركة — رد باقى / إضافة مبلغ ═══════════════

    [HttpPost("{id:long}/transactions")]
    [Authorize(Policy = "PERM:CUSTODY.SETTLE")]
    public async Task<IActionResult> AddTransaction(long id, [FromBody] TxRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { message = "البيانات مش كاملة" });
        if (req.Amount <= 0) return BadRequest(new { message = "المبلغ لازم يكون أكتر من صفر" });
        var txMax = await _settings.GetDecimalAsync(SettingKeys.CustodyMaxAmount,
                                                    SettingDefaults.CustodyMaxAmount, ct);
        if (txMax > 0 && req.Amount > txMax)
        {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Create, "Custody", null,
                description: $"محاولة تسجيل حركة عهدة فوق السقف ({req.Amount:N2} > {txMax:N2})", ct: ct);
            return BadRequest(new { message = $"أقصى مبلغ للحركة {txMax:N2} — المطلوب {req.Amount:N2}" });
        }
        if (req.Type is not ("TopUp" or "Refund"))
            return BadRequest(new { message = "نوع الحركة لازم يكون «رد باقي» أو «تغذية عهدة»" });

        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status is "Closed" or "Approved" or "Submitted")
            return BadRequest(new { message = "العهدة اتقدّمت للتسوية — مافيش حركات جديدة" });

        /* 🔴 حركات العهدة المالية (رد/تغذية) بتتم على **خزينة الموظف نفسه** —
           لو مش مفتوحة الحركة بتترفض قبل ما تتحجز في الدفتر. */
        CashBox? empBox = null;
        if (IsGeneralOps(c))
        {
            empBox = await EmployeeBoxAsync(c.OwnerId, ct);
            if (empBox is null)
                return BadRequest(new { message = "خزينة الموظف صاحب العهدة مش مفتوحة — حركات العهدة لازم تتم عليها. افتحها الأول" });
            if (req.Type == "TopUp" && empBox.CurrentBalance < req.Amount)
                return BadRequest(new { message = $"رصيد خزينة الموظف «{empBox.NameAr}» {empBox.CurrentBalance:N2} مش كفاية لتغذية {req.Amount:N2} — حوّل له من الخزينة الرئيسية الأول" });
        }
        else if (req.Type == "TopUp")
            return BadRequest(new { message = "التغذية متاحة لعهد الموظفين العامة بس" });

        if (req.Type == "Refund")
        {
            // CK_Custodies_NoOverRefund — مانرجّعش أكتر من المتاح
            var max = c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned;
            if (req.Amount > max)
                return BadRequest(new { message = $"المتاح للرد {max:N2} بس" });
        }

        if (req.ExpenseId is not null)
            return BadRequest(new { message =
                "ربط المصروفات بالعهدة بيتعمل تلقائيًا من شاشة المصروفات — حركة «رد باقي» مالهاش مصروف" });

        var tx = new CustodyTransaction
        {
            CustodyId       = c.CustodyId,
            TransactionType = req.Type,
            Amount          = req.Amount,
            TransactionDate = Dt(req.TxDate),
            ExpenseId       = req.ExpenseId,
            Notes           = B(req.Notes),
            CreatedBy       = CurrentUserId()
        };

        /* 🔴 الخزينة — **رد الباقي** = فلوس رجعت من صاحب العهدة للخزينة → إيصال،
           و**تغذية العهدة** = صرف فعلي من خزينة الموظف يزود مبلغ عهدته
           (تحل محل «إضافة مبلغ» الوهمية التي لم تكن تتحرك بها أي فلوس). */
        var toTreasury = false;

        if (req.Type == "TopUp" && empBox is not null)
        {
            /* 🔴 الترتيب مهم — `trg_CustodyTransactions_Sync` بيحسب `AdditionalDue`
               من `AmountIssued` **لحظة تسجيل الحركة**، والتريجر على `CustodyTransactions`
               فما بيشتغلش تاني عند تحديث صف العهدة. لو سجلنا الحركة الأول كان
               `AdditionalDue` بيتحسب على المبلغ القديم ويفضل غلط للأبد ⇒
               «الباقي» يطلع أكبر من حقه ويترد للموظف زيادة عند الإقفال.
               عشان كده: نزوّد `AmountIssued` الأول، وبعدين نسجل الحركة.
               والاتنين + حركة الخزينة جوه معاملة واحدة — أي فشل يلغي الكل. */
            await _db.Database.BeginTransactionAsync(ct);
            try
            {
                c.AmountIssued += tx.Amount;
                await _db.SaveChangesAsync(ct);

                _db.CustodyTransactions.Add(tx);
                await _db.SaveChangesAsync(ct);      // ← عشان ناخد `tx.CustodyTransactionId` + التريجر يحسب على المبلغ الجديد

                toTreasury = await _cash.PaymentAsync(new CashEntry(
                    ReferenceNumber : $"CUST-TOPUP:{c.CustodyNumber}:{tx.CustodyTransactionId}",
                    Amount          : tx.Amount,
                    PaymentMethodId : await CashMethodIdAsync(ct) ?? 0,
                    CustodyId       : c.CustodyId,
                    Description     : $"تغذية عهدة {c.CustodyNumber}",
                    Date            : DateOnly.FromDateTime(tx.TransactionDate),
                    CashBoxId       : empBox.CashBoxId), ct);
                if (!toTreasury)
                {
                    try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
                    return BadRequest(new { message = "تعذّر صرف التغذية من خزينة الموظف — اتلغت الحركة بالكامل" });
                }

                await _db.SaveChangesAsync(ct);      // ← حركة الخزينة (تريجر الرصيد بيحدّث `CurrentBalance`)
                await _db.Database.CommitTransactionAsync(ct);
            }
            catch (Exception)
            {
                try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
                throw;
            }
        }
        else
        {
            _db.CustodyTransactions.Add(tx);
            await _db.SaveChangesAsync(ct);          // ← عشان ناخد `tx.CustodyTransactionId`

            if (req.Type == "Refund")
            {
                toTreasury = await _cash.ReceiptAsync(new CashEntry(
                    ReferenceNumber : $"CUST-REFUND:{c.CustodyNumber}:{tx.CustodyTransactionId}",
                    Amount          : tx.Amount,
                    PaymentMethodId : await CashMethodIdAsync(ct) ?? 0,
                    CustodyId       : c.CustodyId,
                    Description     : $"رد باقي عهدة {c.CustodyNumber}",
                    Date            : DateOnly.FromDateTime(tx.TransactionDate),
                    CashBoxId       : empBox?.CashBoxId ?? 0), ct);
                if (!toTreasury)
                {
                    _db.CustodyTransactions.Remove(tx);
                    await _db.SaveChangesAsync(ct);
                    return BadRequest(new { message = "تعذّر تسجيل رد الباقي في الخزينة — اتلغت الحركة بالكامل" });
                }
                await _db.SaveChangesAsync(ct);
            }
        }

        var now = await _db.DriverCustodies.AsNoTracking()
            .FirstAsync(x => x.CustodyId == id, ct);

        return Ok(new
        {
            message = (req.Type == "Refund" ? "اتسجل رد الباقي" : "اتسجلت تغذية العهدة")
                      + (toTreasury ? " — واتسجلت في الخزينة" : ""),
            spent    = now.AmountSpent,
            returned = now.AmountReturned,
            due      = now.AdditionalDue,
            remaining = now.AmountIssued + now.AdditionalDue - now.AmountSpent - now.AmountReturned,
            status   = now.Status
        });
    }

    /// <summary>
    /// 🗑️ حذف حركة غلط من دفتر العهدة (رد/إضافة) — والـ trigger بيعيد حساب الأرصدة.
    /// حركات المصروفات بتتدار من شاشة المصروفات (ExpenseId → ممنوع الحذف من هنا).
    /// </summary>
    [HttpDelete("{id:long}/transactions/{txId:long}")]
    [Authorize(Policy = "PERM:CUSTODY.SETTLE")]
    public async Task<IActionResult> DeleteTransaction(long id, long txId, CancellationToken ct)
    {
        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status is not ("Open" or "PartiallySettled"))
            return BadRequest(new { message = "العهدة اتقدّمت للتسوية — مافيش حذف حركات" });

        var tx = await _db.CustodyTransactions
            .FirstOrDefaultAsync(t => t.CustodyTransactionId == txId && t.CustodyId == id, ct);
        if (tx is null) return NotFound(new { message = "الحركة مش موجودة" });

        if (tx.TransactionType == "Issue")
            return BadRequest(new { message = "حركة الصرف الأساسية بتتشال مع حذف العهدة نفسها" });
        if (tx.TransactionType == "Distribute")
            return BadRequest(new { message = "حركات التوزيع قديمة ملغاة — لا تُحذف من هنا" });
        if (tx.TransactionType == "TopUp")
        {
            /* حذف التغذية يعكسها بالكامل: إلغاء صرف الخزينة وإنقاص مبلغ العهدة */
            var topRef = $"CUST-TOPUP:{c.CustodyNumber}:{tx.CustodyTransactionId}";
            var topLegs = await _db.CashTransactions
                .Where(t => !t.IsDeleted && t.Status == "Posted" && t.ReferenceNumber == topRef)
                .ToListAsync(ct);
            foreach (var leg in topLegs) leg.Status = "Void";
            c.AmountIssued -= tx.Amount;
        }
        if (tx.ExpenseId is not null)
            return BadRequest(new { message = "الحركة دي مربوطة بمصروف — الغِها أو عدّلها من شاشة المصروفات" });

        /* 🔴 حذف حركة «رد باقي» لازم يُلغي إيصال الخزينة المقابل في نفس الحفظ —
           وإلا الخزينة بتفضل حاسبة فلوس الدفتر بيقول إنها ما رجعتش.
           المطابقة بالمرجع الكامل بالضبط (مش prefix) عشان أرقام حركات متقاربة. */
        if (tx.TransactionType == "Refund")
        {
            var refundRef = $"CUST-REFUND:{c.CustodyNumber}:{tx.CustodyTransactionId}";
            var legs = await _db.CashTransactions
                .Where(t => !t.IsDeleted && t.Status == "Posted" && t.ReferenceNumber == refundRef)
                .ToListAsync(ct);
            foreach (var leg in legs) leg.Status = "Void";
        }

        _db.CustodyTransactions.Remove(tx);
        await _db.SaveChangesAsync(ct);

        var now = await _db.DriverCustodies.AsNoTracking()
            .FirstAsync(x => x.CustodyId == id, ct);

        return Ok(new
        {
            message = "اتحذفت الحركة والأرصدة اتظبطت",
            spent    = now.AmountSpent,
            returned = now.AmountReturned,
            due      = now.AdditionalDue,
            remaining = now.AmountIssued + now.AdditionalDue - now.AmountSpent - now.AmountReturned,
            status   = now.Status
        });
    }

    // ═══════════════ تقديم التسوية ═══════════════

    [HttpPost("{id:long}/settle")]
    [Authorize(Policy = "PERM:CUSTODY.SETTLE")]
    public async Task<IActionResult> Settle(long id, CancellationToken ct)
    {
        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status is not ("Open" or "PartiallySettled"))
        {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Settle, "Custody", c.CustodyId.ToString(),
                description: $"محاولة تسوية عهدة بحالة {c.Status}", ct: ct);
            return BadRequest(new { message = $"العهدة حالتها {StatusAr(c.Status)} — التسوية بتتقدم من عهدة مفتوحة بس" });
        }

        var moved = await _db.CustodyTransactions.AnyAsync(
            t => t.CustodyId == id && t.TransactionType != "Issue", ct);
        if (!moved)
            return BadRequest(new { message = "مافيش حركات مسجلة — سجّل المصروفات والرد الأول" });

        /* 🔴 عهدة تشغيل عامة: التسوية ما تتقدّمش وفي مصروفات مش مربوطة —
           كل مصروف لازم يبقى على عملية، وسلفة النولون لازم تبقى على رحلة.
           الربط بيحصل من شاشة العهدة (أو من تعديل المصروف) قبل التقديم. */
        if (IsGeneralOps(c))
        {
            var unlinked = await _db.Expenses.AsNoTracking()
                .CountAsync(e => e.CustodyId == id && !e.IsDeleted && e.Status != "Cancelled"
                                 && e.OperationId == null
                                 && !(e.ExpenseType.IsFreightAdvance && e.TripId != null), ct);
            if (unlinked > 0)
                return BadRequest(new { message = $"في {unlinked} مصروف لسه مش مربوط — اربط كل مصروف بعمليته (ونولون النقلة برحلتها) من تفاصيل العهدة الأول" });
        }

        c.Status    = "Submitted";
        c.SettledBy = CurrentUserId();
        c.UpdatedAt = DateTime.UtcNow;
        c.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        /* إشعار لمن يملكون صلاحية الاعتماد — التقديم بلا إشعار كان يجعل
           التسوية تنتظر من يكتشفها بالصدفة */
        var deficitNote = c.AmountSpent + c.AmountReturned - c.AmountIssued;
        var remainingNote = c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned;
        await NotifyPermissionHoldersAsync("CUSTODY.APPROVE",
            $"تسوية عهدة بانتظار الاعتماد — {c.CustodyNumber}",
            deficitNote > 0
                ? $"قُدّمت تسوية العهدة {c.CustodyNumber} وبها عجز مستحق للموظف قدره {deficitNote:N2} ج.م سيُصرف له عند الاعتماد."
                : remainingNote > 0
                    ? $"قُدّمت تسوية العهدة {c.CustodyNumber} وبها باقٍ قدره {remainingNote:N2} ج.م سيُرد تلقائيًا إلى خزينة الموظف عند الاعتماد."
                    : $"قُدّمت تسوية العهدة {c.CustodyNumber} وهي بانتظار اعتمادك.",
            c, deficitNote > 0 ? "High" : "Normal", ct);

        return Ok(new { message = "اتقدّمت التسوية — مستنية الاعتماد" });
    }

    // ═══════════════ اعتماد التسوية ═══════════════

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = "PERM:CUSTODY.APPROVE")]
    public async Task<IActionResult> Approve(long id, [FromBody] ApproveRequest? req, CancellationToken ct)
    {
        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status != "Submitted")
        {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Approve, "Custody", c.CustodyId.ToString(),
                description: $"محاولة اعتماد عهدة بحالة {c.Status}", ct: ct);
            return BadRequest(new { message = $"العهدة حالتها {StatusAr(c.Status)} — الاعتماد بيتم على تسوية متقدّمة بس" });
        }

        /* فصل المهام (قرار المالك): الاعتماد من الإدارة، ومن قدّم التسوية
           لا يعتمدها بنفسه.
           🔴 استثناء واحد — **مدير النظام** (`ADMIN`): هو اللي بيمضي في آخر
           المطاف، فله يقدّم التسوية ويعتمدها هو نفسه، حتى لو كان هو مقدّمها.
           أي حد تاني — حتى لو كان يملك صلاحية الاعتماد — مرفوض لو كان هو
           اللي قدّمها. نفس الباس المستخدم في `ExpensesController:205` و
           `PermissionAuthorization:55` (ADMIN بيتجاوز كل الصلاحيات أصلًا). */
        if (!User.IsInRole("ADMIN") && c.SettledBy is not null && c.SettledBy == CurrentUserId())
            return BadRequest(new { message = "لا يمكن اعتماد تسوية قمت بتقديمها بنفسك — الاعتماد يتم من مدير النظام أو من مستخدم آخر يملك صلاحية الاعتماد" });

        /* العجز الحقيقي = ما صُرف ورُد فوق ما استلمه الموظف فعلًا.
           ده مال دفعه الموظف من جيبه لصالح الشغل — واعتماده عند المالك
           يُعالج بـ«الاستعاضة»: يُصرف له العجز نقدًا مع الاعتماد نفسه. */
        var deficit = c.AmountSpent + c.AmountReturned - c.AmountIssued;
        if (deficit > 0 && req?.AddAdditional != true)
            return BadRequest(new
            {
                message = $"الموظف صرف من ماله الخاص {deficit:N2} ج.م فوق مبلغ العهدة — علّم «صرف الاستعاضة والاعتماد» لصرفها له مع الاعتماد"
            });

        if (deficit > 0)
        {
            var empBox = await EmployeeBoxAsync(c.OwnerId, ct);
            if (empBox is null)
                return BadRequest(new { message = "خزينة الموظف صاحب العهدة مش مفتوحة — الاستعاضة لازم تتصرف منها. افتحها الأول" });
            if (empBox.CurrentBalance < deficit)
                return BadRequest(new { message = $"رصيد خزينة الموظف «{empBox.NameAr}» {empBox.CurrentBalance:N2} مش كفاية لصرف استعاضة {deficit:N2} — موّل الخزينة الأول" });

            await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var paidBack = await _cash.PaymentAsync(new CashEntry(
                    ReferenceNumber : $"CUST-REIMB:{c.CustodyNumber}",
                    Amount          : deficit,
                    PaymentMethodId : await CashMethodIdAsync(ct) ?? 0,
                    CustodyId       : c.CustodyId,
                    Description     : $"استعاضة عجز عهدة {c.CustodyNumber}",
                    Date            : DateOnly.FromDateTime(DateTime.Today),
                    CashBoxId       : empBox.CashBoxId), ct);
                if (!paidBack) throw new GeneralCustodyBoxException();
                await _db.SaveChangesAsync(ct);

                /* 🔴 #88 — الاستعاضة فلوس **من الشركة للموظف** ⇒ تُعامل زي التغذية:
                   نزوّد `AmountIssued` بيها **قبل** تسجيل الحركة، عشان
                   `trg_CustodyTransactions_Sync` يحسب `AdditionalDue` على المبلغ الجديد
                   ويطلع **صفر** بعد السداد. من غير كده العمود كان بيفضل يقول
                   «مستحق للموظف» للأبد مع إننا دفعناه — ومفيش حاجة بتمنع الدفع مرتين.
                   ⚠️ ما تدخلش في مجموع `Refund`: ده كان بيضاعف `deficit`
                   (المنصرف + المرتجع − المستلم) بدل ما يصفّره. */
                c.AmountIssued += deficit;
                await _db.SaveChangesAsync(ct);

                _db.CustodyTransactions.Add(new CustodyTransaction
                {
                    CustodyId       = c.CustodyId,
                    TransactionType = "Reimburse",
                    Amount          = deficit,
                    TransactionDate = DateTime.Today,
                    Notes           = "استعاضة عجز — صُرفت للموظف عند الاعتماد",
                    CreatedBy       = CurrentUserId()
                });

                c.Status     = "Approved";
                c.ApprovedBy = CurrentUserId();
                c.UpdatedAt  = DateTime.UtcNow;
                c.UpdatedBy  = CurrentUserId();
                await _db.SaveChangesAsync(ct);
                await _db.Database.CommitTransactionAsync(ct);
            }
            catch (GeneralCustodyBoxException)
            {
                try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
                return BadRequest(new { message = "تعذّر صرف الاستعاضة من خزينة الموظف — الاعتماد اتلغى بالكامل" });
            }
            catch (Exception)
            {
                try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
                throw;
            }

            await NotifyUserAsync(c.SettledBy,
                $"اعتمدت تسوية العهدة {c.CustodyNumber}",
                $"اعتمدت تسوية عهدتك {c.CustodyNumber} وصُرفت لك استعاضة عجز قدرها {deficit:N2} ج.م من خزينتك.",
                c, "Normal", ct);

            return Ok(new { message = $"اعتمدت التسوية — وصُرفت استعاضة {deficit:N2} ج.م للموظف من خزينته" });
        }

        /* 🔴 رد الباقي تلقائيًا عند الاعتماد (قرار المالك): ما تبقّى مع الموظف
           يرجع لخزينته داخل نفس معاملة الاعتماد، فيخرج الاعتماد والباقي صفر —
           ولا يعود الإغلاق محبوسًا بباقٍ لا يمكن ردّه بعد الاعتماد. */
        var remaining = c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned;
        if (remaining > 0 && IsGeneralOps(c))
        {
            var empBox = await EmployeeBoxAsync(c.OwnerId, ct);
            if (empBox is null)
                return BadRequest(new { message = "خزينة الموظف صاحب العهدة مش مفتوحة — رد الباقي التلقائي بيتم عليها. افتحها الأول" });

            decimal refunded = 0;
            await _db.Database.BeginTransactionAsync(ct);
            try
            {
                refunded = await AutoRefundRemainderAsync(c, empBox, "Approve", ct);
                c.Status     = "Approved";
                c.ApprovedBy = CurrentUserId();
                c.UpdatedAt  = DateTime.UtcNow;
                c.UpdatedBy  = CurrentUserId();
                await _db.SaveChangesAsync(ct);
                await _db.Database.CommitTransactionAsync(ct);
            }
            catch (GeneralCustodyBoxException)
            {
                try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
                return BadRequest(new { message = "تعذّر رد الباقي لخزينة الموظف — الاعتماد اتلغى بالكامل" });
            }
            catch (Exception)
            {
                try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
                throw;
            }

            await NotifyUserAsync(c.SettledBy,
                $"اعتمدت تسوية العهدة {c.CustodyNumber}",
                $"اعتمدت تسوية عهدتك {c.CustodyNumber} ورُد باقي {refunded:N2} ج.م تلقائيًا إلى خزينتك.",
                c, "Normal", ct);

            return Ok(new { message = $"اعتمدت التسوية — ورُد باقي {refunded:N2} ج.م تلقائيًا إلى خزينة الموظف" });
        }

        c.Status     = "Approved";
        c.ApprovedBy = CurrentUserId();
        c.UpdatedAt  = DateTime.UtcNow;
        c.UpdatedBy  = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        await NotifyUserAsync(c.SettledBy,
            $"اعتمدت تسوية العهدة {c.CustodyNumber}",
            $"اعتمدت تسوية عهدتك {c.CustodyNumber} — جاهزة للإغلاق.",
            c, "Normal", ct);

        return Ok(new { message = "اعتمدت التسوية — جاهزة للإغلاق" });
    }

    // ═══════════════ إرجاع التسوية (رفض الاعتماد) ═══════════════

    public record ReturnRequest(string? Reason);

    [HttpPost("{id:long}/return")]
    [Authorize(Policy = "PERM:CUSTODY.APPROVE")]
    public async Task<IActionResult> ReturnSettlement(long id, [FromBody] ReturnRequest? req, CancellationToken ct)
    {
        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status != "Submitted")
            return BadRequest(new { message = $"العهدة حالتها {StatusAr(c.Status)} — الإرجاع بيتم على تسوية متقدّمة بس" });

        var settler = c.SettledBy;
        var reason  = B(req?.Reason);

        c.Status    = "Open";
        c.SettledBy = null;
        c.UpdatedAt = DateTime.UtcNow;
        c.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Update, "Custody", c.CustodyId.ToString(),
            description: $"إرجاع تسوية العهدة {c.CustodyNumber}" + (reason is null ? "" : $" — السبب: {reason}"), ct: ct);

        await NotifyUserAsync(settler,
            $"أُرجعت تسوية العهدة {c.CustodyNumber}",
            reason is null
                ? $"أُرجعت تسوية عهدتك {c.CustodyNumber} لاستكمالها — راجع البنود وقدّمها من جديد."
                : $"أُرجعت تسوية عهدتك {c.CustodyNumber} — السبب: {reason}",
            c, "High", ct);

        return Ok(new { message = "أُرجعت التسوية لصاحب العهدة لاستكمالها" });
    }

    // ═══════════════ إشعارات سير العمل ═══════════════

    /* مستخدمو صلاحية معينة — بنفس منطق تجميع الصلاحيات في TokenService
       (أدوار + منح صريح − منع صريح) */
    private async Task NotifyPermissionHoldersAsync(string permissionCode, string title, string message,
        DriverCustody c, string priority, CancellationToken ct)
    {
        var ids = await _db.Database.SqlQuery<int>($"""
            WITH Perm AS (SELECT PermissionId FROM AppPermissions WHERE PermissionCode = {permissionCode}),
            FromRoles AS (SELECT DISTINCT ur.UserId FROM AspNetUserRoles ur
                          JOIN AppRolePermissions arp ON arp.RoleId = ur.RoleId
                          WHERE arp.PermissionId IN (SELECT PermissionId FROM Perm)),
            Granted AS (SELECT UserId FROM AppUserPermissions
                        WHERE PermissionId IN (SELECT PermissionId FROM Perm) AND IsGranted = 1),
            Denied  AS (SELECT UserId FROM AppUserPermissions
                        WHERE PermissionId IN (SELECT PermissionId FROM Perm) AND IsGranted = 0)
            SELECT UserId FROM FromRoles
            UNION SELECT UserId FROM Granted
            EXCEPT SELECT UserId FROM Denied
            """).ToListAsync(ct);

        var me = CurrentUserId();
        foreach (var uid in ids.Where(u => u != me))
            _db.Notifications.Add(new Notification
            {
                UserId           = uid,
                BranchId         = c.BranchId,
                NotificationType = "Custody",
                Title            = title,
                Message          = message,
                EntityType       = "Custody",
                EntityId         = c.CustodyId,
                Priority         = priority,
                IsRead           = false,
                CreatedAt        = DateTime.UtcNow
            });
        if (ids.Any(u => u != me)) await _db.SaveChangesAsync(ct);
    }

    private async Task NotifyUserAsync(int? userId, string title, string message,
        DriverCustody c, string priority, CancellationToken ct)
    {
        if (userId is null || userId == CurrentUserId()) return;
        _db.Notifications.Add(new Notification
        {
            UserId           = userId.Value,
            BranchId         = c.BranchId,
            NotificationType = "Custody",
            Title            = title,
            Message          = message,
            EntityType       = "Custody",
            EntityId         = c.CustodyId,
            Priority         = priority,
            IsRead           = false,
            CreatedAt        = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    // ═══════════════ إغلاق ═══════════════

    [HttpPost("{id:long}/close")]
    [Authorize(Policy = "PERM:CUSTODY.CLOSE")]
    public async Task<IActionResult> Close(long id, CancellationToken ct)
    {
        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status != "Approved")
        {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Close, "Custody", c.CustodyId.ToString(),
                description: $"محاولة إغلاق عهدة بحالة {c.Status}", ct: ct);
            return BadRequest(new { message = $"العهدة حالتها {StatusAr(c.Status)} — الإغلاق بيتم بعد الاعتماد بس" });
        }

        /* 🔴 شبكة أمان (قرار المالك): عهدة معتمدة وبها باقٍ — بدل رفض الإغلاق
           برسالة تطلب ردًّا مستحيلًا بعد الاعتماد (الحركات مقفولة)، يُرد الباقي
           تلقائيًا لخزينة الموظف داخل معاملة الإغلاق نفسها ثم تُقفل العهدة. */
        var remaining = c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned;
        if (remaining > 0 && IsGeneralOps(c))
        {
            var empBox = await EmployeeBoxAsync(c.OwnerId, ct);
            if (empBox is null)
                return BadRequest(new { message = "خزينة الموظف صاحب العهدة مش مفتوحة — رد الباقي التلقائي بيتم عليها. افتحها الأول" });

            decimal refunded = 0;
            await _db.Database.BeginTransactionAsync(ct);
            try
            {
                refunded = await AutoRefundRemainderAsync(c, empBox, "Close", ct);
                c.Status    = "Closed";
                c.ClosedAt  = DateTime.UtcNow;
                c.ClosedBy  = CurrentUserId();
                c.UpdatedAt = DateTime.UtcNow;
                c.UpdatedBy = CurrentUserId();
                await _db.SaveChangesAsync(ct);
                await _db.Database.CommitTransactionAsync(ct);
            }
            catch (GeneralCustodyBoxException)
            {
                try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
                return BadRequest(new { message = "تعذّر رد الباقي لخزينة الموظف — الإغلاق اتلغى بالكامل" });
            }
            catch (Exception)
            {
                try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
                throw;
            }

            return Ok(new { message = $"اتقفلت العهدة — ورُد باقي {refunded:N2} ج.م تلقائيًا إلى خزينة الموظف" });
        }
        if (remaining > 0)
        {
            await _audit.LogAsync(FastCom.Server.Services.AuditActions.Close, "Custody", c.CustodyId.ToString(),
                description: $"محاولة إغلاق عهدة وباقي {remaining:N2} لسه مااتردش", ct: ct);
            return BadRequest(new { message = $"فاضل مع صاحب العهدة {remaining:N2} — سجّلها مصروفات أو رد باقي الأول" });
        }

        c.Status    = "Closed";
        c.ClosedAt  = DateTime.UtcNow;
        c.ClosedBy  = CurrentUserId();
        c.UpdatedAt = DateTime.UtcNow;
        c.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "اتقفلت العهدة" });
    }

    // ═══════════════ حذف ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:CUSTODY.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status != "Open")
            return BadRequest(new { message = "طلب غير صالح" });

        if (await _db.Expenses.AnyAsync(e => e.CustodyId == id && !e.IsDeleted, ct))
            return BadRequest(new { message = "في مصروفات مربوطة بالعهدة دي — الغِها الأول" });

        if (await _db.CashTransactions.AnyAsync(t => t.CustodyId == id && !t.IsDeleted, ct))
            return BadRequest(new { message = "في حركات خزينة مربوطة بالعهدة دي" });

        // لو اتفتحت بمبلغ، هيبقى فيها سطر «صرف» واحد بس — ده بيتشال مع العهدة
        var issueOnly = await _db.CustodyTransactions
            .Where(t => t.CustodyId == id && t.TransactionType != "Issue")
            .AnyAsync(ct);
        if (issueOnly) return BadRequest(new { message = "في حركات مسجلة على العهدة — ماتتحذفش" });

        var issues = await _db.CustodyTransactions
            .Where(t => t.CustodyId == id).ToListAsync(ct);
        _db.CustodyTransactions.RemoveRange(issues);

        /* 🔴 لازم حركة الخزينة بتاعة الصرف تتلغى — وإلا الرصيد يفضل ناقص
           على عهدة اتمسحت. (العهدة هنا `Open` بس، فمافيش رد باقي حصل.) */
        await _cash.VoidByReferenceAsync($"CUST-ISSUE:{c.CustodyNumber}", ct);

        /* #63 — عهدة فرعية اتحذفت → نشيل حركة «التوزيع» المقابلة من دفتر الموظف
           (الـ trigger هيعيد حساب أرصدته) */
        if (c.ParentCustodyId is not null)
        {
            var distTx = await _db.CustodyTransactions
                .FirstOrDefaultAsync(t => t.CustodyId == c.ParentCustodyId &&
                                          t.TransactionType == "Distribute" &&
                                          t.Notes == $"توزيع لسائق — {c.CustodyNumber}", ct);
            if (distTx is not null) _db.CustodyTransactions.Remove(distTx);
        }

        c.IsDeleted = true;
        c.DeletedAt = DateTime.UtcNow;
        c.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "اتحذفت العهدة" });
    }

    // ═══════════════ #88 — طلبات تمويل العهدة ═══════════════
    /* الموظف بيطلب مبلغ زيادة على عهدته ⇒ إشعار لمدير النظام ⇒ يوافق أو يرفض.
       الطلب **صف في جدول** مش إشعار: عشان يبقى فيه طابور منتظر، ومبلغ مطلوب ومعتمد،
       ومين طلب ومين قرر، و**قفل ضد الموافقة مرتين** (`TopUpTransactionId` unique).
       الإشعار هو الجرس بس. */

    public record FundRequestBody(decimal Amount, string? Reason);
    public record FundDecisionBody(decimal? ApprovedAmount, string? Notes);

    public record FundRequestItem(
        long FundRequestId, long CustodyId, string CustodyNumber, string OwnerName,
        decimal CustodyIssued, decimal CustodyRemaining, string CustodyStatus,
        decimal RequestedAmount, string? Reason, string Status, decimal? ApprovedAmount,
        int RequestedBy, string RequestedByName, DateTime RequestedAt,
        int? DecidedBy, string? DecidedByName, DateTime? DecidedAt, string? DecisionNotes,
        long? TopUpTransactionId);

    /// <summary>الخزينة الرئيسية (الافتراضية من الإعداد) — مصدر تمويل طلب الموظف.</summary>
    private async Task<CashBox?> MainBoxAsync(CancellationToken ct)
    {
        var code = await _settings.GetStringAsync(SettingKeys.TreasuryDefaultBox,
                                                  SettingDefaults.TreasuryDefaultBox, ct);
        if (string.IsNullOrWhiteSpace(code)) code = SettingDefaults.TreasuryDefaultBox;
        return await _db.CashBoxes.AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive && b.Status == "Open" && b.Code == code)
            .FirstOrDefaultAsync(ct);
    }

    private static string FundStatusAr(string? s) => s switch
    {
        "Pending"   => "منتظر",
        "Approved"  => "معتمد",
        "Rejected"  => "مرفوض",
        "Cancelled" => "ملغي",
        _           => s ?? "غير معروفة"
    };

    /* 🔴 قاموس أسماء المستخدمين — نفس أسلوب AuditController/Dashboard:
       الـ join المباشر على AspNetUsers طلّع «النظام» لكل الصفوف على الداتابيز الحقيقية. */
    private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i > 0).Distinct().ToList();
        var map  = new Dictionary<int, string>();
        if (list.Count == 0) return map;

        var us = await _db.Users.AsNoTracking()
            .Where(u => list.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToListAsync(ct);
        foreach (var u in us)
            map[u.Id] = string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? "") : u.FullName;
        return map;
    }

    private async Task<List<FundRequestItem>> ProjectFundAsync(IQueryable<CustodyFundRequest> q, CancellationToken ct)
    {
        var rows = await q
            .OrderByDescending(x => x.FundRequestId)
            .Select(x => new
            {
                x.FundRequestId, x.CustodyId, x.RequestedAmount, x.Reason, x.Status,
                x.ApprovedAmount, x.RequestedBy, x.RequestedAt, x.DecidedBy, x.DecidedAt,
                x.DecisionNotes, x.TopUpTransactionId,
                CustodyNumber   = x.Custody.CustodyNumber,
                CustodyStatus   = x.Custody.Status,
                CustodyIssued   = x.Custody.AmountIssued,
                CustodySpent    = x.Custody.AmountSpent,
                CustodyReturned = x.Custody.AmountReturned,
                CustodyDue      = x.Custody.AdditionalDue,
                OwnerId         = x.Custody.OwnerId
            })
            .ToListAsync(ct);

        var ownerIds = rows.Select(r => r.OwnerId).Distinct().ToList();
        var ownerName = (await _db.Employees.AsNoTracking()
            .Where(e => ownerIds.Contains(e.EmployeeId))
            .Select(e => new { e.EmployeeId, e.FullNameAr })
            .ToListAsync(ct)).ToDictionary(e => e.EmployeeId, e => e.FullNameAr);

        var names = await UserNamesAsync(
            rows.Select(r => r.RequestedBy)
                .Concat(rows.Where(r => r.DecidedBy is not null).Select(r => r.DecidedBy!.Value)), ct);

        return rows.Select(r => new FundRequestItem(
            r.FundRequestId, r.CustodyId, r.CustodyNumber,
            ownerName.TryGetValue(r.OwnerId, out var on) ? on : "",
            r.CustodyIssued,
            r.CustodyIssued + r.CustodyDue - r.CustodySpent - r.CustodyReturned,
            r.CustodyStatus,
            r.RequestedAmount, r.Reason, r.Status, r.ApprovedAmount,
            r.RequestedBy,
            names.TryGetValue(r.RequestedBy, out var rn) ? rn : "",
            r.RequestedAt, r.DecidedBy,
            r.DecidedBy is not null && names.TryGetValue(r.DecidedBy.Value, out var dn) ? dn : null,
            r.DecidedAt, r.DecisionNotes, r.TopUpTransactionId)).ToList();
    }

    /// <summary>طابور الطلبات المنتظرة — لمدير النظام.</summary>
    [HttpGet("fund-requests/pending")]
    [Authorize(Policy = "PERM:CUSTODY.APPROVE")]
    public async Task<IActionResult> PendingFundRequests(CancellationToken ct) =>
        Ok(await ProjectFundAsync(
            _db.CustodyFundRequests.AsNoTracking().Where(x => !x.IsDeleted && x.Status == "Pending"), ct));

    /// <summary>سجل طلبات عهدة معيّنة.</summary>
    [HttpGet("{id:long}/fund-requests")]
    [Authorize(Policy = "PERM:CUSTODY.VIEW")]
    public async Task<IActionResult> CustodyFundRequests(long id, CancellationToken ct) =>
        Ok(await ProjectFundAsync(
            _db.CustodyFundRequests.AsNoTracking().Where(x => !x.IsDeleted && x.CustodyId == id), ct));

    // ─────────────── الموظف بيطلب ───────────────

    [HttpPost("{id:long}/fund-requests")]
    [Authorize(Policy = "PERM:CUSTODY.SETTLE")]
    public async Task<IActionResult> CreateFundRequest(long id, [FromBody] FundRequestBody? req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { message = "البيانات مش كاملة" });
        if (req.Amount <= 0) return BadRequest(new { message = "المبلغ المطلوب لازم يكون أكبر من صفر" });

        var custodyMax = await _settings.GetDecimalAsync(SettingKeys.CustodyMaxAmount,
                                                         SettingDefaults.CustodyMaxAmount, ct);
        if (custodyMax > 0 && req.Amount > custodyMax)
            return BadRequest(new { message = $"أقصى مبلغ مسموح {custodyMax:N2} — المطلوب {req.Amount:N2}" });

        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status is not ("Open" or "PartiallySettled"))
            return BadRequest(new { message = $"حالة العهدة {StatusAr(c.Status)} — الطلب بيتقدم على عهدة مفتوحة بس" });

        /* طلب منتظر واحد بس لكل عهدة — منع الإغراق وتكرار التمويل */
        if (await _db.CustodyFundRequests.AnyAsync(
                x => !x.IsDeleted && x.CustodyId == id && x.Status == "Pending", ct))
            return BadRequest(new { message = "في طلب تمويل لسه منتظر على العهدة دي — استنى القرار الأول" });

        var fr = new CustodyFundRequest
        {
            CustodyId       = c.CustodyId,
            RequestedAmount = req.Amount,
            Reason          = B(req.Reason),
            Status          = "Pending",
            RequestedBy     = CurrentUserId(),
            RequestedAt     = DateTime.UtcNow,
            CreatedAt       = DateTime.UtcNow
        };
        _db.CustodyFundRequests.Add(fr);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Create, "CustodyFundRequest",
            fr.FundRequestId.ToString(), description: $"طلب تمويل {req.Amount:N2} على العهدة {c.CustodyNumber}", ct: ct);

        await NotifyPermissionHoldersAsync("CUSTODY.APPROVE",
            $"طلب تمويل عهدة — {c.CustodyNumber}",
            $"طلب تمويل بمبلغ {req.Amount:N2} ج.م على عهدة {c.CustodyNumber}"
            + (string.IsNullOrWhiteSpace(req.Reason) ? "" : $" — السبب: {B(req.Reason)}"),
            c, "High", ct);

        return Ok(new { message = $"اتسجل الطلب بمبلغ {req.Amount:N2} — مستني موافقة مدير النظام" });
    }

    /// <summary>الموظف يلغي طلبه (ما دام لسه منتظر).</summary>
    [HttpPost("fund-requests/{rid:long}/cancel")]
    [Authorize(Policy = "PERM:CUSTODY.SETTLE")]
    public async Task<IActionResult> CancelFundRequest(long rid, CancellationToken ct)
    {
        var fr = await _db.CustodyFundRequests.FirstOrDefaultAsync(x => x.FundRequestId == rid && !x.IsDeleted, ct);
        if (fr is null) return NotFound(new { message = "الطلب مش موجود" });
        if (fr.Status != "Pending")
            return BadRequest(new { message = $"الطلب حالته {FundStatusAr(fr.Status)} — الإلغاء على طلب منتظر بس" });

        fr.Status    = "Cancelled";
        fr.DecidedBy = CurrentUserId();
        fr.DecidedAt = DateTime.UtcNow;
        fr.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Update, "CustodyFundRequest",
            fr.FundRequestId.ToString(), description: "إلغاء طلب تمويل من صاحبه", ct: ct);

        return Ok(new { message = "اتلغى الطلب" });
    }

    // ─────────────── مدير النظام بيقرر ───────────────

    /* الموافقة = **تحويل من الرئيسية لصندوق الموظف + تغذية العهدة** في معاملة واحدة.
       🔴 التحويل لوحده من غير تغذية كان هيخلي `AmountIssued` أقل من المنصرف ⇒
       عند الاعتماد `deficit` يطلع موجب والنظام **يصرف له نفس المبلغ تاني** استعاضة.
       🔴 والترتيب مهم زي `AddTransaction`: نزوّد `AmountIssued` الأول وبعدين نسجل
       الحركة، عشان `trg_CustodyTransactions_Sync` يحسب `AdditionalDue` على المبلغ الجديد. */
    [HttpPost("fund-requests/{rid:long}/approve")]
    [Authorize(Policy = "PERM:CUSTODY.APPROVE")]
    public async Task<IActionResult> ApproveFundRequest(long rid, [FromBody] FundDecisionBody? req, CancellationToken ct)
    {
        var fr = await _db.CustodyFundRequests.FirstOrDefaultAsync(x => x.FundRequestId == rid && !x.IsDeleted, ct);
        if (fr is null) return NotFound(new { message = "الطلب مش موجود" });
        if (fr.Status != "Pending")
            return BadRequest(new { message = $"الطلب حالته {FundStatusAr(fr.Status)} — الموافقة على طلب منتظر بس" });
        if (fr.TopUpTransactionId is not null)
            return BadRequest(new { message = "الطلب ده اتوافق عليه واتموّل قبل كده" });

        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == fr.CustodyId && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status is not ("Open" or "PartiallySettled"))
            return BadRequest(new { message = $"حالة العهدة {StatusAr(c.Status)} — مافيش تمويل إلا على عهدة مفتوحة" });

        var amount = req?.ApprovedAmount is > 0 ? req.ApprovedAmount.Value : fr.RequestedAmount;
        if (amount <= 0) return BadRequest(new { message = "المبلغ المعتمد لازم يكون أكبر من صفر" });

        var custodyMax = await _settings.GetDecimalAsync(SettingKeys.CustodyMaxAmount,
                                                         SettingDefaults.CustodyMaxAmount, ct);
        if (custodyMax > 0 && amount > custodyMax)
            return BadRequest(new { message = $"أقصى مبلغ مسموح {custodyMax:N2} — المعتمد {amount:N2}" });

        var empBox = await EmployeeBoxAsync(c.OwnerId, ct);
        if (empBox is null)
            return BadRequest(new { message = "مفيش خزينة تشغيل مفتوحة باسم الموظف صاحب العهدة — افتحها الأول" });

        var mainBox = await MainBoxAsync(ct);
        if (mainBox is null)
            return BadRequest(new { message = "الخزينة الرئيسية مش موجودة أو مقفولة" });
        if (mainBox.CurrentBalance < amount)
            return BadRequest(new { message = $"رصيد الخزينة الرئيسية {mainBox.CurrentBalance:N2} مش كفاية لتحويل {amount:N2}" });

        decimal funded;
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            /* 1) تحويل الرئيسية → صندوق الموظف (حركتين مربوطين بمعرّف واحد) */
            var group = Guid.NewGuid();
            var when  = DateTime.Now;
            var desc  = $"تمويل عهدة {c.CustodyNumber} — طلب رقم {fr.FundRequestId}";
            var uid   = CurrentUserId();
            _db.CashTransactions.AddRange(
                new CashTransaction
                {
                    BranchId = c.BranchId, CashBoxId = mainBox.CashBoxId, TransactionDate = when,
                    TransactionType = "TransferOut", Amount = amount,
                    TransferGroupId = group, CounterpartCashBoxId = empBox.CashBoxId,
                    Description = desc, Status = "Posted", CreatedBy = uid
                },
                new CashTransaction
                {
                    BranchId = c.BranchId, CashBoxId = empBox.CashBoxId, TransactionDate = when,
                    TransactionType = "TransferIn", Amount = amount,
                    TransferGroupId = group, CounterpartCashBoxId = mainBox.CashBoxId,
                    Description = desc, Status = "Posted", CreatedBy = uid
                });
            await _db.SaveChangesAsync(ct);

            /* 2) مبلغ العهدة يزيد **الأول** — عشان التريجر يحسب على الرقم الجديد */
            c.AmountIssued += amount;
            await _db.SaveChangesAsync(ct);

            /* 3) حركة «تغذية» في دفتر العهدة */
            var tx = new CustodyTransaction
            {
                CustodyId       = c.CustodyId,
                TransactionType = "TopUp",
                Amount          = amount,
                TransactionDate = when,
                Notes           = $"تمويل معتمد — طلب رقم {fr.FundRequestId}",
                CreatedBy       = uid
            };
            _db.CustodyTransactions.Add(tx);
            await _db.SaveChangesAsync(ct);

            /* 4) الفلوس تخرج من صندوق الموظف لحساب العهدة (نفس منطق التغذية اليدوية) */
            var ok = await _cash.PaymentAsync(new CashEntry(
                ReferenceNumber : $"CUST-TOPUP:{c.CustodyNumber}:{tx.CustodyTransactionId}",
                Amount          : amount,
                PaymentMethodId : await CashMethodIdAsync(ct) ?? 0,
                CustodyId       : c.CustodyId,
                Description     : $"تغذية عهدة {c.CustodyNumber} — تمويل معتمد",
                Date            : DateOnly.FromDateTime(when),
                CashBoxId       : empBox.CashBoxId), ct);
            if (!ok) throw new GeneralCustodyBoxException();
            await _db.SaveChangesAsync(ct);

            /* 5) الطلب → معتمد + ربط حركة التغذية (القفل ضد الموافقة مرتين) */
            fr.Status               = "Approved";
            fr.ApprovedAmount       = amount;
            fr.DecidedBy            = uid;
            fr.DecidedAt            = DateTime.UtcNow;
            fr.DecisionNotes        = B(req?.Notes);
            fr.TopUpTransactionId   = tx.CustodyTransactionId;
            fr.UpdatedAt            = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _db.Database.CommitTransactionAsync(ct);
            funded = amount;
        }
        catch (GeneralCustodyBoxException)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            return BadRequest(new { message = "تعذّر صرف التغذية من خزينة الموظف — الموافقة اتلغت بالكامل" });
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Approve, "CustodyFundRequest",
            fr.FundRequestId.ToString(),
            description: $"اعتماد تمويل {funded:N2} على العهدة {c.CustodyNumber} (مطلوب {fr.RequestedAmount:N2})", ct: ct);

        await NotifyUserAsync(fr.RequestedBy,
            $"اتوافق على طلب تمويل عهدتك {c.CustodyNumber}",
            $"اتحوّل {funded:N2} ج.م من الخزينة الرئيسية لخزينتك، وزاد مبلغ العهدة بنفس القيمة.",
            c, "Normal", ct);

        return Ok(new { message = $"اتحوّل {funded:N2} ج.م لخزينة الموظف وزاد مبلغ العهدة بنفس القيمة" });
    }

    [HttpPost("fund-requests/{rid:long}/reject")]
    [Authorize(Policy = "PERM:CUSTODY.APPROVE")]
    public async Task<IActionResult> RejectFundRequest(long rid, [FromBody] FundDecisionBody? req, CancellationToken ct)
    {
        var fr = await _db.CustodyFundRequests.FirstOrDefaultAsync(x => x.FundRequestId == rid && !x.IsDeleted, ct);
        if (fr is null) return NotFound(new { message = "الطلب مش موجود" });
        if (fr.Status != "Pending")
            return BadRequest(new { message = $"الطلب حالته {FundStatusAr(fr.Status)} — الرفض على طلب منتظر بس" });

        var c = await _db.DriverCustodies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CustodyId == fr.CustodyId && !x.IsDeleted, ct);

        fr.Status         = "Rejected";
        fr.DecidedBy      = CurrentUserId();
        fr.DecidedAt      = DateTime.UtcNow;
        fr.DecisionNotes  = B(req?.Notes);
        fr.UpdatedAt      = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(FastCom.Server.Services.AuditActions.Update, "CustodyFundRequest",
            fr.FundRequestId.ToString(),
            description: $"رفض طلب تمويل {fr.RequestedAmount:N2}" + (string.IsNullOrWhiteSpace(req?.Notes) ? "" : $" — السبب: {B(req!.Notes)}"), ct: ct);

        if (c is not null)
            await NotifyUserAsync(fr.RequestedBy,
                $"اترفض طلب تمويل عهدتك {c.CustodyNumber}",
                string.IsNullOrWhiteSpace(req?.Notes)
                    ? $"اترفض طلب التمويل بمبلغ {fr.RequestedAmount:N2} ج.م."
                    : $"اترفض طلب التمويل بمبلغ {fr.RequestedAmount:N2} ج.م — السبب: {B(req!.Notes)}",
                c, "High", ct);

        return Ok(new { message = "اترفض الطلب" });
    }
}
