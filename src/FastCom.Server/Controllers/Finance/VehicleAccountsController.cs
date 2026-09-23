using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Finance;

/// <summary>
/// كشف حساب العربية — زي ملف الإكسيل بالظبط.
/// <para>اختار عربية + شهر → رصيد مُرحّل ← رحلات (نولون − عهدة) ← مصاريف ← دفعات ← رصيد آخر الشهر.</para>
/// <para>الرصيد المرحّل بيتجمع من كل الحركات من أول ما العربية اتسجلت لحد أول الشهر —
/// مافيش جدول ترحيل، يعني مستحيل يغلط أو يتنسي.</para>
/// </summary>
[ApiController]
[Route("api/vehicle-accounts")]
[Authorize]
public class VehicleAccountsController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly ICashBook _cash;

    public VehicleAccountsController(FastComDbContext db, ICashBook cash)
    { _db = db; _cash = cash; }

    /* ═══════════════ DTOs ═══════════════ */

    public record VehicleOpt(int VehicleId, string PlateNumber, string VehicleType, string Status);

    public record StmtLine(DateTime Date, string Kind, string Ref, string? Description,
        decimal Debit, decimal Credit, decimal Balance);

    public record StatementOut(string PlateNumber, decimal Opening, List<StmtLine> Lines,
        decimal TotalDebit, decimal TotalCredit, decimal Closing);

    public record PaymentIn(int VehicleId, string? TxDate, decimal Amount,
        int? PaymentMethodId, string? Description);

    /* ═══════════════ LIST — العربيات ═══════════════ */

    [HttpGet("vehicles")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> Vehicles(CancellationToken ct) =>
        Ok(await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted)
            .OrderBy(v => v.PlateNumber)
            .Select(v => new VehicleOpt(v.VehicleId, v.PlateNumber, v.VehicleType, v.Status))
            .ToListAsync(ct));

    /* ═══════════════ STATEMENT — كشف الحساب ═══════════════ */

    [HttpGet("{vehicleId:int}/statement")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> Statement(int vehicleId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.VehicleId == vehicleId && !v.IsDeleted, ct);
        if (vehicle is null) return NotFound(new { message = "العربية مش موجودة" });

        var start = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var end   = (to ?? start.AddMonths(1)).Date;
        if (end <= start) end = start.AddMonths(1);

        /* ── الرصيد المرحّل: كل الحركات من الأول لحد أول الشهر ── */
        var opening = await CalcBalanceAsync(vehicleId, DateTime.MinValue, start, ct);

        /* ── الرحلات في الفترة (نولون + عهدة السائق) ── */
        var trips = await _db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted && t.VehicleId == vehicleId &&
                        t.ActualEndAt != null &&
                        t.ActualEndAt >= start && t.ActualEndAt < end)
            .OrderBy(t => t.ActualEndAt)
            .Select(t => new
            {
                Date        = t.ActualEndAt!.Value,
                t.TripNumber,
                Freight     = (decimal?)(t.FreightAmount ?? 0m),
                Custody     = _db.DriverCustodies
                    .Where(c => !c.IsDeleted && c.TripId == t.TripId && c.OwnerType == "Driver")
                    .Sum(c => (decimal?)c.AmountIssued)
            })
            .ToListAsync(ct);

        /* ── المصروفات في الفترة ── */
        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.VehicleId == vehicleId &&
                        e.ExpenseDate >= start && e.ExpenseDate < end &&
                        e.Status != "Cancelled")
            .OrderBy(e => e.ExpenseDate)
            .Select(e => new
            {
                e.ExpenseDate,
                e.ExpenseNumber,
                e.Description,
                Amount = (decimal?)e.Amount
            })
            .ToListAsync(ct);

        /* ── الدفعات في الفترة ── */
        var payments = await _db.CashTransactions.AsNoTracking()
            .Where(t => !t.IsDeleted && t.VehicleId == vehicleId &&
                        t.Status == "Posted" &&
                        t.TransactionDate >= start && t.TransactionDate < end)
            .OrderBy(t => t.TransactionDate)
            .Select(t => new
            {
                t.TransactionDate,
                t.ReferenceNumber,
                t.Description,
                Amount = (decimal?)t.Amount
            })
            .ToListAsync(ct);

        /* ── نبني السطور (كلها client-side بعد ToListAsync) ── */
        var lines = new List<StmtLine>();

        foreach (var t in trips)
        {
            if (t.Freight is > 0)
                lines.Add(new StmtLine(t.Date, "Freight", t.TripNumber, "نولون الرحلة", t.Freight.Value, 0, 0));
            if (t.Custody is > 0)
                lines.Add(new StmtLine(t.Date, "Custody", t.TripNumber, "عهدة السائق", 0, t.Custody.Value, 0));
        }

        foreach (var e in expenses)
            lines.Add(new StmtLine(e.ExpenseDate, "Expense", e.ExpenseNumber,
                e.Description, 0, e.Amount ?? 0, 0));

        foreach (var p in payments)
            lines.Add(new StmtLine(p.TransactionDate, "Payment", p.ReferenceNumber ?? "—",
                p.Description, 0, p.Amount ?? 0, 0));

        lines = lines.OrderBy(l => l.Date).ToList();

        /* ── الرصيد التراكمي ── */
        var bal = opening;
        for (var i = 0; i < lines.Count; i++)
        {
            bal += lines[i].Debit - lines[i].Credit;
            lines[i] = lines[i] with { Balance = bal };
        }

        return Ok(new StatementOut(vehicle.PlateNumber, opening, lines,
            lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), bal));
    }

    /* ═══════════════ PAYMENT — تسجيل دفعة على العربية ═══════════════ */

    [HttpPost("payment")]
    [Authorize(Policy = "PERM:TREASURY.MANAGE")]
    public async Task<IActionResult> Payment([FromBody] PaymentIn req, CancellationToken ct)
    {
        if (req.Amount <= 0)
            return BadRequest(new { message = "المبلغ لازم يكون أكتر من صفر" });

        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.VehicleId == req.VehicleId && !v.IsDeleted, ct);
        if (vehicle is null) return BadRequest(new { message = "العربية مش موجودة" });

        /* طريقة الدفع: لو مش محددة، أول طريقة دفع نشطة */
        var methodId = req.PaymentMethodId;
        if (methodId is null)
        {
            methodId = await _db.PaymentMethods.AsNoTracking()
                .Where(m => m.IsActive)
                .OrderBy(m => m.PaymentMethodId)
                .Select(m => (int?)m.PaymentMethodId)
                .FirstOrDefaultAsync(ct);
        }
        if (methodId is null)
            return BadRequest(new { message = "مافيش طريقة دفع نشطة في النظام" });

        /* تاريخ الدفعة */
        DateOnly? payDate = null;
        if (!string.IsNullOrWhiteSpace(req.TxDate) &&
            DateTime.TryParse(req.TxDate, out var parsed))
        {
            payDate = DateOnly.FromDateTime(parsed);
        }

        /* الخزينة — فلوس خرجت */
        var ok = await _cash.PaymentAsync(new CashEntry(
            $"VEHPAY:{req.VehicleId}:{DateTime.UtcNow:yyyyMMddHHmmss}",
            req.Amount,
            methodId.Value,
            Description: req.Description ?? $"دفعة على العربية {vehicle.PlateNumber}",
            Date: payDate), ct);

        if (!ok)
            return BadRequest(new { message = "مافيش خزينة مفتوحة — سجل الدفعة من شاشة الخزينة" });

        return Ok(new { message = $"اتسجلت دفعة {req.Amount:N0} على العربية {vehicle.PlateNumber}" });
    }

    /* ═══════════════ HELPERS ═══════════════ */

    /// <summary>الرصيد = نولون − عهدة − مصاريف − دفعات</summary>
    private async Task<decimal> CalcBalanceAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct)
    {
        var freight = await _db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted && t.VehicleId == vehicleId &&
                        t.ActualEndAt != null &&
                        t.ActualEndAt >= from && t.ActualEndAt < to)
            .SumAsync(t => t.FreightAmount, ct) ?? 0;

        var custody = await _db.DriverCustodies.AsNoTracking()
            .Where(c => !c.IsDeleted && c.OwnerType == "Driver" &&
                        c.Trip != null && c.Trip.VehicleId == vehicleId &&
                        c.Trip.ActualEndAt != null &&
                        c.Trip.ActualEndAt >= from && c.Trip.ActualEndAt < to)
            .SumAsync(c => (decimal?)c.AmountIssued, ct) ?? 0;

        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.VehicleId == vehicleId &&
                        e.ExpenseDate >= from && e.ExpenseDate < to &&
                        e.Status != "Cancelled")
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0;

        var payments = await _db.CashTransactions.AsNoTracking()
            .Where(t => !t.IsDeleted && t.VehicleId == vehicleId &&
                        t.Status == "Posted" &&
                        t.TransactionDate >= from && t.TransactionDate < to)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0;

        return freight - custody - expenses - payments;
    }
}
