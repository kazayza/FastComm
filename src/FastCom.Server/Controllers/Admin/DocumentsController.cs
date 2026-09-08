using System.Security.Claims;
using System.Security.Cryptography;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Admin;

/// <summary>
/// المستندات — رخص، وثائق تأمين، إثبات تسليم، إيصالات...
/// <para>الملفات بتتحفظ على ديسك السيرفر تحت <c>wwwroot/App_Data/documents</c>
/// باسم مولّد (GUID) — <b>اسم ملف المستخدم عمره ما بيلمس المسار</b>.</para>
/// </summary>
[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private const long MaxBytes = 10 * 1024 * 1024;   // 10 MB

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".webp",
        ".doc", ".docx", ".xls", ".xlsx", ".txt", ".zip"
    };

    private static readonly string[] EntityTypes =
    {
        "Customer","Supplier","Employee","Driver","Vehicle","Trailer",
        "Booking","Operation","Trip","Container","Invoice","SupplierInvoice",
        "Expense","Custody","Payment","Maintenance","Other"
    };

    private readonly FastComDbContext _db;
    private readonly IWebHostEnvironment _env;

    public DocumentsController(FastComDbContext db, IWebHostEnvironment env)
    { _db = db; _env = env; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static DateOnly? Dn(string? s) => DateOnly.TryParse(s, out var d) ? d : null;

    // ═══════════════ DTOs ═══════════════

    public record ListItem(long DocumentId, string TypeCode, string TypeName, bool HasExpiry,
        string EntityType, long EntityId, string FileName, string? OriginalFileName,
        string? ContentType, long? FileSizeBytes, string? ExpiryDate, string? Description,
        DateTime UploadedAt, bool Expired);

    public record DocTypeOpt(int Id, string Label, string Code, bool HasExpiry);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:DOCUMENT.DOWNLOAD")]
    public async Task<IActionResult> List(string? entityType, long? entityId, int? typeId,
        string? q, int take = 300, CancellationToken ct = default)
    {
        if (take <= 0 || take > 1000) take = 300;

        var query = _db.Documents.AsNoTracking().Where(d => !d.IsDeleted);

        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(d => d.EntityType == entityType);
        if (entityId is not null)                   query = query.Where(d => d.EntityId == entityId);
        if (typeId is not null)                     query = query.Where(d => d.DocumentTypeId == typeId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(d => d.FileName.Contains(s) ||
                                     (d.OriginalFileName != null && d.OriginalFileName.Contains(s)) ||
                                     (d.Description != null && d.Description.Contains(s)));
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var rows = await query
            .OrderByDescending(d => d.UploadedAt)
            .Take(take)
            .Select(d => new ListItem(
                d.DocumentId, d.DocumentType.Code, d.DocumentType.NameAr, d.DocumentType.HasExpiry,
                d.EntityType, d.EntityId, d.FileName, d.OriginalFileName,
                d.ContentType, d.FileSizeBytes,
                d.ExpiryDate == null ? null : d.ExpiryDate.Value.ToString("yyyy-MM-dd"),
                d.Description, d.UploadedAt,
                d.ExpiryDate != null && d.ExpiryDate < today))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ أنواع المستندات ═══════════════

    [HttpGet("types")]
    [Authorize(Policy = "PERM:DOCUMENT.DOWNLOAD")]
    public async Task<IActionResult> Types(CancellationToken ct) =>
        Ok(await _db.DocumentTypes.AsNoTracking()
            .OrderBy(t => t.DocumentTypeId)
            .Select(t => new DocTypeOpt(t.DocumentTypeId, t.NameAr, t.Code, t.HasExpiry))
            .ToListAsync(ct));

    // ═══════════════ UPLOAD ═══════════════

    [HttpPost("upload")]
    [Authorize(Policy = "PERM:DOCUMENT.UPLOAD")]
    [RequestSizeLimit(MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file,
        [FromForm] int documentTypeId, [FromForm] string entityType, [FromForm] long entityId,
        [FromForm] string? expiryDate, [FromForm] string? description, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "مافيش ملف اتبعت" });
        if (file.Length > MaxBytes)
            return BadRequest(new { message = "الملف أكبر من 10 ميجا" });

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !Allowed.Contains(ext))
            return BadRequest(new { message = "نوع الملف مش مسموح — المسموح: pdf، صور، word، excel، txt، zip" });

        if (!EntityTypes.Contains(entityType))
            return BadRequest(new { message = "نوع الجهة مش معروف" });

        var type = await _db.DocumentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.DocumentTypeId == documentTypeId, ct);
        if (type is null) return BadRequest(new { message = "نوع المستند مش موجود" });

        var exp = Dn(expiryDate);
        if (type.HasExpiry && exp is null)
            return BadRequest(new { message = $"«{type.NameAr}» لازم يكون لها تاريخ انتهاء" });

        if (entityId <= 0) return BadRequest(new { message = "اختار الجهة المرتبطة بالمستند" });
        var entityErr = await ValidateEntityAsync(entityType, entityId, ct);
        if (entityErr is not null) return BadRequest(new { message = entityErr });

        // ── الحفظ على الديس ──
        var safeName = Guid.NewGuid().ToString("N") + ext.ToLowerInvariant();
        var relDir   = Path.Combine("App_Data", "documents",
                                    DateTime.UtcNow.ToString("yyyy"), DateTime.UtcNow.ToString("MM"));
        var root     = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var absDir   = Path.Combine(root, relDir);
        Directory.CreateDirectory(absDir);
        var absPath  = Path.Combine(absDir, safeName);

        await using (var fs = System.IO.File.Create(absPath))
            await file.CopyToAsync(fs, ct);

        string hash;
        await using (var fs = System.IO.File.OpenRead(absPath))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));

        var d = new Document
        {
            BranchId         = int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null,
            DocumentTypeId   = documentTypeId,
            EntityType       = entityType,
            EntityId         = entityId,
            FileName         = safeName,
            OriginalFileName = Path.GetFileName(file.FileName),
            StorageProvider  = "Local",
            StoragePath      = Path.Combine(relDir, safeName).Replace('\\', '/'),
            ContentType      = file.ContentType,
            FileSizeBytes    = file.Length,
            FileHash         = hash,
            ExpiryDate       = exp,
            Description      = B(description),
            UploadedBy       = CurrentUserId()
        };
        _db.Documents.Add(d);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = d.DocumentId, message = $"✅ اتحفظ المستند ({Kb(file.Length)})" });
    }

    // ═══════════════ DOWNLOAD ═══════════════

    [HttpGet("{id:long}/download")]
    [Authorize(Policy = "PERM:DOCUMENT.DOWNLOAD")]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var d = await _db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.DocumentId == id && !x.IsDeleted, ct);
        if (d is null) return NotFound(new { message = "المستند مش موجود" });

        var root = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var abs  = Path.Combine(root, d.StoragePath.Replace('/', Path.DirectorySeparatorChar));

        // حماية من أي محاولة خروج عن مجلد المستندات
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(abs);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "مسار المستند مش سليم" });

        if (!System.IO.File.Exists(abs))
            return NotFound(new { message = "الملف مش موجود على السيرفر" });

        var bytes = await System.IO.File.ReadAllBytesAsync(abs, ct);
        var name  = d.OriginalFileName ?? d.FileName;
        return File(bytes, string.IsNullOrWhiteSpace(d.ContentType)
            ? "application/octet-stream" : d.ContentType, name);
    }

    // ═══════════════ DELETE ═══════════════

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "PERM:DOCUMENT.DELETE")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.DocumentId == id && !x.IsDeleted, ct);
        if (d is null) return NotFound(new { message = "المستند مش موجود" });

        d.IsDeleted = true;
        d.DeletedAt = DateTime.UtcNow;
        d.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "🗑️ اتحذف المستند" });
    }

    // ═══════════════ Helpers ═══════════════

    private static string Kb(long bytes) => bytes switch
    {
        < 1024          => $"{bytes} B",
        < 1024 * 1024   => $"{bytes / 1024.0:N0} KB",
        _               => $"{bytes / (1024.0 * 1024.0):N1} MB"
    };

    /// <summary>بيتأكد إن الجهة موجودة فعلًا — عشان مانشيلش مستندات لجهة وهمية.</summary>
    private async Task<string?> ValidateEntityAsync(string entityType, long entityId, CancellationToken ct)
    {
        var i = (int)entityId;
        var ok = entityType switch
        {
            "Customer" => await _db.Customers.AnyAsync(x => x.CustomerId == i && !x.IsDeleted, ct),
            "Supplier" => await _db.Suppliers.AnyAsync(x => x.SupplierId == i && !x.IsDeleted, ct),
            "Employee" => await _db.Employees.AnyAsync(x => x.EmployeeId == i && !x.IsDeleted, ct),
            "Driver"   => await _db.Drivers.AnyAsync(x => x.DriverId == i && !x.IsDeleted, ct),
            "Vehicle"  => await _db.Vehicles.AnyAsync(x => x.VehicleId == i && !x.IsDeleted, ct),
            "Trailer"  => await _db.Trailers.AnyAsync(x => x.TrailerId == i && !x.IsDeleted, ct),
            "Booking"  => await _db.Bookings.AnyAsync(x => x.BookingId == entityId && !x.IsDeleted, ct),
            "Operation" => await _db.Operations.AnyAsync(x => x.OperationId == entityId && !x.IsDeleted, ct),
            "Trip"     => await _db.Trips.AnyAsync(x => x.TripId == entityId && !x.IsDeleted, ct),
            "Container" => await _db.Containers.AnyAsync(x => x.ContainerId == i && !x.IsDeleted, ct),
            "Invoice"  => await _db.Invoices.AnyAsync(x => x.InvoiceId == entityId && !x.IsDeleted, ct),
            "SupplierInvoice" => await _db.SupplierInvoices.AnyAsync(x => x.SupplierInvoiceId == entityId && !x.IsDeleted, ct),
            "Expense"  => await _db.Expenses.AnyAsync(x => x.ExpenseId == entityId && !x.IsDeleted, ct),
            "Custody"  => await _db.DriverCustodies.AnyAsync(x => x.CustodyId == entityId && !x.IsDeleted, ct),
            "Payment"  => await _db.Payments.AnyAsync(x => x.PaymentId == entityId && !x.IsDeleted, ct),
            "Maintenance" => await _db.VehicleMaintenances.AnyAsync(x => x.MaintenanceId == entityId && !x.IsDeleted, ct),
            _          => true   // Other
        };
        return ok ? null : "الجهة دي مش موجودة";
    }
}
