using ClosedXML.Excel;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Finance;

/// <summary>
/// كشف حساب العربية — زي ملف الإكسيل بالظبط.
/// <para>اختار عربية + شهر → رصيد مُرحّل ← نولون النقله (أجرة السائق = مستحق له) − عهدة − مصاريف طريق − دفعات ← رصيد آخر الشهر.</para>
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

    public record VehicleOpt(int VehicleId, string PlateNumber, string VehicleType, string Status,
        string? LastDriver, string? Drivers);

    public record StmtLine(DateTime Date, string Kind, string Ref, string? Description,
        decimal Debit, decimal Credit, decimal Balance, string? SubRef = null,
        string? Plate = null, string? Dest = null, string? Driver = null);

    public record SupplierStmtOut(string SupplierName, int VehicleCount, decimal Opening,
        List<StmtLine> Lines, decimal TotalDebit, decimal TotalCredit, decimal Closing);

    public record StatementOut(string PlateNumber, decimal Opening, List<StmtLine> Lines,
        decimal TotalDebit, decimal TotalCredit, decimal Closing);

    public record PaymentIn(int VehicleId, string? TxDate, decimal Amount,
        int? PaymentMethodId, string? Description);

    /* ═══════════════ LIST — العربيات ═══════════════ */

    [HttpGet("vehicles")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> Vehicles(CancellationToken ct)
    {
        var opts = await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted)
            .OrderBy(v => v.PlateNumber)
            .Select(v => new VehicleOpt(v.VehicleId, v.PlateNumber, v.VehicleType, v.Status, null, null))
            .ToListAsync(ct);

        /* 🔴 البحث باسم السائق: سائقين رحلات كل مركبة (الأحدث الأول) */
        var tripDrivers = await _db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted && t.ActualEndAt != null && t.Driver != null)
            .OrderByDescending(t => t.ActualEndAt)
            .Select(t => new { t.VehicleId, t.Driver.FullName })
            .ToListAsync(ct);

        var result = opts.Select(v =>
        {
            var names = tripDrivers
                .Where(d => d.VehicleId == v.VehicleId)
                .Select(d => d.FullName)
                .Distinct()
                .Take(4)
                .ToList();
            return v with
            {
                LastDriver = names.FirstOrDefault(),
                Drivers    = names.Count > 0 ? string.Join("، ", names) : null
            };
        }).ToList();

        return Ok(result);
    }

    /* ═══════════════ STATEMENT — كشف الحساب ═══════════════ */

    [HttpGet("{vehicleId:int}/statement")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> Statement(int vehicleId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var built = await BuildStatementAsync(vehicleId, from, to, ct);
        if (built is null) return NotFound(new { message = "العربية غير موجودة" });
        return Ok(built.Value.Stmt);
    }

    /* 🔴 #38 — تصدير كشف الحساب لإكسيل (RTL) */
    [HttpGet("{vehicleId:int}/export")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> Export(int vehicleId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var built = await BuildStatementAsync(vehicleId, from, to, ct);
        if (built is null) return NotFound(new { message = "العربية غير موجودة" });
        var (vehicle, s) = built.Value;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("كشف حساب");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = $"كشف حساب العربية — {s.PlateNumber}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        var heads = new[] { "التاريخ", "النوع", "الوجهة", "السائق", "المرجع", "البيان", "مدين", "دائن", "الرصيد" };
        for (var i = 0; i < heads.Length; i++)
        {
            var hc = ws.Cell(3, i + 1);
            hc.Value = heads[i];
            hc.Style.Font.Bold = true;
            hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF3FA");
            hc.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var r = 4;
        ws.Cell(r, 6).Value = "رصيد مُرحّل";
        ws.Cell(r, 6).Style.Font.Bold = true;
        ws.Cell(r, 9).Value = s.Opening;
        ws.Cell(r, 9).Style.Font.Bold = true;
        r++;
        foreach (var l in s.Lines)
        {
            ws.Cell(r, 1).Value = l.Date.ToString("yyyy-MM-dd");
            ws.Cell(r, 2).Value = KindAr(l.Kind);
            ws.Cell(r, 3).Value = l.Dest;
            ws.Cell(r, 4).Value = l.Driver;
            ws.Cell(r, 5).Value = string.IsNullOrWhiteSpace(l.SubRef) ? l.Ref : l.Ref + "\n" + l.SubRef;
            ws.Cell(r, 5).Style.Alignment.WrapText = true;
            ws.Cell(r, 6).Value = l.Description;
            ws.Cell(r, 7).Value = l.Debit;
            ws.Cell(r, 8).Value = l.Credit;
            ws.Cell(r, 9).Value = l.Balance;
            r++;
        }
        ws.Cell(r, 6).Value = "الإجمالي";
        ws.Cell(r, 7).Value = s.TotalDebit;
        ws.Cell(r, 8).Value = s.TotalCredit;
        ws.Cell(r, 9).Value = s.Closing;
        for (var i = 6; i <= 9; i++) ws.Cell(r, i).Style.Font.Bold = true;

        ws.Columns(1, 9).AdjustToContents(1, Math.Max(r, 5));
        ws.Column(7).Width = 14; ws.Column(8).Width = 14; ws.Column(9).Width = 14;
        ws.Range(4, 7, r, 9).Style.NumberFormat.Format = "#,##0.00";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"vehicle-account-{vehicle.VehicleCode}-{DateTime.Today:yyyyMM}.xlsx");
    }

    private static string KindAr(string k) => k switch
    {
        "Freight"        => "نولون النقله",
        "CustodyFreight" => "سلفة نولون",
        "CustodyRoad"    => "مصاريف طريق (عهدة)",
        "Expense"        => "مصروف",
        "Payment"        => "دفعة",
        _ => k
    };

    /// <summary>🔴 #36 — بناء الكشف: العهد بتتقسم حسب نوعها، والنولون ما بيتعدش مرتين مع السلفة.</summary>
    private async Task<(Vehicle Vehicle, StatementOut Stmt)?> BuildStatementAsync(
        int vehicleId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.VehicleId == vehicleId && !v.IsDeleted, ct);
        if (vehicle is null) return null;

        var start = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var end   = (to ?? start.AddMonths(1)).Date;
        if (end <= start) end = start.AddMonths(1);

        /* ── الرصيد المرحّل: كل الحركات من الأول لحد أول الشهر ── */
        var opening = await CalcBalanceAsync(vehicleId, DateTime.MinValue, start, ct);

        /* ── الرحلات: النولون المتفق عليه + العهد مقسومة حسب النوع (#36) ── */
        var trips = await _db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted && t.VehicleId == vehicleId &&
                        t.ActualEndAt != null &&
                        t.ActualEndAt >= start && t.ActualEndAt < end)
            .OrderBy(t => t.ActualEndAt)
            .Select(t => new
            {
                Date = t.ActualEndAt!.Value,
                t.TripNumber,
                Freight = (decimal?)(t.FreightAmount ?? 0m),
                CustodyFreight = _db.DriverCustodies
                    .Where(c => !c.IsDeleted && c.TripId == t.TripId && c.OwnerType == "Driver"
                                && c.AllocationType == "Freight")
                    .Sum(c => (decimal?)c.AmountIssued),
                CustodyRoad = _db.DriverCustodies
                    .Where(c => !c.IsDeleted && c.TripId == t.TripId && c.OwnerType == "Driver"
                                && c.AllocationType != "Freight")
                    .Sum(c => (decimal?)c.AmountIssued),
                BookingNumber = _db.TripOperations
                    .Where(x => x.TripId == t.TripId && x.Operation != null && x.Operation.Booking != null)
                    .Select(x => x.Operation!.Booking!.BookingNumber)
                    .FirstOrDefault(),
                Dest   = t.Destination != null ? t.Destination.NameAr : null,
                Driver = t.Driver != null ? t.Driver.FullName : null
            })
            .ToListAsync(ct);

        /* ── المصروفات في الفترة ── */
        var expenses = await _db.Expenses.AsNoTracking()
            // مصروفات العهدة (CustodyId != null) مستثناة — فلوسها اتخصمت وقت صرف العهدة نفسها
            .Where(e => !e.IsDeleted && e.VehicleId == vehicleId && e.CustodyId == null &&
                        e.ExpenseDate >= start && e.ExpenseDate < end &&
                        e.Status != "Cancelled")
            .OrderBy(e => e.ExpenseDate)
            .Select(e => new
            {
                e.ExpenseDate,
                e.ExpenseNumber,
                e.Description,
                Amount = (decimal?)e.Amount,
                ExpDest   = e.Trip != null && e.Trip.Destination != null ? e.Trip.Destination.NameAr : null,
                ExpDriver = e.Driver != null ? e.Driver.FullName : null
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

        /* ── نبني السطور ── */
        var lines = new List<StmtLine>();

        foreach (var t in trips)
        {
            /* #36: النولون المتفق عليه يُعد فقط لو ما اتدفعش بسلفة نولون — مافيش عد مزدوج */
            if (t.Freight is > 0 && t.CustodyFreight is not > 0)
                lines.Add(new StmtLine(t.Date, "Freight", t.TripNumber, "نولون النقله (أجرة السائق)", t.Freight.Value, 0, 0, t.BookingNumber, Dest: t.Dest, Driver: t.Driver));
            if (t.CustodyFreight is > 0)
                lines.Add(new StmtLine(t.Date, "CustodyFreight", t.TripNumber, "سلفة نولون (من عهدة السائق)", 0, t.CustodyFreight.Value, 0, t.BookingNumber, Dest: t.Dest, Driver: t.Driver));
            if (t.CustodyRoad is > 0)
                lines.Add(new StmtLine(t.Date, "CustodyRoad", t.TripNumber, "مصاريف طريق (من عهدة السائق)", 0, t.CustodyRoad.Value, 0, t.BookingNumber, Dest: t.Dest, Driver: t.Driver));
        }

        foreach (var e in expenses)
            lines.Add(new StmtLine(e.ExpenseDate, "Expense", e.ExpenseNumber,
                e.Description, 0, e.Amount ?? 0, 0,
                Dest: e.ExpDest, Driver: e.ExpDriver));

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

        return (vehicle, new StatementOut(vehicle.PlateNumber, opening, lines,
            lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), bal));
    }

    /* ═══════════════ #45 — كشف مجمع لكل سيارات مورد ═══════════════ */

    private async Task<SupplierStmtOut?> BuildSupplierStatementAsync(
        int supplierId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var supName = await _db.Suppliers.AsNoTracking()
            .Where(s => s.SupplierId == supplierId)
            .Select(s => s.NameAr).FirstOrDefaultAsync(ct);
        if (supName is null) return null;

        var vehicleIds = await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted && v.SupplierId == supplierId)
            .OrderBy(v => v.PlateNumber)
            .Select(v => v.VehicleId).ToListAsync(ct);

        decimal opening = 0;
        var all = new List<StmtLine>();
        foreach (var vid in vehicleIds)
        {
            var built = await BuildStatementAsync(vid, from, to, ct);
            if (built is null) continue;
            opening += built.Value.Stmt.Opening;
            foreach (var l in built.Value.Stmt.Lines)
                all.Add(l with { Plate = built.Value.Vehicle.PlateNumber, Balance = 0 });
        }

        all = all.OrderBy(l => l.Date).ToList();
        var bal = opening;
        for (var i = 0; i < all.Count; i++)
        {
            bal += all[i].Debit - all[i].Credit;
            all[i] = all[i] with { Balance = bal };
        }

        return new SupplierStmtOut(supName, vehicleIds.Count, opening, all,
            all.Sum(l => l.Debit), all.Sum(l => l.Credit), bal);
    }

    [HttpGet("by-supplier/{supplierId:int}/statement")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> SupplierStatement(int supplierId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var s = await BuildSupplierStatementAsync(supplierId, from, to, ct);
        if (s is null) return NotFound(new { message = "المورد غير موجود" });
        return Ok(s);
    }

    [HttpGet("by-supplier/{supplierId:int}/export")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> SupplierExport(int supplierId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var s = await BuildSupplierStatementAsync(supplierId, from, to, ct);
        if (s is null) return NotFound(new { message = "المورد غير موجود" });

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("كشف مجمع");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = $"كشف حساب مجمع — مورد: {s.SupplierName} ({s.VehicleCount} مركبة)";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        var heads = new[] { "التاريخ", "المركبة", "النوع", "الوجهة", "السائق", "المرجع", "البيان", "مدين", "دائن", "الرصيد" };
        for (var i = 0; i < heads.Length; i++)
        {
            var hc = ws.Cell(3, i + 1);
            hc.Value = heads[i];
            hc.Style.Font.Bold = true;
            hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF3FA");
            hc.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var r = 4;
        ws.Cell(r, 7).Value = "رصيد مُرحّل (كل المركبات)";
        ws.Cell(r, 7).Style.Font.Bold = true;
        ws.Cell(r, 10).Value = s.Opening;
        ws.Cell(r, 10).Style.Font.Bold = true;
        r++;
        foreach (var l in s.Lines)
        {
            ws.Cell(r, 1).Value = l.Date.ToString("yyyy-MM-dd");
            ws.Cell(r, 2).Value = l.Plate;
            ws.Cell(r, 3).Value = KindAr(l.Kind);
            ws.Cell(r, 4).Value = l.Dest;
            ws.Cell(r, 5).Value = l.Driver;
            ws.Cell(r, 6).Value = string.IsNullOrWhiteSpace(l.SubRef) ? l.Ref : l.Ref + "\n" + l.SubRef;
            ws.Cell(r, 6).Style.Alignment.WrapText = true;
            ws.Cell(r, 7).Value = l.Description;
            ws.Cell(r, 8).Value = l.Debit;
            ws.Cell(r, 9).Value = l.Credit;
            ws.Cell(r, 10).Value = l.Balance;
            r++;
        }
        ws.Cell(r, 7).Value = "الإجمالي";
        ws.Cell(r, 8).Value = s.TotalDebit;
        ws.Cell(r, 9).Value = s.TotalCredit;
        ws.Cell(r, 10).Value = s.Closing;
        for (var i = 7; i <= 10; i++) ws.Cell(r, i).Style.Font.Bold = true;

        ws.Columns(1, 10).AdjustToContents(1, Math.Max(r, 5));
        ws.Column(8).Width = 14; ws.Column(9).Width = 14; ws.Column(10).Width = 14;
        ws.Range(4, 8, r, 10).Style.NumberFormat.Format = "#,##0.00";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"supplier-vehicles-{supplierId}-{DateTime.Today:yyyyMM}.xlsx");
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
            VehicleId: req.VehicleId,
            Description: req.Description ?? $"دفعة على العربية {vehicle.PlateNumber}",
            Date: payDate), ct);

        if (!ok)
            return BadRequest(new { message = "مافيش خزينة مفتوحة — سجل الدفعة من شاشة الخزينة" });

        /* 🔴 #43 — دالة الخزينة بتضيف الحركة من غير حفظ — كل كنترولر بيحفظ لنفسه
           (نفس نمط SupplierPayments) — الحفظ ده كان ناقص فالدفعة كانت بتضيع */
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = $"اتسجلت دفعة {req.Amount:N0} على العربية {vehicle.PlateNumber}" });
    }

    /* ═══════════════ HELPERS ═══════════════ */

    /// <summary>الرصيد = نولون النقله (أجرة السائق) − عهدة − مصاريف طريق − دفعات</summary>
    private async Task<decimal> CalcBalanceAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct)
    {
        /* #36: نولون الرحلات اللي ليها سلفة نولون ما بيتعدش — السلفة نفسها هي الدفع */
        var freight = await _db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted && t.VehicleId == vehicleId &&
                        t.ActualEndAt != null &&
                        t.ActualEndAt >= from && t.ActualEndAt < to &&
                        !_db.DriverCustodies.Any(c => !c.IsDeleted && c.OwnerType == "Driver" &&
                            c.TripId == t.TripId && c.AllocationType == "Freight"))
            .SumAsync(t => t.FreightAmount, ct) ?? 0;

        var custody = await _db.DriverCustodies.AsNoTracking()
            .Where(c => !c.IsDeleted && c.OwnerType == "Driver" &&
                        c.Trip != null && c.Trip.VehicleId == vehicleId &&
                        c.Trip.ActualEndAt != null &&
                        c.Trip.ActualEndAt >= from && c.Trip.ActualEndAt < to)
            .SumAsync(c => (decimal?)c.AmountIssued, ct) ?? 0;

        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.VehicleId == vehicleId && e.CustodyId == null &&
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
