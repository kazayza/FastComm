using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// جدول الترقيم — سلاسل أرقام المستندات (حجز · عملية · فاتورة …).
/// </summary>
/// <remarks>
/// <para><b>🔴 الرقم بيتولّد بـ Stored Procedure `usp_GetNextNumber`</b> — مش بكود C#.
/// الصيغة: <c>Prefix + Year + "-" + Right("000000" + Num, NumberLength)</c>
/// مثال: <c>OP-2026-000042</c></para>
///
/// <para><b>⛔ `DocumentType` مقفول للتعديل</b> — الاسم متنادى بيه حرفيًا
/// في الكود (<c>NextAsync("OPERATION")</c>). تغييره = الـ SP مش هتلاقي السلسلة
/// وهترمي «سلسلة الترقيم غير معرّفة لهذا النوع».</para>
///
/// <para><b>⚠️ `CurrentNumber` التعديل عليه خطر</b> — لو نزّلته تحت رقم مستخدم
/// هتطلع أرقام مكررة. فالتعديل مسموح بس لفوق، أو تصفير بسلسلة جديدة.</para>
/// </remarks>
[ApiController]
[Route("api/master/numbering")]
[Authorize]
public class NumberingController : ControllerBase
{
    private readonly FastComDbContext _db;
    public NumberingController(FastComDbContext db) { _db = db; }

    // ═══════════════ DTOs ═══════════════

    public record Item(int NumberSequenceId, int? BranchId, string DocumentType,
        string TypeNameAr, string Prefix, short Year, long CurrentNumber,
        byte NumberLength, string ResetPeriod, string Sample, DateTime? UpdatedAt);

    /// <summary>مطابق لأعمدة `NumberSequences` القابلة للتعديل.</summary>
    public record Upsert(string Prefix, byte NumberLength, string ResetPeriod, long? CurrentNumber);

