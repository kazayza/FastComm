using ClosedXML.Excel;
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

    // ══════════════════════════════════════════════════════════════════
    //  📥 استيراد من Excel (🆕 2026-09-14)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>صف في ملف الاستيراد.</summary>
    public record ImportRow(string? CustomerCode, string? CustomerType, string? NameAr,
        string? NameEn, string? TaxNumber, string? NationalId, string? CommercialRegister,
        string? Phone, string? Email, string? AddressAr, string? CityAr,
        decimal CreditLimit, bool PortalEnabled);

    /// <summary>نتيجة الاستيراد.</summary>
    public record ImportResult(int Success, int Failed, List<string> Errors);

    /// <summary>
    /// 📥 <c>GET api/customers/import-template</c> — تحميل ملف Excel مرجعي.
    /// </summary>
    [HttpGet("import-template")]
    [Authorize(Policy = "PERM:CUSTOMER.VIEW")]
    public IActionResult ImportTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("العملاء");
        ws.RightToLeft = true;

        /* العناوين */
        var cols = new[]
        {
            "الكود (اختياري)", "النوع (Company/Individual)", "الاسم (عربي) *",
            "الاسم (إنجليزي)", "الرقم الضريبي", "الرقم القومي", "السجل التجاري",
            "التليفون", "البريد الإلكتروني", "العنوان", "المدينة",
            "حد الائتمان", "البوابة مفعّلة (TRUE/FALSE)"
        };

        for (var c = 0; c < cols.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = cols[c];
            cell.Style.Font.SetBold();
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#16233A");
            cell.Style.Font.FontColor = XLColor.FromHtml("#FFFFFF");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        /* صف مثال */
        ws.Cell(2, 1).Value = "CUST-001";
        ws.Cell(2, 2).Value = "Company";
        ws.Cell(2, 3).Value = "شركة النور";
        ws.Cell(2, 4).Value = "Al-Nour Company";
        ws.Cell(2, 5).Value = "123456789012345";
        ws.Cell(2, 6).Value = "";
        ws.Cell(2, 7).Value = "";
        ws.Cell(2, 8).Value = "02-12345678";
        ws.Cell(2, 9).Value = "info@alnour.com";
        ws.Cell(2, 10).Value = "القاهرة";
        ws.Cell(2, 11).Value = "القاهرة";
        ws.Cell(2, 12).Value = 50000;
        ws.Cell(2, 13).Value = "FALSE";

        /* تنسيق */
        for (var c = 1; c <= cols.Length; c++)
            ws.Column(c).Width = Math.Max(15, cols[c - 1].Length + 5);

        ws.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var bytes = ms.ToArray();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "customers-template.xlsx");
    }

    /// <summary>
    /// 📤 <c>POST api/customers/import</c> — استيراد عملاء من ملف Excel.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Policy = "PERM:CUSTOMER.CREATE")]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "مافيش ملف" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls"))
            return BadRequest(new { message = "الملف لازم يكون Excel (.xlsx)" });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "الملف أكبر من 10 ميجا" });

        var rows = new List<ImportRow>();
        var errors = new List<string>();
        var success = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);

            /* نبدأ من الصف التاني (الأول عناوين) */
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (var r = 2; r <= lastRow; r++)
            {
                var code = ws.Cell(r, 1).GetString().Trim();
                var type = ws.Cell(r, 2).GetString().Trim();
                var nameAr = ws.Cell(r, 3).GetString().Trim();
                var nameEn = ws.Cell(r, 4).GetString().Trim();
                var taxNumber = ws.Cell(r, 5).GetString().Trim();
                var nationalId = ws.Cell(r, 6).GetString().Trim();
                var commercialRegister = ws.Cell(r, 7).GetString().Trim();
                var phone = ws.Cell(r, 8).GetString().Trim();
                var email = ws.Cell(r, 9).GetString().Trim();
                var addressAr = ws.Cell(r, 10).GetString().Trim();
                var cityAr = ws.Cell(r, 11).GetString().Trim();
                var creditLimit = ws.Cell(r, 12).GetDouble();
                var portalEnabled = ws.Cell(r, 13).GetString().Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase);

                /* تخطي الصفوف الفاضية */
                if (string.IsNullOrWhiteSpace(nameAr)) continue;

                var row = new ImportRow(
                    string.IsNullOrWhiteSpace(code) ? null : code,
                    string.IsNullOrWhiteSpace(type) ? null : type,
                    nameAr, nameEn, taxNumber, nationalId, commercialRegister,
                    phone, email, addressAr, cityAr,
                    (decimal)creditLimit, portalEnabled);

                rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل قراءة ملف الاستيراد");
            return BadRequest(new { message = "فشل قراءة الملف: " + ex.Message });
        }

        if (rows.Count == 0)
            return BadRequest(new { message = "مافيش بيانات في الملف" });

        /* معالجة كل صف */
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNum = i + 2; /* +2 لأن الصف الأول عناوين */

            try
            {
                /* التحقق */
                var err = ValidateImport(row);
                if (err is not null)
                {
                    errors.Add($"الصف {rowNum}: {err}");
                    continue;
                }

                /* الكود — لو مش موجود، نولده */
                var code = row.CustomerCode;
                if (string.IsNullOrWhiteSpace(code))
                {
                    code = await GetNextCodeAsync(ct);
                }
                else
                {
                    /* التأكد إن الكود مش موجود */
                    var exists = await _db.Customers.AnyAsync(c => c.CustomerCode == code && !c.IsDeleted, ct);
                    if (exists)
                    {
                        errors.Add($"الصف {rowNum}: الكود {code} موجود بالفعل");
                        continue;
                    }
                }

                /* إنشاء العميل */
                var c = new Customer
                {
                    CustomerCode = code,
                    CustomerType = row.CustomerType!,
                    NameAr = row.NameAr!.Trim(),
                    NameEn = Blank(row.NameEn),
                    TaxNumber = Blank(row.TaxNumber),
                    NationalId = Blank(row.NationalId),
                    CommercialRegister = Blank(row.CommercialRegister),
                    Phone = Blank(row.Phone),
                    Email = Blank(row.Email),
                    AddressAr = Blank(row.AddressAr),
                    CityAr = Blank(row.CityAr),
                    CreditLimit = row.CreditLimit,
                    PortalEnabled = row.PortalEnabled,
                    IsActive = true,
                    CreatedBy = CurrentUserId()
                };

                _db.Customers.Add(c);
                await _db.SaveChangesAsync(ct);
                success++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "فشل استيراد الصف {Row}", rowNum);
                errors.Add($"الصف {rowNum}: {ex.Message}");
            }
        }

        var result = new ImportResult(success, errors.Count, errors);
        var msg = $"✅ تم استيراد {success} عملاء بنجاح";
        if (errors.Count > 0)
            msg += $" · ❌ فشل {errors.Count} صفوف";

        return Ok(new { message = msg, result });
    }

    private static string? ValidateImport(ImportRow row)
    {
        if (string.IsNullOrWhiteSpace(row.NameAr))
            return "الاسم بالعربي مطلوب";

        if (row.CustomerType is not ("Company" or "Individual"))
            return "نوع العميل لازم Company أو Individual";

        if (row.CustomerType == "Company" && string.IsNullOrWhiteSpace(row.TaxNumber))
            return "الشركة لازم يكون لها رقم ضريبي";

        if (row.CustomerType == "Individual" && string.IsNullOrWhiteSpace(row.NationalId))
            return "الفرد لازم يكون له رقم قومي";

        if (row.CreditLimit < 0)
            return "حد الائتمان مينفعش يكون بالسالب";

        return null;
    }
}
