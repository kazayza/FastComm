using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

[ApiController]
[Route("api/drivers")]
[Authorize]
public class DriversController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly INumberingService _numbers;
    public DriversController(FastComDbContext db, INumberingService numbers)
    { _db = db; _numbers = numbers; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    public record ListItem(int DriverId, string DriverCode, string DriverType, string FullName,
        string? Mobile, DateOnly? LicenseExpiryDate, string Status);

    public record Detail(int DriverId, string DriverCode, string DriverType, int? EmployeeId, int? SupplierId,
        string FullName, string? NationalId, string? Mobile, string? LicenseNumber, string? LicenseType,
        DateOnly? LicenseExpiryDate, string Status, string? Notes);

    public record Upsert(string DriverType, int? EmployeeId, int? SupplierId, string FullName,
        string? NationalId, string? Mobile, string? LicenseNumber, string? LicenseType,
        string? LicenseExpiryDate, string Status, string? Notes);

    [HttpGet]
    [Authorize(Policy = "PERM:DRIVER.VIEW")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _db.Drivers.AsNoTracking()
            .Where(d => !d.IsDeleted)
            .OrderByDescending(d => d.DriverId)
            .Select(d => new ListItem(d.DriverId, d.DriverCode, d.DriverType, d.FullName,
                d.Mobile, d.LicenseExpiryDate, d.Status))
            .ToListAsync(ct));

    [HttpGet("{id:int}")]
    [Authorize(Policy = "PERM:DRIVER.VIEW")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var d = await _db.Drivers.AsNoTracking().FirstOrDefaultAsync(x => x.DriverId == id && !x.IsDeleted, ct);
        if (d is null) return NotFound(new { message = "السائق مش موجود" });
        return Ok(new Detail(d.DriverId, d.DriverCode, d.DriverType, d.EmployeeId, d.SupplierId,
            d.FullName, d.NationalId, d.Mobile, d.LicenseNumber, d.LicenseType,
            d.LicenseExpiryDate, d.Status, d.Notes));
    }

    [HttpPost]
    [Authorize(Policy = "PERM:DRIVER.CREATE")]
    public async Task<IActionResult> Create([FromBody] Upsert req, CancellationToken ct)
    {
        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        var d = new Driver
        {
            DriverCode = await _numbers.NextAsync("DRIVER", ct),
            DriverType = req.DriverType, EmployeeId = req.EmployeeId, SupplierId = req.SupplierId,
            FullName = req.FullName.Trim(), NationalId = B(req.NationalId), Mobile = B(req.Mobile),
            LicenseNumber = B(req.LicenseNumber), LicenseType = B(req.LicenseType),
            LicenseExpiryDate = ParseDate(req.LicenseExpiryDate),
            Status = req.Status, Notes = B(req.Notes),
            CreatedBy = CurrentUserId()
        };
        // CK_Drivers_Internal/External: العلاقة المقابلة لازم تكون NULL
        if (d.DriverType == "Internal") d.SupplierId = null; else d.EmployeeId = null;
        _db.Drivers.Add(d);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = d.DriverId, message = $"✅ اتعمل السائق بكود {d.DriverCode}" });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:DRIVER.EDIT")]
    public async Task<IActionResult> Update(int id, [FromBody] Upsert req, CancellationToken ct)
    {
        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        var d = await _db.Drivers.FirstOrDefaultAsync(x => x.DriverId == id, ct);
        if (d is null) return NotFound(new { message = "السائق مش موجود" });

        d.DriverType = req.DriverType; d.EmployeeId = req.EmployeeId; d.SupplierId = req.SupplierId;
        d.FullName = req.FullName.Trim(); d.NationalId = B(req.NationalId); d.Mobile = B(req.Mobile);
        d.LicenseNumber = B(req.LicenseNumber); d.LicenseType = B(req.LicenseType);
        d.LicenseExpiryDate = ParseDate(req.LicenseExpiryDate);
        d.Status = req.Status; d.Notes = B(req.Notes);
        // CK_Drivers_Internal/External: العلاقة المقابلة لازم تكون NULL
        if (d.DriverType == "Internal") d.SupplierId = null; else d.EmployeeId = null;
        d.UpdatedAt = DateTime.UtcNow; d.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:DRIVER.DELETE")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var d = await _db.Drivers.FirstOrDefaultAsync(x => x.DriverId == id, ct);
        if (d is null) return NotFound(new { message = "السائق مش موجود" });
        d.IsDeleted = true; d.Status = "Inactive";
        d.DeletedAt = DateTime.UtcNow; d.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحذف (ناعم)" });
    }

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : null;

    private static string? Validate(Upsert? r)
    {
        if (r is null || string.IsNullOrWhiteSpace(r.FullName)) return "الاسم مطلوب";
        if (r.DriverType is not ("Internal" or "External")) return "النوع لازم داخلي أو خارجي";
        if (r.DriverType == "Internal" && r.EmployeeId is null)
            return "السائق الداخلي لازم يرتبط بموظف";
        if (r.DriverType == "External" && r.SupplierId is null)
            return "السائق الخارجي لازم يرتبط بمورد";
        if (r.Status is not ("Active" or "Inactive" or "Suspended")) return "الحالة مش صالحة";
        return null;
    }
}
