using System.Data;
using System.Data.Common;
using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// ⭐ Step 5 — العملاء: CRUD كامل بترقيم CST- من usp_GetNextNumber + حذف ناعم.
/// </summary>
[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(FastComDbContext db, ILogger<CustomersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    public record CustomerListItemDto(
        int CustomerId, string CustomerCode, string CustomerType, string NameAr,
        string? Phone, string? TaxNumber, string? CityAr, bool IsActive, decimal CreditLimit,
        bool PortalEnabled);

    public record CustomerDetailDto(
        int CustomerId, string CustomerCode, string CustomerType, string NameAr, string? NameEn,
        string? TaxNumber, string? NationalId, string? CommercialRegister, string? Phone, string? Email,
        string? AddressAr, string? CityAr, decimal CreditLimit, bool PortalEnabled, bool IsActive,
        DateTime CreatedAt);

    public record CustomerUpsertRequest(
        string CustomerType, string NameAr, string? NameEn, string? TaxNumber, string? NationalId,
        string? CommercialRegister, string? Phone, string? Email, string? AddressAr, string? CityAr,
        decimal CreditLimit, bool PortalEnabled);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:CUSTOMER.VIEW")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _db.Customers.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.CustomerId)
            .Select(c => new CustomerListItemDto(
                c.CustomerId, c.CustomerCode, c.CustomerType, c.NameAr,
                c.Phone, c.TaxNumber, c.CityAr, c.IsActive, c.CreditLimit, c.PortalEnabled))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ONE ═══════════════

    [HttpGet("{id:int}")]
    [Authorize(Policy = "PERM:CUSTOMER.VIEW")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var c = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.CustomerId == id && !x.IsDeleted, ct);
        if (c is null) return NotFound(new { message = "العميل مش موجود" });

        return Ok(new CustomerDetailDto(
            c.CustomerId, c.CustomerCode, c.CustomerType, c.NameAr, c.NameEn,
            c.TaxNumber, c.NationalId, c.CommercialRegister, c.Phone, c.Email,
            c.AddressAr, c.CityAr, c.CreditLimit, c.PortalEnabled, c.IsActive, c.CreatedAt));
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:CUSTOMER.CREATE")]
    public async Task<IActionResult> Create([FromBody] CustomerUpsertRequest req, CancellationToken ct)
    {
        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        // ── الترقيم CST- من الـ proc ──
        string code;
        try
        {
            code = await GetNextCodeAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل ترقيم عميل جديد");
            return StatusCode(500, new { message = "تعذّر توليد كود العميل — حاول تاني" });
        }

        var c = new Customer
        {
            CustomerCode   = code,
            CustomerType   = req.CustomerType,
            NameAr         = req.NameAr.Trim(),
            NameEn         = Blank(req.NameEn),
            TaxNumber      = Blank(req.TaxNumber),
            NationalId     = Blank(req.NationalId),
            CommercialRegister = Blank(req.CommercialRegister),
            Phone          = Blank(req.Phone),
            Email          = Blank(req.Email),
            AddressAr      = Blank(req.AddressAr),
            CityAr         = Blank(req.CityAr),
            CreditLimit    = req.CreditLimit,
            PortalEnabled  = req.PortalEnabled,
            IsActive       = true,
            CreatedBy      = CurrentUserId()
        };

        _db.Customers.Add(c);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("عميل جديد {Code} {Name}", code, c.NameAr);
        return Ok(new { id = c.CustomerId, code, message = $"✅ اتعمل العميل بكود {code}" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:CUSTOMER.EDIT")]
    public async Task<IActionResult> Update(int id, [FromBody] CustomerUpsertRequest req, CancellationToken ct)
    {
        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        var c = await _db.Customers.FirstOrDefaultAsync(x => x.CustomerId == id, ct);
        if (c is null) return NotFound(new { message = "العميل مش موجود" });

        c.CustomerType   = req.CustomerType;
        c.NameAr         = req.NameAr.Trim();
        c.NameEn         = Blank(req.NameEn);
        c.TaxNumber      = Blank(req.TaxNumber);
        c.NationalId     = Blank(req.NationalId);
        c.CommercialRegister = Blank(req.CommercialRegister);
        c.Phone          = Blank(req.Phone);
        c.Email          = Blank(req.Email);
        c.AddressAr      = Blank(req.AddressAr);
        c.CityAr         = Blank(req.CityAr);
        c.CreditLimit    = req.CreditLimit;
        c.PortalEnabled  = req.PortalEnabled;
        c.UpdatedAt      = DateTime.UtcNow;
        c.UpdatedBy      = CurrentUserId();

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    // ═══════════════ TOGGLE ACTIVE ═══════════════

    [HttpPost("{id:int}/toggle-active")]
    [Authorize(Policy = "PERM:CUSTOMER.EDIT")]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.CustomerId == id, ct);
        if (c is null) return NotFound(new { message = "العميل مش موجود" });

        c.IsActive  = !c.IsActive;
        c.UpdatedAt = DateTime.UtcNow;
        c.UpdatedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = c.IsActive ? "✅ اتفعّل العميل" : "✅ اتوقف العميل" });
    }

    // ═══════════════ SOFT DELETE ═══════════════

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:CUSTOMER.DELETE")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.CustomerId == id, ct);
        if (c is null) return NotFound(new { message = "العميل مش موجود" });

        c.IsDeleted = true;
        c.IsActive  = false;
        c.DeletedAt = DateTime.UtcNow;
        c.DeletedBy = CurrentUserId();
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("حذف ناعم للعميل {Id}", id);
        return Ok(new { message = "✅ اتحذف العميل (حذف ناعم — البيانات محفوظة للأرشيف)" });
    }

    // ═══════════════ helpers ═══════════════

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Validate(CustomerUpsertRequest? req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.NameAr))
            return "الاسم بالعربي مطلوب";

        if (req.CustomerType is not ("Company" or "Individual"))
            return "نوع العميل لازم شركة أو فرد";

        if (req.CustomerType == "Company" && string.IsNullOrWhiteSpace(req.TaxNumber))
            return "الشركة لازم يكون لها رقم ضريبي (متطلب ضريبي)";

        if (req.CustomerType == "Individual" && string.IsNullOrWhiteSpace(req.NationalId))
            return "الفرد لازم يكون له رقم قومي (متطلب ضريبي)";

        if (req.CreditLimit < 0)
            return "حد الائتمان مينفعش يكون بالسالب";

        return null;
    }

    /// <summary>نادي usp_GetNextNumber بـ CUSTOMER ورجّع الكود.</summary>
    private async Task<string> GetNextCodeAsync(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandType   = CommandType.StoredProcedure;
        cmd.CommandText   = "usp_GetNextNumber";
        cmd.CommandTimeout = 30;

        var pType = cmd.CreateParameter();
        pType.ParameterName = "@DocumentType"; pType.Value = "CUSTOMER";
        cmd.Parameters.Add(pType);

        var pBranch = cmd.CreateParameter();
        pBranch.ParameterName = "@BranchId"; pBranch.Value = DBNull.Value;
        cmd.Parameters.Add(pBranch);

        var pOut = cmd.CreateParameter();
        pOut.ParameterName = "@NextNumber";
        pOut.Direction     = ParameterDirection.Output;
        pOut.Size          = 40;
        cmd.Parameters.Add(pOut);

        await cmd.ExecuteNonQueryAsync(ct);
        return (string)pOut.Value!;
    }
}
