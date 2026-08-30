using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace FastCom.Server.Controllers;

/// <summary>
/// 🔧 تشخيص الاتصال بقاعدة البيانات.
/// <para>
/// بيقولك الـ app بيقرأ إيه <b>بالظبط</b> من <c>appsettings.json</c> —
/// من غير ما يكشف كلمة المرور.
/// </para>
/// </summary>
/// <remarks>
/// 🔴 <b>Development بس.</b> في الإنتاج بيرجع 404.
/// </remarks>
[ApiController]
[Route("api/diag")]
public class DiagController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DiagController> _logger;

    public DiagController(IConfiguration config, IWebHostEnvironment env, ILogger<DiagController> logger)
    {
        _config = config;
        _env    = env;
        _logger = logger;
    }

    /// <summary><c>GET /api/diag/connection</c></summary>
    [HttpGet("connection")]
    [AllowAnonymous]
    public async Task<IActionResult> Connection()
    {
        if (!_env.IsDevelopment()) return NotFound();

        var cs = _config.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
        {
            return Ok(new
            {
                result = "❌ مافيش ConnectionString",
                hint   = "راجع ConnectionStrings:DefaultConnection في appsettings.json"
            });
        }

        // ── نفكك الـ Connection String ─────────────────────────────────────
        string server = "", db = "", uid = "", pwd = "", extra = "";
        bool parsed = false;
        string? parseError = null;

        try
        {
            var b = new SqlConnectionStringBuilder(cs);
            server  = b.DataSource;
            db      = b.InitialCatalog;
            uid     = b.UserID;
            pwd     = b.Password;
            extra   = $"Encrypt={b.Encrypt}; TrustServerCertificate={b.TrustServerCertificate}; MARS={b.MultipleActiveResultSets}";
            parsed  = true;
        }
        catch (Exception ex)
        {
            parseError = ex.Message;
        }

        var outp = new Dictionary<string, object?>
        {
            ["environment"]  = _env.EnvironmentName,
            ["parsedOk"]     = parsed,
            ["parseError"]   = parseError,
            ["server"]       = server,
            ["database"]     = db,
            ["userId"]       = uid,
            ["options"]      = extra,

            // 🔴 كلمة المرور — بنتحقق منها من غير ما نكشفها
            ["pwd.length"]       = pwd.Length,
            ["pwd.isEmpty"]      = pwd.Length == 0,
            ["pwd.firstChar"]    = pwd.Length > 0 ? pwd[0].ToString() : "(فاضي)",
            ["pwd.lastChar"]     = pwd.Length > 0 ? pwd[^1].ToString() : "(فاضي)",
            ["pwd.hasSemicolon"] = pwd.Contains(';'),
            ["pwd.hasQuote"]     = pwd.Contains('"') || pwd.Contains('\''),
            ["pwd.hasSpace"]     = pwd.Contains(' '),
            ["pwd.isPlaceholder"] = pwd.Contains("PUT_YOUR_PASSWORD") || pwd.Contains("CHANGE_ME"),
            ["pwd.hash"]         = pwd.Length > 0
                                      ? Convert.ToHexString(
                                            System.Security.Cryptography.SHA256.HashData(
                                                System.Text.Encoding.UTF8.GetBytes(pwd)))[..12]
                                      : "",

            // 🔍 طول الـ Connection String نفسه — يكشف لو فيه حاجة بايظة
            ["cs.length"] = cs.Length
        };

        // ── نجرب نتصل فعلًا ────────────────────────────────────────────────
        try
        {
            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync();
            outp["connection"] = "✅ connected";
            outp["serverVersion"] = conn.ServerVersion;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DB_NAME()";
            outp["actualDatabase"] = (await cmd.ExecuteScalarAsync())?.ToString();
        }
        catch (SqlException ex)
        {
            outp["connection"] = "❌ FAILED";
            outp["sqlError.number"] = ex.Number;
            outp["sqlError.state"]  = ex.State;
            outp["sqlError.class"]  = ex.Class;
            outp["sqlError.message"] = ex.Message;
            outp["hint"] = Hint(ex.Number, ex.State);
        }
        catch (Exception ex)
        {
            outp["connection"] = "❌ FAILED";
            outp["error"] = ex.Message;
            outp["errorType"] = ex.GetType().Name;
        }

        return Ok(outp);
    }

    /// <summary>ترجمة رقم خطأ SQL Server لسبب محتمل.</summary>
    private static string Hint(int number, int state) => number switch
    {
        18456 => state switch
        {
            2 or 5  => "اسم المستخدم مش موجود على السيرفر",
            6       => "محاولة Windows Auth على SQL login",
            7       => "كلمة المرور غلط + الحساب مقفول",
            8       => "كلمة المرور غلط",
            11 or 12 => "الدخول صالح بس مافيش إذن على السيرفر",
            18      => "كلمة المرور لازم تتغير",
            58      => "السيرفر متظبط على Windows Auth بس",
            _       => $"فشل تسجيل دخول (State {state}) — راجع كلمة المرور في appsettings.json"
        },
        -2      => "Timeout — السيرفر مش reachable أو الـ Firewall",
        53 or 26 => "اسم السيرفر غلط أو مش reachable",
        4060    => "الدخول نجح بس اسم قاعدة البيانات غلط",
        _       => "راجع الرسالة"
    };
}