    // ═══════════════ أسماء الأنواع بالعربي ═══════════════

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BOOKING"]    = "حجز",
        ["OPERATION"]  = "عملية",
        ["TRIP"]       = "رحلة",
        ["CUSTODY"]    = "عهدة",
        ["CUSTOMER"]   = "عميل",
        ["SUPPLIER"]   = "مورد",
        ["DRIVER"]     = "سائق",
        ["EMPLOYEE"]   = "موظف",
        ["VEHICLE"]    = "عربة",
        ["TRAILER"]    = "مقطورة",
        ["EXPENSE"]    = "مصروف",
        ["INVOICE"]    = "فاتورة",
        ["CREDITNOTE"] = "إشعار دائن",
        ["PAYMENT"]    = "سند قبض",
        ["TREASURYTX"] = "حركة خزينة",
        ["TRANSFER"]   = "تحويل بين الخزائن",
        ["CONTAINER"]  = "حاوية",
        ["DOCUMENT"]   = "مستند",
        ["MAINTENANCE"] = "صيانة",
        ["SUPPLIER_INVOICE"] = "فاتورة مورد",
        ["SUPPLIER_PAYMENT"] = "سند صرف مورد",
        ["CASH_RECEIPT"]     = "سند قبض خزينة",
        ["CASH_PAYMENT"]     = "سند صرف خزينة",
    };

    private static string NameAr(string code) =>
        Names.TryGetValue(code, out var n) ? n : code;

    /// <summary>نفس صيغة الـ SP بالظبط — عشان المعاينة تكون حقيقية.</summary>
    private static string Sample(string prefix, short year, long num, byte len)
    {
        var l = len < 1 ? 1 : (len > 12 ? 12 : len);
        var s = num.ToString();
        if (s.Length < l) s = new string('0', l - s.Length) + s;
        return $"{prefix}{year}-{s}";
    }

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> List([FromQuery] short? year, CancellationToken ct)
    {
        var q = _db.NumberSequences.AsNoTracking().AsQueryable();
        q = year is null ? q.Where(n => n.Year == (short)DateTime.UtcNow.Year)
                         : q.Where(n => n.Year == year.Value);

        var rows = await q
            .OrderBy(n => n.DocumentType).ThenBy(n => n.BranchId)
            .ToListAsync(ct);

        return Ok(rows.Select(n => new Item(
            n.NumberSequenceId, n.BranchId, n.DocumentType, NameAr(n.DocumentType),
            n.Prefix, n.Year, n.CurrentNumber, n.NumberLength, n.ResetPeriod,
            Sample(n.Prefix, n.Year, n.CurrentNumber + 1, n.NumberLength),
            n.UpdatedAt)).ToList());
    }

    /// <summary>السنوات المتاحة في الجدول — للفلتر.</summary>
    [HttpGet("years")]
    [Authorize(Policy = "PERM:MASTERDATA.VIEW")]
    public async Task<IActionResult> Years(CancellationToken ct) =>
        Ok(await _db.NumberSequences.AsNoTracking()
            .Select(n => n.Year).Distinct().OrderByDescending(y => y).ToListAsync(ct));

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> Update(int id, [FromBody] Upsert req, CancellationToken ct)
    {
        var n = await _db.NumberSequences.FirstOrDefaultAsync(x => x.NumberSequenceId == id, ct);
        if (n is null) return NotFound(new { message = "سلسلة الترقيم غير موجودة" });

        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        n.Prefix       = req.Prefix.Trim().ToUpperInvariant();
        n.NumberLength = req.NumberLength;
        n.ResetPeriod  = req.ResetPeriod;

        /* 🔴 التعديل على العدّاد — مسموح بس لفوق، أو تصفير.
           النزول تحت الرقم الحالي = أرقام مكررة. */
        if (req.CurrentNumber is not null && req.CurrentNumber.Value != n.CurrentNumber)
        {
            if (req.CurrentNumber.Value < 0)
                return BadRequest(new { message = "العدّاد ماينفعش يبقى سالب" });
            if (req.CurrentNumber.Value < n.CurrentNumber && req.CurrentNumber.Value != 0)
                return BadRequest(new
                {
                    message = $"العدّاد حاليًا {n.CurrentNumber} — ماينفعش ينزل لـ {req.CurrentNumber.Value} " +
                              "عشان هتطلع أرقام مكررة. صفّره (0) لو عايز تبدأ من جديد."
                });
            n.CurrentNumber = req.CurrentNumber.Value;
        }

        n.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            message = $"✅ اتحفظ — الرقم الجاي هيكون {Sample(n.Prefix, n.Year, n.CurrentNumber + 1, n.NumberLength)}"
        });
    }

    // ═══════════════ إنشاء سلسلة لسنة جديدة ═══════════════

    /// <summary>
    /// الـ SP بتدوّر على <c>Year = السنة الحالية</c> — ولو مالقتش بترمي
    /// «سلسلة الترقيم غير معرّفة لهذا النوع». فالإندبوينت ده بيجهّز سلاسل السنة الجديدة.
    /// </summary>
    [HttpPost("prepare-year")]
    [Authorize(Policy = "PERM:MASTERDATA.MANAGE")]
    public async Task<IActionResult> PrepareYear([FromBody] YearIn req, CancellationToken ct)
    {
        var target = req.Year is > 0 ? req.Year.Value : (short)DateTime.UtcNow.Year;
        if (target < 2020 || target > 2100)
            return BadRequest(new { message = "سنة غير منطقية" });

        /* بننسخ سلاسل آخر سنة موجودة — عشان ناخد نفس الـ Prefix والـ NumberLength */
        var srcYear = await _db.NumberSequences.AsNoTracking()
            .Where(n => n.Year < target)
            .Select(n => n.Year).OrderByDescending(y => y).FirstOrDefaultAsync(ct);

        if (srcYear == 0)
            return BadRequest(new { message = "مافيش سلاسل موجودة أصلًا — راجع سكربت الـ seed" });

        var exists = await _db.NumberSequences.AnyAsync(n => n.Year == target, ct);
        if (exists)
            return BadRequest(new { message = $"سلاسل سنة {target} موجودة بالفعل" });

        var src = await _db.NumberSequences.AsNoTracking()
            .Where(n => n.Year == srcYear).ToListAsync(ct);

        foreach (var s in src)
        {
            _db.NumberSequences.Add(new NumberSequence
            {
                BranchId      = s.BranchId,
                DocumentType  = s.DocumentType,
                Prefix        = s.Prefix,
                Year          = target,
                CurrentNumber = 0,                 // 🔴 بتبدأ من الصفر
                NumberLength  = s.NumberLength,
                ResetPeriod   = s.ResetPeriod
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = $"✅ اتعملت {src.Count} سلسلة لسنة {target} — العدّاد من الصفر" });
    }

    public record YearIn(short? Year);

    // ═══════════════ validation ═══════════════

    private static readonly string[] Periods = { "Yearly", "Monthly", "Never" };

    private static string? Validate(Upsert? r)
    {
        if (r is null) return "البيانات غير كاملة";
        if (string.IsNullOrWhiteSpace(r.Prefix)) return "البادئة مطلوبة";
        if (r.Prefix.Trim().Length > 20) return "البادئة أطول من 20 حرف";
        if (r.NumberLength < 3 || r.NumberLength > 12) return "طول الرقم لازم يكون بين 3 و 12";
        if (!Periods.Contains(r.ResetPeriod)) return "دورية التصفير غير صالحة";
        return null;
    }
}
