using System.Data;
using System.Data.Common;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers;

/// <summary>
/// أول Controller — بيتأكد إن الـ API شغال وبيتصل بقاعدة البيانات.
/// افتح: https://localhost:7001/api/health
/// </summary>
/// <remarks>
/// 🔴 بيستخدم <b>ADO.NET مباشرة</b> (مش <c>Database.SqlQueryRaw&lt;T&gt;</c>).
/// السبب: <c>SqlQueryRaw&lt;T&gt;</c> مع كلاس مش Entity بيتطلب شروط صارمة
/// على الأسماء، ولو رمى استثناء الأرقام كلها بتطلع صفر من غير سبب واضح.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly ILogger<HealthController> _logger;

    public HealthController(FastComDbContext db, ILogger<HealthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var result = new Dictionary<string, object?>
        {
            ["status"]      = "ok",
            ["serverTime"]  = DateTime.UtcNow,
            ["cairoTime"]   = ToCairoTime(DateTime.UtcNow),
            ["environment"] = _db.Database.ProviderName
        };

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

            result["database"] = "connected";

            // ── العدّادات (كل واحدة لوحدها عشان خطأ واحد مايوقّفش الباقي) ──
            await SafeCountAsync(conn, result, "tablesInDatabase",
                "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0");

            await SafeCountAsync(conn, result, "views",
                "SELECT COUNT(*) FROM sys.views WHERE is_ms_shipped = 0");

            await SafeCountAsync(conn, result, "triggers",
                "SELECT COUNT(*) FROM sys.triggers WHERE is_ms_shipped = 0");

            await SafeCountAsync(conn, result, "procedures",
                "SELECT COUNT(*) FROM sys.procedures WHERE is_ms_shipped = 0");

            await SafeCountAsync(conn, result, "permissions",
                "SELECT COUNT(*) FROM AppPermissions");

            await SafeCountAsync(conn, result, "roles",
                "SELECT COUNT(*) FROM AspNetRoles");

            // ── بيانات الشركة (نفس الاستعلام اللي في BrandingController) ──
            await SafeScalarAsync(conn, result, "companyRows",
                "SELECT COUNT(*) FROM CompanyProfile");

            await SafeScalarAsync(conn, result, "companyName",
                "SELECT TOP 1 LegalNameAr FROM CompanyProfile ORDER BY CompanyProfileId");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل الاتصال بقاعدة البيانات");
            result["database"] = "ERROR";
            result["error"]    = ex.Message;

            // 🔍 تفاصيل أكتر تساعد في التشخيص
            result["errorType"] = ex.GetType().FullName;
            result["errorInner"] = ex.InnerException?.Message;
        }
        finally
        {
            if (weOpened && conn is not null)
            {
                try { await conn.CloseAsync(); } catch { /* تجاهل */ }
            }
        }

        return Ok(result);
    }

    /// <summary>بيعدّ — ولو فشل بيحط <c>-1</c> ورسالة الخطأ جنبه.</summary>
    private async Task SafeCountAsync(DbConnection conn,
                                      IDictionary<string, object?> result,
                                      string key, string sql)
    {
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 15;
            var v = await cmd.ExecuteScalarAsync();
            result[key] = v is null || v is DBNull ? 0 : Convert.ToInt32(v);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "فشل الاستعلام: {Sql}", sql);
            result[key]          = -1;
            result[key + "Error"] = ex.Message;
        }
    }

    /// <summary>بيجيب قيمة واحدة (نص أو رقم).</summary>
    private async Task SafeScalarAsync(DbConnection conn,
                                       IDictionary<string, object?> result,
                                       string key, string sql)
    {
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 15;
            var v = await cmd.ExecuteScalarAsync();
            result[key] = v is null || v is DBNull ? null : v.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "فشل الاستعلام: {Sql}", sql);
            result[key]          = null;
            result[key + "Error"] = ex.Message;
        }
    }

    /// <summary>تحويل UTC لوقت القاهرة — بيشتغل على Windows و Linux.</summary>
    private static DateTime ToCairoTime(DateTime utc)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");   // Windows
            return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");      // Linux
                return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            }
            catch
            {
                return utc.AddHours(2);                                            // Fallback
            }
        }
    }
}
