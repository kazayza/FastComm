using System.Data;
using System.Data.Common;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers;

/// <summary>
/// 🔴 D11: بيانات الشركة من جدول <c>CompanyProfile</c> — مش من SystemSettings.
/// <para>
/// الـ endpoint ده <c>[AllowAnonymous]</c> لأن الـ Header والـ Footer محتاجينه
/// قبل ما المستخدم يسجّل دخول.
/// </para>
/// </summary>
/// <remarks>
/// 🔴 بيستخدم <b>ADO.NET مباشرة</b> — مش <c>Database.SqlQueryRaw&lt;CompanyDto&gt;</c>.
/// <para>
/// السبب: <c>SqlQueryRaw&lt;T&gt;</c> مع كلاس <b>nested</b> مش Entity بيرمي استثناء،
/// والـ <c>catch</c> كان بيبلعه ويرجّع البيانات الافتراضية — فمكانش فيه أي دليل
/// على إن فيه مشكلة أصلًا.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class BrandingController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly ILogger<BrandingController> _logger;

    public BrandingController(FastComDbContext db, ILogger<BrandingController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>بيجيب بيانات الشركة للـ Header والـ Footer.</summary>
    [HttpGet("company")]
    public async Task<IActionResult> GetCompany()
    {
        const string sql = @"
            SELECT TOP 1
                LegalNameAr,
                ISNULL(TradeNameAr, N'') AS TradeNameAr,
                ISNULL(TradeNameEn, N'') AS TradeNameEn,
                ISNULL(TaxNumber,   N'') AS TaxNumber,
                ISNULL(AddressAr,   N'') AS AddressAr,
                ISNULL(CityAr,      N'') AS CityAr,
                ISNULL(Phone,       N'') AS Phone,
                ISNULL(Mobile,      N'') AS Mobile,
                ISNULL(Email,       N'') AS Email,
                ISNULL(LogoPath,    N'') AS LogoPath
            FROM CompanyProfile
            ORDER BY CompanyProfileId";

        DbConnection? conn = null;
        bool weOpened = false;

        try
        {
            conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
                weOpened = true;
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText  = sql;
            cmd.CommandTimeout = 15;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
            {
                _logger.LogWarning("جدول CompanyProfile فاضي — راجع الـ Seed");
                return Ok(new CompanyDto
                {
                    LegalNameAr = "فاست كوم",
                    Error       = "جدول CompanyProfile مفيهوش ولا صف"
                });
            }

            var dto = new CompanyDto
            {
                LegalNameAr = rd.GetString(rd.GetOrdinal("LegalNameAr")),
                TradeNameAr = rd.GetString(rd.GetOrdinal("TradeNameAr")),
                TradeNameEn = rd.GetString(rd.GetOrdinal("TradeNameEn")),
                TaxNumber   = rd.GetString(rd.GetOrdinal("TaxNumber")),
                AddressAr   = rd.GetString(rd.GetOrdinal("AddressAr")),
                CityAr      = rd.GetString(rd.GetOrdinal("CityAr")),
                Phone       = rd.GetString(rd.GetOrdinal("Phone")),
                Mobile      = rd.GetString(rd.GetOrdinal("Mobile")),
                Email       = rd.GetString(rd.GetOrdinal("Email")),
                LogoPath    = rd.GetString(rd.GetOrdinal("LogoPath"))
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل جلب بيانات الشركة");

            // ⚠️ مانرجّعش 500 — الواجهة لازم تشتغل حتى لو البيانات ناقصة
            // 🔴 بس بنرجّع رسالة الخطأ الحقيقية عشان نشوفها في الـ Network tab
            return Ok(new CompanyDto
            {
                LegalNameAr = "فاست كوم",
                Error       = ex.Message,
                ErrorType   = ex.GetType().Name,
                ErrorInner  = ex.InnerException?.Message
            });
        }
        finally
        {
            if (weOpened && conn is not null)
            {
                try { await conn.CloseAsync(); } catch { /* تجاهل */ }
            }
        }
    }

    /// <summary>
    /// 🔴 مش nested — عشان لو احتجنا نرجع لـ <c>SqlQueryRaw&lt;T&gt;</c> يوم ما.
    /// </summary>
    public class CompanyDto
    {
        public string LegalNameAr { get; set; } = "فاست كوم";
        public string TradeNameAr { get; set; } = "";
        public string TradeNameEn { get; set; } = "";
        public string TaxNumber   { get; set; } = "";
        public string AddressAr   { get; set; } = "";
        public string CityAr      { get; set; } = "";
        public string Phone       { get; set; } = "";
        public string Mobile      { get; set; } = "";
        public string Email       { get; set; } = "";
        public string LogoPath    { get; set; } = "";

        // 🔍 للتشخيص — بتظهر في الـ JSON لو فيه مشكلة
        public string? Error      { get; set; }
        public string? ErrorType  { get; set; }
        public string? ErrorInner { get; set; }
    }
}
