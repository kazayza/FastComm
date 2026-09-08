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
    private readonly INumberingService _numbers;
    private readonly IPermissionService _perms;

    public CustodiesController(FastComDbContext db, INumberingService numbers, IPermissionService perms)
    { _db = db; _numbers = numbers; _perms = perms; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static DateTime Dt(string? s) => DateTime.TryParse(s, out var d) ? d : DateTime.UtcNow;

    private async Task<int?> ResolveBranchIdAsync(CancellationToken ct)
    {
        if (int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0) return b;
        return await _db.Branches.AsNoTracking()
            .OrderBy(x => x.BranchId).Select(x => (int?)x.BranchId).FirstOrDefaultAsync(ct);
    }

    // ═══════════════ DTOs ═══════════════

    public record CustodyCreate(long TripId, string OwnerType, int OwnerId,
        decimal AmountIssued, string? CustodyDate, string? Notes);

    public record TxRequest(string? Type, decimal Amount, string? TxDate, long? ExpenseId, string? Notes);

    public record ApproveRequest(bool AddAdditional);

    public record ListItem(long CustodyId, string CustodyNumber, DateTime CustodyDate,
        string OwnerType, string OwnerName, long TripId, string TripNumber, string? DriverName,
        decimal AmountIssued, decimal AmountSpent, decimal AmountReturned, decimal AdditionalDue,
        decimal Remaining, string Status, int TxCount);

    public record Detail(long CustodyId, string CustodyNumber, DateTime CustodyDate,
        string OwnerType, int OwnerId, string OwnerName, long TripId, string TripNumber,
        string? DriverName, decimal AmountIssued, decimal AmountSpent, decimal AmountReturned,
        decimal AdditionalDue, decimal Remaining, string Status, string? Notes,
        DateTime? ClosedAt);

    public record TxLine(long CustodyTransactionId, string TransactionType, DateTime TransactionDate,
        decimal Amount, long? ExpenseId, string? ExpenseNumber, string? Notes);

    public record CustodyExpense(long ExpenseId, string ExpenseNumber, string TypeName,
        decimal Amount, DateTime ExpenseDate, string? Description, string PaymentStatus);

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
            query = query.Where(c => c.CustodyNumber.Contains(s) || c.Trip.TripNumber.Contains(s));
        }

        var rows = await query
            .OrderByDescending(c => c.CustodyId)
            .Take(take)
            .Select(c => new ListItem(
                c.CustodyId, c.CustodyNumber, c.CustodyDate, c.OwnerType,
                c.OwnerType == "Driver"
                    ? _db.Drivers.Where(d => d.DriverId == c.OwnerId).Select(d => d.FullName).FirstOrDefault() ?? "—"
                    : _db.Employees.Where(e => e.EmployeeId == c.OwnerId).Select(e => e.FullNameAr).FirstOrDefault() ?? "—",
                c.TripId, c.Trip.TripNumber,
                c.Trip.Driver.FullName,
                c.AmountIssued, c.AmountSpent, c.AmountReturned, c.AdditionalDue,
                c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned,
                c.Status,
                _db.CustodyTransactions.Count(t => t.CustodyId == c.CustodyId)))
            .ToListAsync(ct);

        return Ok(rows);
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
                t.Amount, t.ExpenseId, t.Expense != null ? t.Expense.ExpenseNumber : null, t.Notes))
            .ToListAsync(ct);

        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => e.CustodyId == id && !e.IsDeleted)
            .OrderByDescending(e => e.ExpenseId)
            .Select(e => new CustodyExpense(e.ExpenseId, e.ExpenseNumber, e.ExpenseType.NameAr,
                e.Amount, e.ExpenseDate, e.Description, e.PaymentStatus))
            .ToListAsync(ct);

        var detail = new Detail(c.CustodyId, c.CustodyNumber, c.CustodyDate, c.OwnerType, c.OwnerId,
            owner ?? "—", c.TripId, trip?.TripNumber ?? "—", trip?.Driver,
            c.AmountIssued, c.AmountSpent, c.AmountReturned, c.AdditionalDue,
            c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned,
            c.Status, c.Notes, c.ClosedAt);

        return Ok(new { Custody = detail, Transactions = txs, Expenses = expenses });
    }

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
        if (req.AmountIssued > 10_000_000m) return BadRequest(new { message = "المبلغ كبير بشكل غير منطقي" });
        if (req.OwnerId <= 0) return BadRequest(new { message = "اختار صاحب العهدة" });

        if (req.OwnerType is not ("Driver" or "Employee"))
            return BadRequest(new { message = "نوع صاحب العهدة لازم يكون سائق أو موظف" });

        var trip = await _db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TripId == req.TripId && !t.IsDeleted, ct);
        if (trip is null) return BadRequest(new { message = "الرحلة مش موجودة" });
        if (trip.Status == "Cancelled") return BadRequest(new { message = "الرحلة ملغية — مافيش عهدة عليها" });

        if (req.OwnerType == "Driver")
        {
            var d = await _db.Drivers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.DriverId == req.OwnerId && !x.IsDeleted, ct);
            if (d is null) return BadRequest(new { message = "السائق مش موجود" });
            if (d.DriverId != trip.DriverId)
                return BadRequest(new { message = $"السائق ده مش سائق الرحلة {trip.TripNumber}" });
        }
        else if (!await _db.Employees.AnyAsync(e => e.EmployeeId == req.OwnerId && !e.IsDeleted, ct))
            return BadRequest(new { message = "الموظف مش موجود" });

        // عهدة واحدة مفتوحة لكل (رحلة + صاحب)
        var dup = await _db.DriverCustodies.AsNoTracking().AnyAsync(c =>
            !c.IsDeleted && c.TripId == req.TripId && c.OwnerType == req.OwnerType &&
            c.OwnerId == req.OwnerId && c.Status != "Closed", ct);
        if (dup) return BadRequest(new { message = "في عهدة مفتوحة لنفس الشخص على الرحلة دي — صفّيها الأول" });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var c = new DriverCustody
        {
            CustodyNumber = await _numbers.NextAsync("CUSTODY", ct),
            BranchId      = branchId.Value,
            TripId        = req.TripId,
            OwnerType     = req.OwnerType,
            OwnerId       = req.OwnerId,
            CustodyDate   = Dt(req.CustodyDate),
            AmountIssued  = req.AmountIssued,
            Status        = "Open",
            Notes         = B(req.Notes),
            CreatedBy     = CurrentUserId()
        };
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
        }

        return Ok(new { id = c.CustodyId, number = c.CustodyNumber,
            message = $"✅ اتفتحت العهدة برقم {c.CustodyNumber}" });
    }

    // ═══════════════ حركة — رد باقى / إضافة مبلغ ═══════════════

    [HttpPost("{id:long}/transactions")]
    [Authorize(Policy = "PERM:CUSTODY.SETTLE")]
    public async Task<IActionResult> AddTransaction(long id, [FromBody] TxRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { message = "البيانات مش كاملة" });
        if (req.Amount <= 0) return BadRequest(new { message = "المبلغ لازم يكون أكتر من صفر" });
        if (req.Amount > 10_000_000m) return BadRequest(new { message = "المبلغ كبير بشكل غير منطقي" });
        if (req.Type is not ("Additional" or "Refund"))
            return BadRequest(new { message = "نوع الحركة لازم يكون «رد باقي» أو «إضافة مبلغ»" });

        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status is "Closed" or "Approved" or "Submitted")
            return BadRequest(new { message = "العهدة اتقدّمت للتسوية — مافيش حركات جديدة" });

        if (req.Type == "Refund")
        {
            // CK_Custodies_NoOverRefund — مانرجّعش أكتر من المتاح
            var max = c.AmountIssued + c.AdditionalDue - c.AmountSpent - c.AmountReturned;
            if (req.Amount > max)
                return BadRequest(new { message = $"المتاح للرد {max:N2} بس" });
        }

        if (req.ExpenseId is not null)
        {
            var ex = await _db.Expenses.AsNoTracking()
                .FirstOrDefaultAsync(e => e.ExpenseId == req.ExpenseId && !e.IsDeleted, ct);
            if (ex is null) return BadRequest(new { message = "المصروف مش موجود" });
            if (ex.TripId != c.TripId)
                return BadRequest(new { message = "المصروف ده على رحلة تانية — مش مرتبط بالعهدة دي" });
        }

        _db.CustodyTransactions.Add(new CustodyTransaction
        {
            CustodyId       = c.CustodyId,
            TransactionType = req.Type,
            Amount          = req.Amount,
            TransactionDate = Dt(req.TxDate),
            ExpenseId       = req.ExpenseId,
            Notes           = B(req.Notes),
            CreatedBy       = CurrentUserId()
        });
        await _db.SaveChangesAsync(ct);

        var now = await _db.DriverCustodies.AsNoTracking()
            .FirstAsync(x => x.CustodyId == id, ct);

        return Ok(new
        {
            message = req.Type == "Refund" ? "✅ اتسجل رد الباقي" : "✅ اتسجلت الإضافة",
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
            return BadRequest(new { message = "العهدة مش في حالة تسمح بتقديم التسوية" });

        var moved = await _db.CustodyTransactions.AnyAsync(
            t => t.CustodyId == id && t.TransactionType != "Issue", ct);
        if (!moved)
            return BadRequest(new { message = "مافيش حركات مسجلة — سجّل المصروفات والرد الأول" });

        c.Status    = "Submitted";
        c.UpdatedAt = DateTime.UtcNow;
        c.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "📤 اتقدّمت التسوية — مستنية الاعتماد" });
    }

    // ═══════════════ اعتماد التسوية ═══════════════

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = "PERM:CUSTODY.APPROVE")]
    public async Task<IActionResult> Approve(long id, [FromBody] ApproveRequest? req, CancellationToken ct)
    {
        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status != "Submitted")
            return BadRequest(new { message = "العهدة مش مقدّمة للتسوية" });

        var extra = c.AmountSpent + c.AmountReturned - (c.AmountIssued + c.AdditionalDue);
        if (extra > 0)
        {
            if (req?.AddAdditional != true)
                return BadRequest(new
                {
                    message = $"السائق صرف أكتر من العهدة بـ {extra:N2} — علّم «سجّل الفرق كمبلغ إضافي» واعتمد تاني"
                });

            _db.CustodyTransactions.Add(new CustodyTransaction
            {
                CustodyId       = c.CustodyId,
                TransactionType = "Additional",
                Amount          = extra,
                TransactionDate = DateTime.UtcNow,
                Notes           = "فرق مصروفات اتسجل وقت الاعتماد",
                CreatedBy       = CurrentUserId()
            });
            await _db.SaveChangesAsync(ct);
            // الـ trigger هيحدّث AdditionalDue — نقرا من جديد
            c = await _db.DriverCustodies.FirstAsync(x => x.CustodyId == id, ct);
        }

        c.Status    = "Approved";
        c.UpdatedAt = DateTime.UtcNow;
        c.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اعتمدتت التسوية — جاهزة للإغلاق" });
    }

    // ═══════════════ إغلاق ═══════════════

    [HttpPost("{id:long}/close")]
    [Authorize(Policy = "PERM:CUSTODY.CLOSE")]
    public async Task<IActionResult> Close(long id, CancellationToken ct)
    {
        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status != "Approved")
            return BadRequest(new { message = "العهدة لازم تكون معتمدة قبل الإغلاق" });

        c.Status    = "Closed";
        c.ClosedAt  = DateTime.UtcNow;
        c.ClosedBy  = CurrentUserId();
        c.UpdatedAt = DateTime.UtcNow;
        c.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "🔒 اتقفلت العهدة" });
    }

    // ═══════════════ حذف ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:CUSTODY.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var c = await _db.DriverCustodies.FirstOrDefaultAsync(x => x.CustodyId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العهدة مش موجودة" });
        if (c.Status != "Open")
            return BadRequest(new { message = "العهدة اتحرّكت — ماتتحذفش" });

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

        c.IsDeleted = true;
        c.DeletedAt = DateTime.UtcNow;
        c.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "🗑️ اتحذفت العهدة" });
    }
}
