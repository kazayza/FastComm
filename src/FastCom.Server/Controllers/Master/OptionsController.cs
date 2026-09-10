using System.Security.Claims;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// قوائم منسدلة خفيفة لكل الشاشات (selects) — {id, label}.
/// </summary>
[ApiController]
[Route("api/options")]
[Authorize]
public class OptionsController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly IPermissionService _perms;

    public OptionsController(FastComDbContext db, IPermissionService perms)
    { _db = db; _perms = perms; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    public record Opt(int Id, string Label);

    [HttpGet("customers")]
    [Authorize(Policy = "PERM:CUSTOMER.VIEW")]
    public async Task<IActionResult> Customers(CancellationToken ct) =>
        Ok(await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive && !c.IsDeleted)
            .OrderBy(c => c.NameAr)
            .Select(c => new Opt(c.CustomerId, c.NameAr + " (" + c.CustomerCode + ")"))
            .ToListAsync(ct));

    [HttpGet("suppliers")]
    [Authorize(Policy = "PERM:SUPPLIER.VIEW")]
    public async Task<IActionResult> Suppliers(CancellationToken ct) =>
        Ok(await _db.Suppliers.AsNoTracking()
            .Where(s => s.IsActive && !s.IsDeleted)
            .OrderBy(s => s.NameAr)
            .Select(s => new Opt(s.SupplierId, s.NameAr + " (" + s.SupplierCode + ")"))
            .ToListAsync(ct));

    [HttpGet("drivers")]
    [Authorize(Policy = "PERM:DRIVER.VIEW")]
    public async Task<IActionResult> Drivers(CancellationToken ct) =>
        Ok(await _db.Drivers.AsNoTracking()
            .Where(d => d.Status == "Active" && !d.IsDeleted)
            .OrderBy(d => d.FullName)
            .Select(d => new Opt(d.DriverId, d.FullName + " (" + d.DriverCode + ")"))
            .ToListAsync(ct));

    [HttpGet("employees")]
    [Authorize(Policy = "PERM:EMPLOYEE.VIEW")]
    public async Task<IActionResult> Employees(CancellationToken ct) =>
        Ok(await _db.Employees.AsNoTracking()
            .Where(e => !e.IsDeleted && e.EmploymentStatus != "Terminated")
            .OrderBy(e => e.FullNameAr)
            .Select(e => new Opt(e.EmployeeId, e.FullNameAr))
            .ToListAsync(ct));

    [HttpGet("departments")]
    [Authorize(Policy = "PERM:EMPLOYEE.VIEW")]
    public async Task<IActionResult> Departments(CancellationToken ct) =>
        Ok(await _db.Departments.AsNoTracking()
            .Where(d => d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.NameAr)
            .Select(d => new Opt(d.DepartmentId, d.NameAr))
            .ToListAsync(ct));

    [HttpGet("job-titles")]
    [Authorize(Policy = "PERM:EMPLOYEE.VIEW")]
    public async Task<IActionResult> JobTitles(CancellationToken ct) =>
        Ok(await _db.JobTitles.AsNoTracking()
            .Where(j => j.IsActive && !j.IsDeleted)
            .OrderBy(j => j.NameAr)
            .Select(j => new Opt(j.JobTitleId, j.NameAr))
            .ToListAsync(ct));

    [HttpGet("vehicles")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> Vehicles(CancellationToken ct) =>
        Ok(await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted && v.Status != "OutOfService")
            .OrderBy(v => v.PlateNumber)
            .Select(v => new Opt(v.VehicleId, v.PlateNumber + " — " + v.VehicleType))
            .ToListAsync(ct));

    [HttpGet("trailers")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> Trailers(CancellationToken ct) =>
        Ok(await _db.Trailers.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status != "OutOfService")
            .OrderBy(t => t.TrailerCode)
            .Select(t => new Opt(t.TrailerId, (t.PlateNumber ?? t.TrailerCode) + " — " + t.TrailerType))
            .ToListAsync(ct));

    // ── قوائم الحجوزات (Step 6) ──

    [HttpGet("ports")]
    [Authorize(Policy = "PERM:BOOKING.VIEW")]
    public async Task<IActionResult> Ports(CancellationToken ct) =>
        Ok(await _db.Ports.AsNoTracking()
            .Where(p => p.IsActive && !p.IsDeleted)
            .OrderBy(p => p.NameAr)
            .Select(p => new Opt(p.PortId, p.NameAr))
            .ToListAsync(ct));

    [HttpGet("destinations")]
    [Authorize(Policy = "PERM:BOOKING.VIEW")]
    public async Task<IActionResult> Destinations(CancellationToken ct) =>
        Ok(await _db.Destinations.AsNoTracking()
            .Where(d => d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.NameAr)
            .Select(d => new Opt(d.DestinationId, d.NameAr))
            .ToListAsync(ct));

    [HttpGet("trip-types")]
    [Authorize(Policy = "PERM:BOOKING.VIEW")]
    public async Task<IActionResult> TripTypes(CancellationToken ct) =>
        Ok(await _db.TripTypes.AsNoTracking()
            .Where(t => t.IsActive && !t.IsDeleted)
            .OrderBy(t => t.TripTypeId)
            .Select(t => new Opt(t.TripTypeId, t.NameAr))
            .ToListAsync(ct));

    [HttpGet("services")]
    [Authorize(Policy = "PERM:BOOKING.VIEW")]
    public async Task<IActionResult> Services(CancellationToken ct) =>
        Ok(await _db.Services.AsNoTracking()
            .Where(s => s.IsActive && !s.IsDeleted)
            .OrderBy(s => s.ServiceCode)
            .Select(s => new Opt(s.ServiceId, s.NameAr))
            .ToListAsync(ct));

    [HttpGet("container-types")]
    [Authorize]
    public async Task<IActionResult> ContainerTypes(CancellationToken ct)
    {
        // بتستخدمها شاشة الحجوزات وشاشة الحاويات — تكفي أي واحدة من الصلاحيتين
        var uid = CurrentUserId();
        if (!await _perms.HasAsync(uid, "BOOKING.VIEW", ct) &&
            !await _perms.HasAsync(uid, "MASTERDATA.VIEW", ct))
            return StatusCode(403, new { message = "ليس لديك  صلاحية" });

        return Ok(await _db.ContainerTypes.AsNoTracking()
            .Where(c => c.IsActive && !c.IsDeleted)
            .OrderBy(c => c.SizeFeet).ThenBy(c => c.Code)
            .Select(c => new Opt(c.ContainerTypeId, c.NameAr))
            .ToListAsync(ct));
    }

    [HttpGet("contacts")]
    [Authorize(Policy = "PERM:BOOKING.VIEW")]
    public async Task<IActionResult> Contacts([FromQuery] int customerId, CancellationToken ct) =>
        Ok(await _db.CustomerContacts.AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.IsActive)
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.ContactName)
            .Select(c => new Opt(c.CustomerContactId,
                c.ContactName + (c.Mobile != null ? " — " + c.Mobile : "")))
            .ToListAsync(ct));

    [HttpGet("expense-types")]
    [Authorize(Policy = "PERM:EXPENSE.VIEW")]
    public async Task<IActionResult> ExpenseTypes(CancellationToken ct) =>
        Ok(await _db.ExpenseTypes.AsNoTracking()
            .Where(e => e.IsActive && !e.IsDeleted)
            .OrderBy(e => e.Code)
            .Select(e => new ExpenseTypeOpt(e.ExpenseTypeId, e.NameAr,
                e.IsCustodyAllowed, e.IsOperationCost, e.IsTaxDeductible))
            .ToListAsync(ct));

    public record ExpenseTypeOpt(int Id, string Label, bool CustodyAllowed,
        bool IsOperationCost, bool IsTaxDeductible);

    /// <summary>العهود المفتوحة على رحلة — بتظهر في فورم المصروف.</summary>
    [HttpGet("custodies")]
    [Authorize(Policy = "PERM:EXPENSE.VIEW")]
    public async Task<IActionResult> Custodies(long tripId, CancellationToken ct) =>
        Ok(await _db.DriverCustodies.AsNoTracking()
            .Where(c => !c.IsDeleted && c.TripId == tripId &&
                        c.Status != "Closed" && c.Status != "Approved")
            .OrderBy(c => c.CustodyNumber)
            .Select(c => new CustodyOpt(c.CustodyId, c.CustodyNumber, c.OwnerType, c.OwnerId,
                                        c.TripId, c.AmountIssued))
            .ToListAsync(ct));

    public record CustodyOpt(long Id, string Label, string OwnerType, int OwnerId,
                             long TripId, decimal AmountIssued);

    [HttpGet("tax-rates")]
    [Authorize(Policy = "PERM:OPERATION.VIEW")]
    public async Task<IActionResult> TaxRates(CancellationToken ct) =>
        Ok(await _db.TaxRates.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.TaxRateId)
            .Select(t => new TaxOpt(t.TaxRateId, t.Code + " — " + t.Rate.ToString("0.##") + "%", t.Rate))
            .ToListAsync(ct));

    public record TaxOpt(int Id, string Label, decimal Value);

    /// <summary>طرق الدفع — بتستخدمها شاشة التحصيل وفاتورة المورد.</summary>
    [HttpGet("payment-methods")]
    [Authorize(Policy = "PERM:INVOICE.VIEW")]
    public async Task<IActionResult> PaymentMethods(CancellationToken ct) =>
        Ok(await _db.PaymentMethods.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.PaymentMethodId)
            .Select(p => new PaymentMethodOpt(p.PaymentMethodId, p.NameAr, p.Code,
                                              p.RequiresChequeNo, p.IsCashBased))
            .ToListAsync(ct));

    public record PaymentMethodOpt(int Id, string Label, string Code,
                                   bool RequiresChequeNo, bool IsCashBased);

    [HttpGet("payment-terms")]
    [Authorize(Policy = "PERM:CUSTOMER.VIEW")]
    public async Task<IActionResult> PaymentTerms(CancellationToken ct) =>
        Ok(await _db.PaymentTerms.AsNoTracking()
            .Where(p => p.IsActive && !p.IsDeleted)
            .OrderBy(p => p.DueDays)
            .Select(p => new Opt(p.PaymentTermId, p.NameAr + " (" + p.DueDays + " يوم)"))
            .ToListAsync(ct));
}
