using System.Data;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FastCom.Infrastructure.Identity;
using FastCom.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace FastCom.Server.Auth;

/// <summary>
/// 🔐 بيولّد الـ JWT وبيجيب صلاحيات المستخدم.
/// </summary>
/// <remarks>
/// 🔴 <b>القرارات المهمة:</b>
/// <list type="bullet">
/// <item>الـ <b>107 صلاحية مش بتتحط في الـ JWT</b> — ده كان هيخلي الـ Token
///       يوصل لـ 4–5 كيلوبايت، وده كبير على HTTP Header.</item>
/// <item>بدل كده: الأدوار بس في الـ Token، والصلاحيات بتتجلب من
///       <c>GET /api/auth/me</c> والـ Client بيخزّنها.</item>
/// <item>جلب الصلاحيات بـ <b>ADO.NET</b> — نفس منهج باقي الـ Controllers.</item>
/// </list>
/// </remarks>
public class TokenService
{
    private readonly IConfiguration _config;
    private readonly FastComDbContext _db;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IConfiguration config, FastComDbContext db, ILogger<TokenService> logger)
    {
        _config = config;
        _db = db;
        _logger = logger;
    }

    /// <summary>مدة الـ Token بالدقايق — من <c>Jwt:ExpirationMinutes</c>.</summary>
    public int ExpirationMinutes =>
        int.TryParse(_config["Jwt:ExpirationMinutes"], out var m) && m > 0 ? m : 480;

    /// <summary>
    /// بيولّد الـ JWT.
    /// </summary>
    /// <param name="user">المستخدم.</param>
    /// <param name="roleCodes">أكواد الأدوار (<c>ADMIN</c> · <c>OPSMGR</c> …).</param>
    /// <param name="rememberMe">بيزود المدة ×3.</param>
    public (string Token, DateTime ExpiresAt) CreateToken(
        ApplicationUser user,
        IEnumerable<string> roleCodes,
        bool rememberMe = false)
    {
        var key = _config["Jwt:Key"]
                  ?? throw new InvalidOperationException("Jwt:Key مش متعرّف في appsettings");

        if (key.Length < 64)
            throw new InvalidOperationException(
                $"Jwt:Key لازم يكون 64 حرف على الأقل — الحالي {key.Length} حرف");

        var minutes = rememberMe ? ExpirationMinutes * 3 : ExpirationMinutes;
        var expiresAt = DateTime.UtcNow.AddMinutes(minutes);

        var claims = new List<Claim>
        {
            // 🔴 A5: الـ key عدد صحيح (int) — مش GUID
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? ""),
            new("fullName", user.FullName ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));

        if (user.BranchId is not null)
            claims.Add(new Claim("branchId", user.BranchId.Value.ToString()));

        // الأدوار — بتتحط مرتين عشان [Authorize(Roles = "...")] يشتغل
        foreach (var role in roleCodes)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:   _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims:   claims,
            notBefore: DateTime.UtcNow,
            expires:  expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>
    /// 🔴 بيجيب صلاحيات المستخدم النهائية.
    /// <para>
    /// المنطق:
    /// <list type="number">
    /// <item>كل صلاحيات الأدوار اللي المستخدم فيها</item>
    /// <item><b>ناقص</b> الصلاحيات اللي <c>AppUserPermissions.IsGranted = 0</c> (منع صريح)</item>
    /// <item><b>زائد</b> الصلاحيات اللي <c>AppUserPermissions.IsGranted = 1</c> (استثناء)</item>
    /// </list>
    /// </para>
    /// </summary>
    public async Task<List<string>> GetPermissionsAsync(int userId, CancellationToken ct = default)
    {
        const string sql = @"
WITH FromRoles AS (
    SELECT DISTINCT arp.PermissionId
    FROM AspNetUserRoles ur
         INNER JOIN AppRolePermissions arp ON arp.RoleId = ur.RoleId
    WHERE ur.UserId = @uid
),
UserOverrides AS (
    SELECT PermissionId, IsGranted
    FROM AppUserPermissions
    WHERE UserId = @uid
)
SELECT p.PermissionCode
FROM AppPermissions p
WHERE (p.PermissionId IN (SELECT PermissionId FROM FromRoles)
       OR p.PermissionId IN (SELECT PermissionId FROM UserOverrides WHERE IsGranted = 1))
  AND p.PermissionId NOT IN (SELECT PermissionId FROM UserOverrides WHERE IsGranted = 0)
ORDER BY p.SortOrder, p.PermissionCode";

        DbConnection? conn = null;
        bool weOpened = false;

        try
        {
            conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(ct);
                weOpened = true;
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 15;
            cmd.Parameters.Add(new SqlParameter("@uid", SqlDbType.Int) { Value = userId });

            await using var rd = await cmd.ExecuteReaderAsync(ct);

            var list = new List<string>();
            while (await rd.ReadAsync(ct))
                list.Add(rd.GetString(0));

            return list;
        }
        catch (Exception ex)
        {
            // ⚠️ مانرميش — المستخدم يدخل بس من غير صلاحيات، وأحسن من إن النظام يقع
            _logger.LogError(ex, "فشل جلب صلاحيات المستخدم {UserId}", userId);
            return new List<string>();
        }
        finally
        {
            if (weOpened && conn is not null)
            {
                try { await conn.CloseAsync(); } catch { /* تجاهل */ }
            }
        }
    }

    /// <summary>بيجيب أكواد الأدوار النشطة للمستخدم.</summary>
    public async Task<List<string>> GetRoleCodesAsync(ApplicationUser user)
    {
        const string sql = @"
SELECT r.RoleCode
FROM AspNetUserRoles ur
     INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
WHERE ur.UserId = @uid AND r.IsActive = 1
ORDER BY r.Id";

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
            cmd.CommandText = sql;
            cmd.CommandTimeout = 15;
            cmd.Parameters.Add(new SqlParameter("@uid", SqlDbType.Int) { Value = user.Id });

            await using var rd = await cmd.ExecuteReaderAsync();

            var list = new List<string>();
            while (await rd.ReadAsync())
                list.Add(rd.GetString(0));

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل جلب أدوار المستخدم {UserId}", user.Id);
            // Fallback: من Identity نفسه
            return (await _db.UserRoles
                        .Where(ur => ur.UserId == user.Id)
                        .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name ?? "")
                        .ToListAsync())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
        }
        finally
        {
            if (weOpened && conn is not null)
            {
                try { await conn.CloseAsync(); } catch { /* تجاهل */ }
            }
        }
    }
}
