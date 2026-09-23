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
    private readonly ISettingsService _settings;
    private readonly ICashBook _cash;

    private readonly Services.IAuditService _audit;

    public ExpensesController(FastComDbContext db, INumberingService numbers, IPermissionService perms,
                              ICashBook cash,
                               ISettingsService settings, Services.IAuditService audit)
    { _db = db; _numbers = numbers; _perms = perms; _settings = settings; _cash = cash; _audit = audit; }

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
        decimal Amount, long? OperationId, long? TripId, int? DriverId, int? VehicleId, int? SupplierId,
        long? CustodyId, int? TaxRateId, bool? IsTaxDeductible, string? ReferenceNumber, string? Notes, bool AsDraft,
        int? PaymentMethodId = null);

    public record ListItem(long ExpenseId, string ExpenseNumber, string TypeName,
        string? Description, DateTime ExpenseDate, decimal Amount, decimal TaxRate,
        string? OperationNumber, string? TripNumber, string? VehiclePlate, string? SupplierName, string? DriverName, string? CustodyNumber,
        string PaymentStatus, string Status, bool IsApproved);

    public record Detail(long ExpenseId, string ExpenseNumber, int ExpenseTypeId,
        string? ExpenseDate, string? Description, decimal Amount, int? TaxRateId,
        bool IsTaxDeductible, long? OperationId, long? TripId, int? DriverId, int? VehicleId, int? SupplierId, long? CustodyId,
        string? ReferenceNumber, string? Notes, string PaymentStatus, string Status,
        bool IsApproved, decimal TaxRate, int? PaymentMethodId, string? MethodName);

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
                e.Vehicle != null ? e.Vehicle.PlateNumber : null,
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
        if (e is null) return NotFound(new { message = "المصروف غير موجود" });

        var methodName = e.PaymentMethodId is null ? null
            : await _db.PaymentMethods.AsNoTracking()
                .Where(m => m.PaymentMethodId == e.PaymentMethodId)
                .Select(m => (string?)m.NameAr).FirstOrDefaultAsync(ct);

        return Ok(new Detail(e.ExpenseId, e.ExpenseNumber, e.ExpenseTypeId,
            e.ExpenseDate.ToString("yyyy-MM-ddTHH:mm"), e.Description, e.Amount,
            e.TaxRateId, e.IsTaxDeductible, e.OperationId, e.TripId, e.DriverId, e.VehicleId, e.SupplierId, e.CustodyId, e.ReferenceNumber, e.Notes, e.PaymentStatus, e.Status, e.IsApproved, e.TaxRate,
            e.PaymentMethodId, methodName));
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:EXPENSE.CREATE")]
    public async Task<IActionResult> Create([FromBody] ExpenseUpsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, ct);
        if (err is not null) return BadRequest(new { message = err });

        var branchId = await ResolveBranchIdAsync(ct);
        if (branchId is null) return BadRequest(new { message = "لايوجد فرع معرّف في النظام" });

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
            VehicleId       = req.VehicleId,
            SupplierId      = req.SupplierId,
            ExpenseTypeId   = req.ExpenseTypeId,
            ExpenseDate     = Dt(req.ExpenseDate),
            Description     = B(req.Description),
            Amount          = req.Amount,
            TaxRateId       = req.TaxRateId,
            TaxRate         = 0,                       // Snapshot — بتتملأ تحت
            IsTaxDeductible = req.IsTaxDeductible ?? type.IsTaxDeductible,
            ReferenceNumber = B(req.ReferenceNumber),
            PaymentMethodId = req.PaymentMethodId,
            Notes           = B(req.Notes),
            PaymentStatus   = "Unpaid",
            Status          = req.AsDraft ? "Draft" : "Posted",
            CreatedBy       = CurrentUserId()
        };

        // 🔴 Atomicity + ربط العهدة: المصروف المربوط بعهدة مفتوحة بيسجّل
        //    حركة «صرف» في دفتر العهدة تلقائيًا — والـ trigger بيعيد حساب AmountSpent.
        await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.Expenses.Add(e);
            await FillTaxSnapshotAsync(ct);
            await _db.SaveChangesAsync(ct);

            if (e.CustodyId is not null && e.Status == "Posted")
            {
                _db.CustodyTransactions.Add(new CustodyTransaction
                {
                    CustodyId       = e.CustodyId.Value,
                    TransactionType = "Expense",
                    Amount          = e.Amount,
                    TransactionDate = e.ExpenseDate,
                    ExpenseId       = e.ExpenseId,
                    Notes           = $"مصروف مربوط: {e.ExpenseNumber}",
                    CreatedBy       = CurrentUserId()
                });
                await _db.SaveChangesAsync(ct);
            }

            await _db.Database.CommitTransactionAsync(ct);
        }
        catch (Exception)
        {
            try { await _db.Database.RollbackTransactionAsync(ct); } catch { /* تجاهل */ }
            throw;
        }

        return Ok(new { id = e.ExpenseId, number = e.ExpenseNumber,
            message = e.CustodyId is not null && e.Status == "Posted"
                ? $"✅ تم تسجيل المصروف برقم {e.ExpenseNumber} — واتحسب على العهدة"
                : $"✅ تم تسجيل المصروف برقم {e.ExpenseNumber}" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:long}")]
    [Authorize(Policy = "PERM:EXPENSE.EDIT")]
    public async Task<IActionResult> Update(long id, [FromBody] ExpenseUpsert req, CancellationToken ct)
    {
        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.ExpenseId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف غير موجود" });
        if (e.Status == "Cancelled") return BadRequest(new { message = "المصروف ملغي — غير قابل للتعديل" });

        var err = await ValidateAsync(req, ct, e.CustodyId);
        if (err is not null) return BadRequest(new { message = err });

        // 🔴 العهدة مقدّمة/معتمدة + نفس الربط → تغيير المبلغ هيعبث بأرقام التسوية
        if (e.CustodyId is not null && e.CustodyId == req.CustodyId && e.Amount != req.Amount)
        {
            var cus = await _db.DriverCustodies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustodyId == e.CustodyId && !c.IsDeleted, ct);
            if (cus is not null && cus.Status is not ("Open" or "PartiallySettled"))
                return BadRequest(new { message = "العهدة مقدّمة للتسوية أو معتمدة — فك ربط المصروف أو ألغِه قبل تغيير المبلغ" });
        }

        /* 🔴 امسك القيم **قبل** أي تعديل — عشان القفل والمزامنة */
        var oldAmount = e.Amount;
        var oldMethod = e.PaymentMethodId;

        var type = await _db.ExpenseTypes.AsNoTracking()
            .FirstAsync(t => t.ExpenseTypeId == req.ExpenseTypeId, ct);

        /* 🔒 لو المصروف دخل الخزينة — المبلغ وطريقة الدفع مقفولين.
            `oldAmount = 0` إشارة لـ `SyncTreasuryAsync` إن القفل اتكسر. */
        var wasInTreasury = await _db.CashTransactions.AnyAsync(t =>
            !t.IsDeleted && t.Status == "Posted" && t.ReferenceNumber == $"EXP:{e.ExpenseNumber}", ct);
        if (wasInTreasury &&
            (oldAmount != req.Amount ||
             (oldMethod ?? 0) != (req.PaymentMethodId ?? 0)))
            return BadRequest(new { message = "المصروف اتسجل في الخزينة بالفعل — المبلغ وطريقة الدفع ماتتغيرش. ألغِ المصروف وسجّل واحد جديد" });

        e.ExpenseTypeId   = req.ExpenseTypeId;
        e.ExpenseDate     = Dt(req.ExpenseDate);
        e.Description     = B(req.Description);
        e.Amount          = req.Amount;
        e.OperationId     = req.OperationId;
        e.TripId          = req.TripId;
        e.CustodyId       = req.CustodyId;
        e.DriverId        = req.DriverId;
        e.VehicleId       = req.VehicleId;
        e.SupplierId      = req.SupplierId;
        e.TaxRateId       = req.TaxRateId;
        e.IsTaxDeductible = req.IsTaxDeductible ?? type.IsTaxDeductible;
        e.ReferenceNumber = B(req.ReferenceNumber);
        e.PaymentMethodId = req.PaymentMethodId;
        e.Notes           = B(req.Notes);
        e.UpdatedAt       = DateTime.UtcNow;
        e.UpdatedBy       = CurrentUserId();

        await FillTaxSnapshotAsync(ct);

        // 🔴 مزامنة حركة العهدة مع المصروف (إنشاء/تعديل/فك ربط) — مصدر واحد للحقيقة
        await SyncCustodyTransactionAsync(e, ct);

        // 🔴 مزامنة الخزينة — المصروف المدفوع والمعتمد من غير عهدة
        await SyncTreasuryAsync(e, oldAmount, ct);
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

    /// <summary>
    /// 🔴 مصدر واحد للحقيقة: حركة «صرف» في دفتر العهدة لازم تطابق المصروف.
    /// بتتنادى بعد أي تغيير على المصروف (إنشاء/تعديل/حالة) — والـ trigger بيعيد حساب AmountSpent.
    /// </summary>
    private async Task SyncCustodyTransactionAsync(Expense e, CancellationToken ct)
    {
        var existing = await _db.CustodyTransactions
            .Where(t => t.ExpenseId == e.ExpenseId)
            .OrderBy(t => t.CustodyTransactionId)
            .ToListAsync(ct);

        var shouldExist = e.CustodyId is not null && e.Status == "Posted" && !e.IsDeleted;

        if (!shouldExist)
        {
            if (existing.Count > 0)
                _db.CustodyTransactions.RemoveRange(existing);
            return;
        }

        if (existing.Count == 0)
        {
            _db.CustodyTransactions.Add(new CustodyTransaction
            {
                CustodyId       = e.CustodyId!.Value,
                TransactionType = "Expense",
                Amount          = e.Amount,
                TransactionDate = e.ExpenseDate,
                ExpenseId       = e.ExpenseId,
                Notes           = $"مصروف مرتبط: {e.ExpenseNumber}",
                CreatedBy       = CurrentUserId()
            });
            return;
        }

        // لو فيه أكتر من حركة لنفس المصروف (بيانات قديمة) — نسيب الأولى ونشيل الباقي
        if (existing.Count > 1)
            _db.CustodyTransactions.RemoveRange(existing.Skip(1));

        var tx = existing[0];
        tx.CustodyId       = e.CustodyId!.Value;
        tx.Amount          = e.Amount;
        tx.TransactionDate = e.ExpenseDate;
        tx.Notes           = $"مصروف مرتبط: {e.ExpenseNumber}";
    }

    /* ═══════════════ 🔴 مزامنة الخزينة ═══════════════
       المصروف يدخل الخزينة **بأربعة شروط** — وأي شرط ناقص يعني مافيش حركة:

         1. `IsApproved`              — معتمد (قرار نهائي)
         2. `PaymentStatus ≠ Unpaid`  — اتدفع فعلًا (مش مجرد موافقة)
         3. `CustodyId IS NULL`       — مش من عهدة، وإلا **يتحسب مرتين**
                                       (صرف العهدة نفسه هو اللي عمل حركة الخزينة)
         4. طريقة الدفع `IsCashBased` — نقدي. الشيكات والتحويلات مش كاش في الدرج

       ⚠️ `oldAmount` مهم: لو مصروف **داخل الخزينة** مبلغه اتغيّر،
          بنلغي الحركة القديمة ونعمل واحدة جديدة — عشان الرصيد مايغلطش.      */
    private async Task<bool> SyncTreasuryAsync(Expense e, decimal? oldAmount, CancellationToken ct)
    {
        const string Prefix = "EXP:";
        var refNo = $"{Prefix}{e.ExpenseNumber}";

        var inTreasury = await _db.CashTransactions.AnyAsync(t =>
            !t.IsDeleted && t.Status == "Posted" && t.ReferenceNumber == refNo, ct);

        /* 🔒 مصروف فلوسه خرجت من الخزينة فعلًا — المبلغ وطريقة الدفع **ماتتغيرش**.
            (مصروف معتمد بس لسه مااتدفعش بيتعدّل عادي — مافيش فلوس اتحركت.) */
        if (inTreasury && oldAmount is not null && oldAmount.Value != e.Amount)
            throw new InvalidOperationException("TREASURY_LOCKED");

        var method = e.PaymentMethodId is null ? null
            : await _db.PaymentMethods.AsNoTracking()
                .FirstOrDefaultAsync(m => m.PaymentMethodId == e.PaymentMethodId, ct);

        var shouldExist = e.IsApproved
                       && e.PaymentStatus != "Unpaid"
                       && e.CustodyId is null
                       && e.Status == "Posted"
                       && !e.IsDeleted
                       && method is { IsCashBased: true };

        if (!shouldExist)
        {
            var n = await _cash.VoidByReferenceAsync(refNo, ct);
            if (n > 0) await _db.SaveChangesAsync(ct);
            return false;
        }

        /* مافيش تغيير؟ — سيب الحركة زي ما هي (ومنع أي تكرار) */
        if (inTreasury && oldAmount == e.Amount) return true;

        /* فيه تغيير في المبلغ؟ — العكس الأول، وبعدين الحركة الجديدة */
        if (inTreasury)
        {
            await _cash.VoidByReferenceAsync(refNo, ct);
            await _db.SaveChangesAsync(ct);
        }

        var ok = await _cash.PaymentAsync(new CashEntry(
            ReferenceNumber : refNo,
            Amount          : e.Amount,
            PaymentMethodId : e.PaymentMethodId!.Value,
            SupplierId      : e.SupplierId ?? 0,
            OperationId     : e.OperationId ?? 0,
            Description     : $"مصروف {e.ExpenseNumber}",
            Date            : DateOnly.FromDateTime(e.ExpenseDate)), ct);

        if (ok) await _db.SaveChangesAsync(ct);
        return ok;
    }

    // ═══════════════ STATUS ═══════════════

    public record StatusRequest(string? To);

    [HttpPost("{id:long}/status")]
    [Authorize(Policy = "PERM:EXPENSE.EDIT")]
    public async Task<IActionResult> SetStatus(long id, [FromBody] StatusRequest req, CancellationToken ct)
    {
        var to = req?.To?.Trim();
        if (to is not ("Draft" or "Posted" or "Cancelled"))
            return BadRequest(new { message = "الحالة غير صالحة" });

        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.ExpenseId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف غير موجود" });

        if (e.Status == to) return BadRequest(new { message = "المصروف في الحالة دي بالفعل" });

        if (to == "Posted" && e.PaymentStatus == "Paid")
            return BadRequest(new { message = "المصروف مدفوع — مش هيتلغى التأكيد" });

        // 🔴 رجوع للمؤكد وعليه عهدة → لازم العهدة لسه مفتوحة
        if (to == "Posted" && e.CustodyId is not null)
        {
            var cus = await _db.DriverCustodies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustodyId == e.CustodyId && !c.IsDeleted, ct);
            if (cus is null || cus.Status is not ("Open" or "PartiallySettled"))
                return BadRequest(new { message = "العهدة المرتبطة غير مفتوحة — راجع حالتها الأول" });
        }

        e.Status    = to;
        e.UpdatedAt = DateTime.UtcNow;
        e.UpdatedBy = CurrentUserId();

        // 🔴 الإلغاء بيشيل حركة الصرف من دفتر العهدة — والرجوع للمؤكد بيعيدها
        await SyncCustodyTransactionAsync(e, ct);

        /* 🔴 والخزينة كمان — إلغاء مصروف مدفوع لازم يشيل حركته من الخزينة */
        await SyncTreasuryAsync(e, null, ct);

        await _db.SaveChangesAsync(ct);

        return Ok(new { message = to switch
        {
            "Posted"    => "✅ تم تاكيد المصروف",
            "Cancelled" => "✅ تم الغاء المصروف",
            _           => "✅ رجع مسودة"
        }});
    }

    // ═══════════════ APPROVE ═══════════════

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = "PERM:EXPENSE.APPROVE")]
    public async Task<IActionResult> Approve(long id, CancellationToken ct)
    {
        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.ExpenseId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف غير موجود" });
        if (e.Status != "Posted") return BadRequest(new { message = "أكّد المصروف الأول" });
        if (e.IsApproved) return BadRequest(new { message = "المصروف معتمد بالفعل" });

        e.IsApproved = true;
        e.ApprovedBy = CurrentUserId();
        e.ApprovedAt = DateTime.UtcNow;
        e.UpdatedAt  = DateTime.UtcNow;
        e.UpdatedBy  = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        /* 🔴 لو المصروف **مدفوع قبل الاعتماد** — الاعتماد هو اللي بيفتح باب الخزينة.
            (الترتيب العكسي — اعتماد ثم دفع — بيتسجّل في `/pay`.) */
        var toTreasury = await SyncTreasuryAsync(e, null, ct);

        await _audit.LogAsync(Services.AuditActions.Approve, "Expense", e.ExpenseId.ToString(),
            newValues: $"IsApproved=true Amount={e.Amount}",
            description: $"اعتماد المصروف {e.ExpenseNumber}" + (toTreasury ? " — وتسجيله في الخزينة" : ""), ct: ct);

        return Ok(new { message = toTreasury
            ? "✅ اعتمد المصروف — واتسجل في الخزينة"
            : "✅ اعتمد المصروف" });
    }

    /* ═══════════════ 🔴 تعليم مصروف «مدفوع» ═══════════════
       ده الباب اللي كان **ناقص**: `SetStatus` بتتعامل مع Draft/Posted/Cancelled بس،
       و`Approve` بتغيّر `IsApproved` بس — فمافيش أي طريقة كانت بتوصّل
       `PaymentStatus = Paid` لغير مصروفات العهدة.

       ⚠️ مصروف العهدة **مرفوض** هنا — فلوسه خرجت وقت صرف العهدة،
          فلو سجّلناها تاني هنا هتتحسب **مرتين**.                          */
    [HttpPost("{id:long}/pay")]
    [Authorize(Policy = "PERM:EXPENSE.EDIT")]
    public async Task<IActionResult> SetPaid(long id, [FromBody] PayRequest req,
                                             CancellationToken ct)
    {
        var to = req?.To?.Trim();
        if (to is not ("Unpaid" or "PartiallyPaid" or "Paid"))
            return BadRequest(new { message = "حالة الدفع غير صالحة" });

        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.ExpenseId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف غير موجود" });

        if (to != "Unpaid")
        {
            if (e.Status != "Posted")
                return BadRequest(new { message = "أكّد المصروف الأول" });
            if (e.CustodyId is not null)
                return BadRequest(new { message = "المصروف مربوط بعهدة — فلوسه اتصرفت من العهدة، ماتعلّمش مدفوع من هنا" });
            if (e.PaymentMethodId is null)
                return BadRequest(new { message = "حدد طريقة الدفع الأول" });

            var m = await _db.PaymentMethods.AsNoTracking()
                .FirstOrDefaultAsync(x => x.PaymentMethodId == e.PaymentMethodId, ct);
            if (m is null)
                return BadRequest(new { message = "طريقة الدفع غير موجودة" });

        }

        var was = e.PaymentStatus;
        e.PaymentStatus = to;
        e.UpdatedAt     = DateTime.UtcNow;
        e.UpdatedBy     = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        var inTreasury = await SyncTreasuryAsync(e, null, ct);

        var msg = to switch
        {
            "Unpaid"        => was != "Unpaid"
                ? "↩️ رجع المصروف غير مدفوع — واتشالت حركته من الخزينة"
                : "↩️ رجع المصروف غير مدفوع",
            "PartiallyPaid" => "🟡 اتعلّم المصروف مدفوع جزئيًا",
            _               => inTreasury
                ? "✅ اتعلّم المصروف مدفوع — واتسجل في الخزينة"
                : "✅ اتعلّم المصروف مدفوع"
        };

        return Ok(new { message = msg, paymentStatus = to });
    }

    public record PayRequest(string? To);

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:EXPENSE.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.ExpenseId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "المصروف غير موجود" });

        if (e.PaymentStatus != "Unpaid")
            return BadRequest(new { message = "المصروف مدفوع جزئيًا أو كليًا — لايمكن حذفه " });
        if (await _db.CustodyTransactions.AnyAsync(t => t.ExpenseId == id, ct))
            return BadRequest(new { message = "المصروف مربوط بعهدة — لايمكن حذفه " });

        e.IsDeleted = true;
        e.DeletedAt = DateTime.UtcNow;
        e.DeletedBy = CurrentUserId();

        /* 🔴 دفاعي — الشرط فوق بيمنع حذف المدفوع، فمفروض مافيش حركة.
            بس لو حصلت بأي شكل، الرصيد مايفضلش غلط. */
        await _cash.VoidByReferenceAsync($"EXP:{e.ExpenseNumber}", ct);

        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "✅ تم حذف المصروف" });
    }

    // ═══════════════ validation ═══════════════

    private async Task<string?> ValidateAsync(ExpenseUpsert? r, CancellationToken ct,
        long? currentCustodyId = null)
    {
        if (r is null) return "البيانات غير كاملة";
        if (r.Amount <= 0) return "المبلغ لازم يكون أكتر من صفر";
        /* 🔴 كان hard-coded — بقى من `EXPENSE.MAX_AMOUNT`. 0 = من غير سقف. */
        var expenseMax = await _settings.GetDecimalAsync(SettingKeys.ExpenseMaxAmount,
                                                        SettingDefaults.ExpenseMaxAmount, ct);
        if (expenseMax > 0 && r.Amount > expenseMax)
            return $"المبلغ أكبر من سقف المصروف المسموح ({expenseMax:N0})";

        var type = await _db.ExpenseTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ExpenseTypeId == r.ExpenseTypeId && !t.IsDeleted, ct);
        if (type is null) return "نوع المصروف غير موجود";

        if (r.OperationId is not null)
        {
            if (!type.IsOperationCost)
                return $"«{type.NameAr}» غير مخصص للتكاليف التشغيلية — الغى ربط العملية";

            var op = await _db.Operations.AsNoTracking()
                .FirstOrDefaultAsync(o => o.OperationId == r.OperationId && !o.IsDeleted, ct);
            if (op is null) return "العملية غير موجودة";
            if (op.Status == "Closed") return "العملية مقفولة — لايمكن إضافة مصروفات عليها";
        }

        if (r.TripId is not null &&
            !await _db.Trips.AnyAsync(t => t.TripId == r.TripId && !t.IsDeleted, ct))
            return "الرحلة غير موجودة";

        if (r.DriverId is not null &&
            !await _db.Drivers.AnyAsync(d => d.DriverId == r.DriverId && !d.IsDeleted, ct))
            return "السائق غير موجود";

        if (r.SupplierId is not null &&
            !await _db.Suppliers.AnyAsync(s => s.SupplierId == r.SupplierId && !s.IsDeleted, ct))
            return "المورد غير موجود";

        if (r.TaxRateId is not null &&
            !await _db.TaxRates.AnyAsync(t => t.TaxRateId == r.TaxRateId && t.IsActive, ct))
            return "نسبة الضريبة غير موجودة";

        if (r.CustodyId is not null)
        {
            var cus = await _db.DriverCustodies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustodyId == r.CustodyId, ct);
            if (cus is null) return "العهدة غير موجودة";
            if (cus.IsDeleted) return "العهدة محذوفة";

            // 🔴 ربط جديد على عهدة مجمّدة ممنوع — لكن تعديل مصروف مربوط أصلًا مسموح
            //    (غير المبلغ — ده متحقق في Update) عشان متبوظش أرقام تسوية اتقدّمت.
            var linkChanged = r.CustodyId != currentCustodyId;
            if (linkChanged && cus.Status is not ("Open" or "PartiallySettled"))
                return "العهدة مقدّمة للتسوية أو مغلقة — لايمكن ربطها بمصروف جديد";

            if (r.TripId is null)
                return "المصروف المربوط بعهدة لابد ان يكون على رحلة العهدة نفسها";
            if (r.TripId != cus.TripId)
                return "العهدة على رحلة اخرى — اختار رحلة العهدة نفسها";
        }

        /* 🔴 كان الشرط بيجبر **أي** مصروف يتربط بعملية/رحلة/مورد — وده غلط:
              المصروفات الإدارية (إيجار · كهرباء · مرتبات · قرطاسية) `IsOperationCost = 0`
              فمافيهاش حاجة تتربط بيها أصلًا، والقديمة كانت بتخليها مستحيلة التسجيل.

              القاعدة الصح:
                • مصروف **تشغيلي** (`IsOperationCost = 1`) → لازم يتربط،
                  عشان يدخل في تكلفة العملية ويظهر في ربحيتها.
                • مصروف **إداري** (`IsOperationCost = 0`) → الربط اختياري.           */
        if (type.IsOperationCost &&
            r.OperationId is null && r.TripId is null && r.SupplierId is null)
            return $"«{type.NameAr}» مصروف تشغيلي — لازم يتربط بعملية أو رحلة أو مورد عشان يتحمل على مين";

        return null;
    }
}
