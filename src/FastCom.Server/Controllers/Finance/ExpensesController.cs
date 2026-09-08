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
/// المصروفات — وقود، رسوم ميناء، ونش، غرامات...
/// <para>المصروف المربوط بعملية أو رحلة بيدخل في <c>ActualCost</c> بـ trigger.</para>
/// <para>أنواع المصروفات اللي <c>IsOperationCost = 0</c> (صيانة/تأمين/إداري) مابتتحملش على عملية.</para>
/// </summary>
[ApiController]
[Route("api/expenses")]
[Authorize]
public class ExpensesController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly INumberingService _numbers;
    private readonly IPermissionService _perms;

    public ExpensesController(FastComDbContext db, INumberingService numbers, IPermissionService perms)
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

    public record ExpenseUpsert(int ExpenseTypeId, string? ExpenseDate, string? Description,
        decimal Amount, long? OperationId, long? TripId, int? DriverId, int? SupplierId,
        long? CustodyId, int? TaxRateId, bool? IsTaxDeductible, string? ReferenceNumber, string? Notes, bool AsDraft);

    public record ListItem(long ExpenseId, string ExpenseNumber, string TypeName,
        string? Description, DateTime ExpenseDate, decimal Amount, decimal TaxRate,
        string? OperationNumber, string? TripNumber, string? SupplierName, string? DriverName, string? CustodyNumber,
        string PaymentStatus, string Status, bool IsApproved);

    public record Detail(long ExpenseId, string ExpenseNumber, int ExpenseTypeId,
        string? ExpenseDate, string? Description, decimal Amount, int? TaxRateId,
        bool IsTaxDeductible, long? OperationId, long? TripId, int? DriverId, int? SupplierId, long? CustodyId,
        string? ReferenceNumber, string? Notes, string PaymentStatus, string Status,
        bool IsApproved, decimal TaxRate);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:EXPENSE.VIEW")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? typeId,
        [FromQuery] long? operationId, [FromQuery] long? tripId, [FromQuery] string? q,
        [FromQuery] int take = 300, CancellationToken ct = default)
    {
        if (take is < 1 or > 500) take = 300;

        var query = _db.Expenses.AsNoTracking().Where(e => !e.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(e => e.Status == status);
        if (typeId is not null)      query = query.Where(e => e.ExpenseTypeId == typeId);
        if (operationId is not null) query = query.Where(e => e.OperationId == operationId);
        if (tripId is not null)      query = query.Where(e => e.TripId == tripId);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(e =>
                e.ExpenseNumber.Contains(s) ||
                (e.Description != null && e.Description.Contains(s)) ||
                (e.ReferenceNumber != null && e.ReferenceNumber.Contains(s)));
        }

        var rows = await query
            .OrderByDescending(e => e.ExpenseId)
            .Take(take)
            .Select(e => new ListItem(
                e.ExpenseId, e.ExpenseNumber, e.ExpenseType.NameAr, e.Description,
                e.ExpenseDate, e.Amount, e.TaxRate,
                e.Operation != null ? e.Operation.OperationNumber : null,
                e.Trip != null ? e.Trip.TripNumber : null,
                e.Supplier != null ? e.Supplier.NameAr : null,
                e.Driver != null ? e.Driver.FullName : null,
                e.Custody != null ? e.Custody.CustodyNumber : null,
                e.PaymentStatus, e.Status, e.IsApproved))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ONE ═══════════════

    [HttpGet("{id:long}")]
    [Authorize(Policy = "PERM:EXPENSE.VIEW")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var e = await _db.Expenses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExpenseId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف مش موجود" });

        return Ok(new Detail(e.ExpenseId, e.ExpenseNumber, e.ExpenseTypeId,
            e.ExpenseDate.ToString("yyyy-MM-ddTHH:mm"), e.Description, e.Amount,
            e.TaxRateId, e.IsTaxDeductible, e.OperationId, e.TripId, e.DriverId, e.SupplierId, e.CustodyId, e.ReferenceNumber, e.Notes, e.PaymentStatus, e.Status, e.IsApproved, e.TaxRate));
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:EXPENSE.CREATE")]
    public async Task<IActionResult> Create([FromBody] ExpenseUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "مافيش فرع معرّف في النظام" });

        var type = await _db.ExpenseTypes.AsNoTracking()
            .FirstAsync(t => t.ExpenseTypeId == req.ExpenseTypeId, ct);

        var e = new Expense
        {
            ExpenseNumber   = await _numbers.NextAsync("EXPENSE", ct),
            BranchId        = branchId.Value,
            OperationId     = req.OperationId,
            TripId          = req.TripId,
            CustodyId       = req.CustodyId,
            DriverId        = req.DriverId,
            SupplierId      = req.SupplierId,
            ExpenseTypeId   = req.ExpenseTypeId,
            ExpenseDate     = Dt(req.ExpenseDate),
            Description     = B(req.Description),
            Amount          = req.Amount,
            TaxRateId       = req.TaxRateId,
            TaxRate         = 0,                       // Snapshot — بتتملأ تحت
            IsTaxDeductible = req.IsTaxDeductible ?? type.IsTaxDeductible,
            ReferenceNumber = B(req.ReferenceNumber),
            Notes           = B(req.Notes),
            PaymentStatus   = "Unpaid",
            Status          = req.AsDraft ? "Draft" : "Posted",
            CreatedBy       = CurrentUserId()
        };

        _db.Expenses.Add(e);
        await FillTaxSnapshotAsync(ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = e.ExpenseId, number = e.ExpenseNumber,
            message = $"✅ اتسجل المصروف برقم {e.ExpenseNumber}" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:long}")]
    [Authorize(Policy = "PERM:EXPENSE.EDIT")]
    public async Task<IActionResult> Update(long id, [FromBody] ExpenseUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.ExpenseId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف مش موجود" });
        if (e.Status == "Cancelled") return BadRequest(new { message = "المصروف ملغي — مش قابل للتعديل" });

        var type = await _db.ExpenseTypes.AsNoTracking()
            .FirstAsync(t => t.ExpenseTypeId == req.ExpenseTypeId, ct);

        e.ExpenseTypeId   = req.ExpenseTypeId;
        e.ExpenseDate     = Dt(req.ExpenseDate);
        e.Description     = B(req.Description);
        e.Amount          = req.Amount;
        e.OperationId     = req.OperationId;
        e.TripId          = req.TripId;
        e.CustodyId       = req.CustodyId;
        e.DriverId        = req.DriverId;
        e.SupplierId      = req.SupplierId;
        e.TaxRateId       = req.TaxRateId;
        e.IsTaxDeductible = req.IsTaxDeductible ?? type.IsTaxDeductible;
        e.ReferenceNumber = B(req.ReferenceNumber);
        e.Notes           = B(req.Notes);
        e.UpdatedAt       = DateTime.UtcNow;
        e.UpdatedBy       = CurrentUserId();

        await FillTaxSnapshotAsync(ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    /// <summary>TaxRate في الجدول Denormalized Snapshot — لازم التطبيق ينسخها من TaxRates.</summary>
    private async Task FillTaxSnapshotAsync(CancellationToken ct)
    {
        var pending = _db.ChangeTracker.Entries<Expense>()
            .Where(x => x.State == EntityState.Added || x.State == EntityState.Modified)
            .Select(x => x.Entity).ToList();

        var ids = pending.Where(e => e.TaxRateId is not null)
                         .Select(e => e.TaxRateId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            foreach (var e in pending) e.TaxRate = 0m;
            return;
        }

        var rates = await _db.TaxRates.AsNoTracking()
            .Where(t => ids.Contains(t.TaxRateId))
            .ToDictionaryAsync(t => t.TaxRateId, t => t.Rate, ct);

        foreach (var e in pending)
            e.TaxRate = e.TaxRateId is not null && rates.TryGetValue(e.TaxRateId.Value, out var v) ? v : 0m;
    }

    // ═══════════════ STATUS ═══════════════

    public record StatusRequest(string? To);

    [HttpPost("{id:long}/status")]
    [Authorize(Policy = "PERM:EXPENSE.EDIT")]
    public async Task<IActionResult> SetStatus(long id, [FromBody] StatusRequest req, CancellationToken ct)
    {
        var to = req?.To?.Trim();
        if (to is not ("Draft" or "Posted" or "Cancelled"))
            return BadRequest(new { message = "الحالة مش صالحة" });

        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.ExpenseId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف مش موجود" });

        if (e.Status == to) return BadRequest(new { message = "المصروف في الحالة دي بالفعل" });

        if (to == "Posted" && e.PaymentStatus == "Paid")
            return BadRequest(new { message = "المصروف اتدفع — مش هيتلغى التأكيد" });

        e.Status    = to;
        e.UpdatedAt = DateTime.UtcNow;
        e.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = to switch
        {
            "Posted"    => "✅ اتأكد المصروف",
            "Cancelled" => "✅ اتلغى المصروف",
            _           => "✅ رجع مسودة"
        }});
    }

    // ═══════════════ APPROVE ═══════════════

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = "PERM:EXPENSE.APPROVE")]
    public async Task<IActionResult> Approve(long id, CancellationToken ct)
    {
        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.ExpenseId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف مش موجود" });
        if (e.Status != "Posted") return BadRequest(new { message = "أكّد المصروف الأول" });
        if (e.IsApproved) return BadRequest(new { message = "المصروف معتمد بالفعل" });

        e.IsApproved = true;
        e.ApprovedBy = CurrentUserId();
        e.ApprovedAt = DateTime.UtcNow;
        e.UpdatedAt  = DateTime.UtcNow;
        e.UpdatedBy  = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اعتمد المصروف" });
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:EXPENSE.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.ExpenseId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف مش موجود" });

        if (e.PaymentStatus != "Unpaid")
            return BadRequest(new { message = "المصروف اتدفع جزئيًا أو كليًا — ماينفعش يتحذف" });
        if (await _db.CustodyTransactions.AnyAsync(t => t.ExpenseId == id, ct))
            return BadRequest(new { message = "المصروف مربوط بعهدة — ماينفعش يتحذف" });

        e.IsDeleted = true;
        e.DeletedAt = DateTime.UtcNow;
        e.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ اتحذف المصروف" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(ExpenseUpsert? r, CancellationToken ct)
    {
        if (r is null) return "البيانات مش كاملة";
        if (r.Amount <= 0) return "المبلغ لازم يكون أكتر من صفر";
        if (r.Amount > 10_000_000m) return "المبلغ كبير بشكل غير منطقي";

        var type = await _db.ExpenseTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ExpenseTypeId == r.ExpenseTypeId && !t.IsDeleted, ct);
        if (type is null) return "نوع المصروف مش موجود";

        if (r.OperationId is not null)
        {
            if (!type.IsOperationCost)
                return $"«{type.NameAr}» مش بتتحمل على عملية — شيل ربط العملية";

            var op = await _db.Operations.AsNoTracking()
                .FirstOrDefaultAsync(o => o.OperationId == r.OperationId && !o.IsDeleted, ct);
            if (op is null) return "العملية مش موجودة";
            if (op.Status == "Closed") return "العملية مقفولة — مش هتضيف عليها مصروفات";
        }

        if (r.TripId is not null &&
            !await _db.Trips.AnyAsync(t => t.TripId == r.TripId && !t.IsDeleted, ct))
            return "الرحلة مش موجودة";

        if (r.DriverId is not null &&
            !await _db.Drivers.AnyAsync(d => d.DriverId == r.DriverId && !d.IsDeleted, ct))
            return "السائق مش موجود";

        if (r.SupplierId is not null &&
            !await _db.Suppliers.AnyAsync(s => s.SupplierId == r.SupplierId && !s.IsDeleted, ct))
            return "المورد مش موجود";

        if (r.TaxRateId is not null &&
            !await _db.TaxRates.AnyAsync(t => t.TaxRateId == r.TaxRateId && t.IsActive, ct))
            return "نسبة الضريبة مش موجودة";

        if (r.CustodyId is not null)
        {
            var cus = await _db.DriverCustodies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustodyId == r.CustodyId, ct);
            if (cus is null) return "العهدة مش موجودة";
            if (cus.IsDeleted) return "العهدة محذوفة";
            if (cus.Status == "Closed") return "العهدة مقفولة — مش هتربط بيها مصروف جديد";
            if (r.TripId is not null && r.TripId != cus.TripId)
                return "العهدة على رحلة تانية — اختار رحلة العهدة نفسها";
        }

        if (r.OperationId is null && r.TripId is null && r.SupplierId is null)
            return "اربط المصروف بعملية أو رحلة أو مورد — عشان يعرف يتحمل على مين";

        return null;
    }
}
