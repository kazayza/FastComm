using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

[ApiController]
[Route("api/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private static readonly string[] Types =
    {
        "TransportCompany", "FuelStation", "Workshop", "PortServices", "PartsSupplier", "Other"
    };

    private readonly FastComDbContext _db;
    private readonly INumberingService _numbers;
    public SuppliersController(FastComDbContext db, INumberingService numbers)
    { _db = db; _numbers = numbers; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    public record ListItem(int SupplierId, string SupplierCode, string SupplierType,
        string NameAr, string? Phone, string? TaxNumber, bool IsActive);

    public record Detail(int SupplierId, string SupplierCode, string SupplierType, string NameAr,
        string? NameEn, string? TaxNumber, string? Phone, string? Email, string? Address, int? PaymentTermId, bool IsActive);

    public record Upsert(string SupplierType, string NameAr, string? NameEn, string? TaxNumber,
        string? Phone, string? Email, string? Address, int? PaymentTermId);

    [HttpGet]
    [Authorize(Policy = "PERM:SUPPLIER.VIEW")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _db.Suppliers.AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.SupplierId)
            .Select(s => new ListItem(s.SupplierId, s.SupplierCode, s.SupplierType,
                s.NameAr, s.Phone, s.TaxNumber, s.IsActive))
            .ToListAsync(ct));

    [HttpGet("{id:int}")]
    [Authorize(Policy = "PERM:SUPPLIER.VIEW")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var s = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.SupplierId == id && !x.IsDeleted, ct);
        if (s is null) return NotFound(new { message = "المورد مش موجود" });
        return Ok(new Detail(s.SupplierId, s.SupplierCode, s.SupplierType, s.NameAr, s.NameEn,
            s.TaxNumber, s.Phone, s.Email, s.Address, s.PaymentTermId, s.IsActive));
    }

    [HttpPost]
    [Authorize(Policy = "PERM:SUPPLIER.CREATE")]
    public async Task<IActionResult> Create([FromBody] Upsert req, CancellationToken ct)
    {
        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        var s = new Supplier
        {
            SupplierCode = await _numbers.NextAsync("SUPPLIER", ct),
            SupplierType = req.SupplierType,
            NameAr = req.NameAr.Trim(),
            NameEn = B(req.NameEn), TaxNumber = B(req.TaxNumber),
            Phone = B(req.Phone), Email = B(req.Email), Address = B(req.Address),
            PaymentTermId = req.PaymentTermId,
            IsActive = true, CreatedBy = CurrentUserId()
        };
        _db.Suppliers.Add(s);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = s.SupplierId, message = $"✅ اتعمل المورد بكود {s.SupplierCode}" });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:SUPPLIER.EDIT")]
    public async Task<IActionResult> Update(int id, [FromBody] Upsert req, CancellationToken ct)
    {
        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.SupplierId == id, ct);
        if (s is null) return NotFound(new { message = "المورد مش موجود" });

        s.SupplierType = req.SupplierType; s.NameAr = req.NameAr.Trim();
        s.NameEn = B(req.NameEn); s.TaxNumber = B(req.TaxNumber);
        s.Phone = B(req.Phone); s.Email = B(req.Email); s.Address = B(req.Address);
        s.PaymentTermId = req.PaymentTermId;
        s.UpdatedAt = DateTime.UtcNow; s.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    [HttpPost("{id:int}/toggle-active")]
    [Authorize(Policy = "PERM:SUPPLIER.EDIT")]
    public async Task<IActionResult> Toggle(int id, CancellationToken ct)
    {
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.SupplierId == id, ct);
        if (s is null) return NotFound(new { message = "المورد مش موجود" });
        s.IsActive = !s.IsActive;
        s.UpdatedAt = DateTime.UtcNow; s.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = s.IsActive ? "✅ اتفعّل" : "✅ اتوقف" });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:SUPPLIER.DELETE")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.SupplierId == id, ct);
        if (s is null) return NotFound(new { message = "المورد مش موجود" });
        s.IsDeleted = true; s.IsActive = false;
        s.DeletedAt = DateTime.UtcNow; s.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحذف (ناعم)" });
    }

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Validate(Upsert? r)
    {
        if (r is null || string.IsNullOrWhiteSpace(r.NameAr)) return "الاسم بالعربي مطلوب";
        if (!Types.Contains(r.SupplierType)) return "نوع المورد مش صالح";
        return null;
    }
}
