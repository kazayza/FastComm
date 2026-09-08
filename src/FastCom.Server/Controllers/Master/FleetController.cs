using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>الأسطول: سيارات + مقطورات في controller واحد.</summary>
[ApiController]
[Route("api/fleet")]
[Authorize]
public class FleetController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly INumberingService _numbers;
    public FleetController(FastComDbContext db, INumberingService numbers)
    { _db = db; _numbers = numbers; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static DateOnly? D(string? s) => DateOnly.TryParse(s, out var d) ? d : null;

    // ═══════════════════ VEHICLES ═══════════════════

    public record VehicleItem(int VehicleId, string VehicleCode, string PlateNumber, string VehicleType,
        string? Brand, string OwnershipType, decimal? CapacityTon, string Status);

    public record VehicleDetail(int VehicleId, string VehicleCode, string PlateNumber, string VehicleType,
        string? Brand, string? Model, short? ModelYear, string OwnershipType, int? SupplierId,
        decimal? CapacityTon, byte ContainerSlots20, DateOnly? LicenseExpiryDate,
        DateOnly? InsuranceExpiryDate, string Status, string? Notes);

    public record VehicleUpsert(string PlateNumber, string VehicleType, string? Brand, string? Model,
        short? ModelYear, string OwnershipType, int? SupplierId, decimal? CapacityTon,
        byte ContainerSlots20, string? LicenseExpiryDate, string? InsuranceExpiryDate,
        string Status, string? Notes);

    [HttpGet("vehicles")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> Vehicles(CancellationToken ct) =>
        Ok(await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted)
            .OrderByDescending(v => v.VehicleId)
            .Select(v => new VehicleItem(v.VehicleId, v.VehicleCode, v.PlateNumber, v.VehicleType,
                v.Brand, v.OwnershipType, v.CapacityTon, v.Status))
            .ToListAsync(ct));

    [HttpGet("vehicles/{id:int}")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> GetVehicle(int id, CancellationToken ct)
    {
        var v = await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(x => x.VehicleId == id && !x.IsDeleted, ct);
        if (v is null) return NotFound(new { message = "العربية مش موجودة" });
        return Ok(new VehicleDetail(v.VehicleId, v.VehicleCode, v.PlateNumber, v.VehicleType,
            v.Brand, v.Model, v.ModelYear, v.OwnershipType, v.SupplierId, v.CapacityTon,
            v.ContainerSlots20, v.LicenseExpiryDate, v.InsuranceExpiryDate, v.Status, v.Notes));
    }

    [HttpPost("vehicles")]
    [Authorize(Policy = "PERM:FLEET.CREATE")]
    public async Task<IActionResult> CreateVehicle([FromBody] VehicleUpsert req, CancellationToken ct)
    {
        var err = ValidateVehicle(req);
        if (err is not null) return BadRequest(new { message = err });
        if (await PlateTakenVehiclesAsync(req.PlateNumber.Trim(), null, ct))
            return BadRequest(new { message = "رقم اللوحة ده مستخدم في عربية تانية" });

        var v = new Vehicle
        {
            VehicleCode = await _numbers.NextAsync("VEHICLE", ct),
            PlateNumber = req.PlateNumber.Trim(), VehicleType = req.VehicleType.Trim(),
            Brand = B(req.Brand), Model = B(req.Model), ModelYear = req.ModelYear,
            OwnershipType = req.OwnershipType, SupplierId = req.SupplierId,
            CapacityTon = req.CapacityTon, ContainerSlots20 = req.ContainerSlots20,
            LicenseExpiryDate = D(req.LicenseExpiryDate), InsuranceExpiryDate = D(req.InsuranceExpiryDate),
            Status = req.Status, Notes = B(req.Notes),
            CreatedBy = CurrentUserId()
        };
        _db.Vehicles.Add(v);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = v.VehicleId, message = $"✅ اتسجلت العربية بكود {v.VehicleCode}" });
    }

    [HttpPut("vehicles/{id:int}")]
    [Authorize(Policy = "PERM:FLEET.EDIT")]
    public async Task<IActionResult> UpdateVehicle(int id, [FromBody] VehicleUpsert req, CancellationToken ct)
    {
        var err = ValidateVehicle(req);
        if (err is not null) return BadRequest(new { message = err });

        var v = await _db.Vehicles.FirstOrDefaultAsync(x => x.VehicleId == id, ct);
        if (v is null) return NotFound(new { message = "العربية مش موجودة" });
        if (await PlateTakenVehiclesAsync(req.PlateNumber.Trim(), id, ct))
            return BadRequest(new { message = "رقم اللوحة ده مستخدم في عربية تانية" });

        v.PlateNumber = req.PlateNumber.Trim(); v.VehicleType = req.VehicleType.Trim();
        v.Brand = B(req.Brand); v.Model = B(req.Model); v.ModelYear = req.ModelYear;
        v.OwnershipType = req.OwnershipType; v.SupplierId = req.SupplierId;
        v.CapacityTon = req.CapacityTon; v.ContainerSlots20 = req.ContainerSlots20;
        v.LicenseExpiryDate = D(req.LicenseExpiryDate); v.InsuranceExpiryDate = D(req.InsuranceExpiryDate);
        v.Status = req.Status; v.Notes = B(req.Notes);
        v.UpdatedAt = DateTime.UtcNow; v.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    [HttpDelete("vehicles/{id:int}")]
    [Authorize(Policy = "PERM:FLEET.DELETE")]
    public async Task<IActionResult> DeleteVehicle(int id, CancellationToken ct)
    {
        var v = await _db.Vehicles.FirstOrDefaultAsync(x => x.VehicleId == id, ct);
        if (v is null) return NotFound(new { message = "العربية مش موجودة" });
        v.IsDeleted = true; v.Status = "OutOfService";
        v.DeletedAt = DateTime.UtcNow; v.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحذفت (ناعم)" });
    }

    // ═══════════════════ TRAILERS ═══════════════════

    public record TrailerItem(int TrailerId, string TrailerCode, string? PlateNumber, string TrailerType,
        byte? SizeFeet, string OwnershipType, string Status);

    public record TrailerDetail(int TrailerId, string TrailerCode, string? PlateNumber, string TrailerType,
        byte? SizeFeet, string OwnershipType, int? SupplierId, DateOnly? LicenseExpiryDate,
        DateOnly? InsuranceExpiryDate, string Status, string? Notes);

    public record TrailerUpsert(string? PlateNumber, string TrailerType, byte? SizeFeet,
        string OwnershipType, int? SupplierId, string? LicenseExpiryDate,
        string? InsuranceExpiryDate, string Status, string? Notes);

    [HttpGet("trailers")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> Trailers(CancellationToken ct) =>
        Ok(await _db.Trailers.AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderByDescending(t => t.TrailerId)
            .Select(t => new TrailerItem(t.TrailerId, t.TrailerCode, t.PlateNumber, t.TrailerType,
                t.SizeFeet, t.OwnershipType, t.Status))
            .ToListAsync(ct));

    [HttpGet("trailers/{id:int}")]
    [Authorize(Policy = "PERM:FLEET.VIEW")]
    public async Task<IActionResult> GetTrailer(int id, CancellationToken ct)
    {
        var t = await _db.Trailers.AsNoTracking().FirstOrDefaultAsync(x => x.TrailerId == id && !x.IsDeleted, ct);
        if (t is null) return NotFound(new { message = "المقطورة مش موجودة" });
        return Ok(new TrailerDetail(t.TrailerId, t.TrailerCode, t.PlateNumber, t.TrailerType,
            t.SizeFeet, t.OwnershipType, t.SupplierId, t.LicenseExpiryDate,
            t.InsuranceExpiryDate, t.Status, t.Notes));
    }

    [HttpPost("trailers")]
    [Authorize(Policy = "PERM:FLEET.CREATE")]
    public async Task<IActionResult> CreateTrailer([FromBody] TrailerUpsert req, CancellationToken ct)
    {
        var err = ValidateTrailer(req);
        if (err is not null) return BadRequest(new { message = err });
        var plateNew = B(req.PlateNumber);
        if (plateNew is not null && await PlateTakenTrailersAsync(plateNew, null, ct))
            return BadRequest(new { message = "رقم اللوحة ده مستخدم في مقطورة تانية" });

        var t = new Trailer
        {
            TrailerCode = await _numbers.NextAsync("TRAILER", ct),
            PlateNumber = B(req.PlateNumber), TrailerType = req.TrailerType.Trim(),
            SizeFeet = req.SizeFeet, OwnershipType = req.OwnershipType, SupplierId = req.SupplierId,
            LicenseExpiryDate = D(req.LicenseExpiryDate), InsuranceExpiryDate = D(req.InsuranceExpiryDate),
            Status = req.Status, Notes = B(req.Notes),
            CreatedBy = CurrentUserId()
        };
        _db.Trailers.Add(t);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = t.TrailerId, message = $"✅ اتسجلت المقطورة بكود {t.TrailerCode}" });
    }

    [HttpPut("trailers/{id:int}")]
    [Authorize(Policy = "PERM:FLEET.EDIT")]
    public async Task<IActionResult> UpdateTrailer(int id, [FromBody] TrailerUpsert req, CancellationToken ct)
    {
        var err = ValidateTrailer(req);
        if (err is not null) return BadRequest(new { message = err });

        var t = await _db.Trailers.FirstOrDefaultAsync(x => x.TrailerId == id, ct);
        if (t is null) return NotFound(new { message = "المقطورة مش موجودة" });
        var plateNew = B(req.PlateNumber);
        if (plateNew is not null && await PlateTakenTrailersAsync(plateNew, id, ct))
            return BadRequest(new { message = "رقم اللوحة ده مستخدم في مقطورة تانية" });

        t.PlateNumber = B(req.PlateNumber); t.TrailerType = req.TrailerType.Trim();
        t.SizeFeet = req.SizeFeet; t.OwnershipType = req.OwnershipType; t.SupplierId = req.SupplierId;
        t.LicenseExpiryDate = D(req.LicenseExpiryDate); t.InsuranceExpiryDate = D(req.InsuranceExpiryDate);
        t.Status = req.Status; t.Notes = B(req.Notes);
        t.UpdatedAt = DateTime.UtcNow; t.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    [HttpDelete("trailers/{id:int}")]
    [Authorize(Policy = "PERM:FLEET.DELETE")]
    public async Task<IActionResult> DeleteTrailer(int id, CancellationToken ct)
    {
        var t = await _db.Trailers.FirstOrDefaultAsync(x => x.TrailerId == id, ct);
        if (t is null) return NotFound(new { message = "المقطورة مش موجودة" });
        t.IsDeleted = true; t.Status = "OutOfService";
        t.DeletedAt = DateTime.UtcNow; t.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحذفت (ناعم)" });
    }

    // ═══════════════════ validation ═══════════════════

    // UQ_Vehicles_Plate / UQ_Trailers_Plate — الصفوف المحذوفة (ناعم) لسه حاجزة اللوحة
    private Task<bool> PlateTakenVehiclesAsync(string plate, int? exceptId, CancellationToken ct) =>
        _db.Vehicles.AnyAsync(x => x.PlateNumber == plate && x.VehicleId != (exceptId ?? 0), ct);

    private Task<bool> PlateTakenTrailersAsync(string plate, int? exceptId, CancellationToken ct) =>
        _db.Trailers.AnyAsync(x => x.PlateNumber == plate && x.TrailerId != (exceptId ?? 0), ct);

    private static readonly string[] VehStatuses = { "Available", "InTrip", "Maintenance", "OutOfService" };

    private static string? ValidateVehicle(VehicleUpsert? r)
    {
        if (r is null || string.IsNullOrWhiteSpace(r.PlateNumber)) return "رقم اللوحة مطلوب";
        if (string.IsNullOrWhiteSpace(r.VehicleType)) return "نوع العربية مطلوب";
        if (r.OwnershipType is not ("Company" or "External")) return "ملكية لازم شركة أو خارجي";
        if (!VehStatuses.Contains(r.Status)) return "الحالة مش صالحة";
        return null;
    }

    private static string? ValidateTrailer(TrailerUpsert? r)
    {
        if (r is null || string.IsNullOrWhiteSpace(r.TrailerType)) return "نوع المقطورة مطلوب";
        if (r.OwnershipType is not ("Company" or "External")) return "ملكية لازم شركة أو خارجي";
        if (!VehStatuses.Contains(r.Status)) return "الحالة مش صالحة";
        return null;
    }
}
