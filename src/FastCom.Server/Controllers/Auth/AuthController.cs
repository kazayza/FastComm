using System.Security.Claims;
using FastCom.Infrastructure.Identity;
using FastCom.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers;

/// <summary>
/// 🔐 تسجيل الدخول + بيانات المستخدم الحالي.
/// </summary>
/// <remarks>
/// 🔒 <b>الأمان:</b>
/// <list type="bullet">
/// <item>رسالة الخطأ <b>واحدة</b> لكل حالات الفشل — مانكشفش إن اسم المستخدم موجود</item>
/// <item>الـ <b>Lockout</b> شغال (5 محاولات → قفل 15 دقيقة) من إعدادات Identity</item>
/// <item><c>seed-admin</c> مقفول على <b>Development</b> بس</item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly TokenService _tokens;
    private readonly ILogger<AuthController> _logger;
    private readonly IWebHostEnvironment _env;

    public AuthController(
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        TokenService tokens,
        ILogger<AuthController> logger,
        IWebHostEnvironment env)
    {
        _users  = users;
        _roles  = roles;
        _tokens = tokens;
        _logger = logger;
        _env    = env;
    }

    /// <summary>رسالة الفشل الموحّدة — مانكشفش سبب الفشل بالظبط.</summary>
    private const string GenericFailure = "اسم المستخدم أو كلمة المرور غير صحيحة";

    // ═══════════════════════════════════════════════════════════════════
    //  POST /api/auth/login
    // ═══════════════════════════════════════════════════════════════════

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse
            {
                Message = string.Join(" · ",
                    ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage))
            });

        try
        {
            // ── 1) نجيب المستخدم بالاسم أو الإيميل ────────────────────────
            var userName = req.UserName.Trim();

            var user = await _users.Users.FirstOrDefaultAsync(u =>
                u.UserName == userName || u.Email == userName, ct);

            if (user is null)
            {
                _logger.LogWarning("محاولة دخول باسم مستخدم غير موجود: {UserName}", userName);
                return Unauthorized(new AuthErrorResponse { Message = GenericFailure });
            }

            // ── 2) الحساب مقفول ولا لأ؟ ────────────────────────────────────
            if (!user.IsActive || user.UserStatus == "Locked")
            {
                _logger.LogWarning("محاولة دخول على حساب مقفول: {UserName}", userName);
                return Unauthorized(new AuthErrorResponse
                {
                    Message = "الحساب ده موقوف. برجاء التواصل مع مدير النظام."
                });
            }

            if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
            {
                var mins = (int)Math.Ceiling((user.LockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes);
                return Unauthorized(new AuthErrorResponse
                {
                    Message = $"الحساب مقفول مؤقتًا بسبب كثرة المحاولات. جرّب بعد {mins} دقيقة."
                });
            }

            // ── 3) نتحقق من كلمة المرور ────────────────────────────────────
            var passwordOk = await _users.CheckPasswordAsync(user, req.Password);

            if (!passwordOk)
            {
                // 🔴 Identity بيعدّ المحاولات الفاشلة ويقفل الحساب تلقائيًا
                await _users.AccessFailedAsync(user);

                var remaining = Math.Max(0, 5 - user.AccessFailedCount);
                _logger.LogWarning("كلمة مرور غلط للمستخدم {UserName} — باقي {Remaining}",
                    userName, remaining);

                return Unauthorized(new AuthErrorResponse
                {
                    Message = remaining > 0
                        ? $"{GenericFailure} — باقي {remaining} محاولة قبل قفل الحساب"
                        : "تم قفل الحساب لمدة 15 دقيقة بسبب كثرة المحاولات الخاطئة"
                });
            }

            // ── 4) نجحت — نصفر عدّاد الفشل ─────────────────────────────────
            await _users.ResetAccessFailedCountAsync(user);

            // ── 5) الأدوار + الصلاحيات ─────────────────────────────────────
            var roles       = await _tokens.GetRoleCodesAsync(user);
            var permissions = await _tokens.GetPermissionsAsync(user.Id, ct);

            // ── 6) الـ Token ───────────────────────────────────────────────
            var (token, expiresAt) = _tokens.CreateToken(user, roles, req.RememberMe);

            // ── 7) نسجّل الدخول ────────────────────────────────────────────
            user.LastLoginAt = DateTime.UtcNow;
            user.LastLoginIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _users.UpdateAsync(user);

            _logger.LogInformation("دخول ناجح: {UserName} ({UserId}) — {Roles}",
                user.UserName, user.Id, string.Join(",", roles));

            return Ok(new LoginResponse
            {
                Token       = token,
                ExpiresAt   = expiresAt,
                UserName    = user.UserName ?? "",
                FullName    = user.FullName ?? "",
                Email       = user.Email,
                BranchId    = user.BranchId,
                Roles       = roles,
                Permissions = permissions
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ أثناء تسجيل الدخول");

            // 🔴 قاعدة البيانات مش متاحة — رسالة واضحة للمستخدم بدل الخطأ العام
            var root = ex.GetBaseException();
            if (root is Microsoft.Data.SqlClient.SqlException sql)
            {
                // 18456 = فشل دخول SQL نفسه (يوزر/باسورد) — غير كده = شبكة/سيرفر
                _logger.LogWarning("DB unavailable during login (SqlError {Number})", sql.Number);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new AuthErrorResponse
                {
                    Message = sql.Number == 18456
                        ? "بيانات الاتصال بقاعدة البيانات غير صحيحة — راجع إعدادات السيرفر."
                        : "النظام مش قادر يوصل لقاعدة البيانات حاليًا. جرّب تاني بعد شوية."
                });
            }
            if (root is TimeoutException || root is System.ComponentModel.Win32Exception)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new AuthErrorResponse
                {
                    Message = "الاتصال بقاعدة البيانات اتأخر أو انقطع. جرّب تاني بعد شوية."
                });
            }

            return StatusCode(500, new AuthErrorResponse
            {
                Message   = "حصل خطأ في النظام. برجاء المحاولة مرة أخرى.",
                ErrorType = _env.IsDevelopment() ? ex.GetType().Name : null
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  GET /api/auth/me
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// بيانات المستخدم الحالي + الصلاحيات.
    /// <para>الـ Client بيناديه بعد ما يحمّل الـ Token من الـ Storage.</para>
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var idText = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub");

        if (!int.TryParse(idText, out var userId))
            return Unauthorized(new AuthErrorResponse { Message = "الـ Token غير صالح" });

        var user = await _users.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || !user.IsActive)
            return Unauthorized(new AuthErrorResponse { Message = "المستخدم غير موجود أو موقوف" });

        return Ok(new MeResponse
        {
            UserId      = user.Id,
            UserName    = user.UserName ?? "",
            FullName    = user.FullName ?? "",
            Email       = user.Email,
            BranchId    = user.BranchId,
            Roles       = await _tokens.GetRoleCodesAsync(user),
            Permissions = await _tokens.GetPermissionsAsync(userId, ct)
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  POST /api/auth/logout
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// الـ JWT Stateless — مافيش جلسة على السيرفر.
    /// الـ Endpoint ده للتسجيل في الـ Audit بس، والـ Client هو اللي بيمسح الـ Token.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        _logger.LogInformation("خروج: {UserName}", User.Identity?.Name);
        return Ok(new { success = true, message = "تم تسجيل الخروج" });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  POST /api/auth/seed-admin   (Development بس)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔧 بيعمل أول مستخدم مدير ويربطه بدور <c>ADMIN</c>.
    /// <para>
    /// 🔴 <b>مقفول على Development.</b> في الإنتاج استخدم SQL مباشر
    /// أو شاشة إدارة المستخدمين (Step 4).
    /// </para>
    /// </summary>
    [HttpPost("seed-admin")]
    [AllowAnonymous]
    public async Task<IActionResult> SeedAdmin([FromBody] SeedAdminRequest req, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();   // 🔴 في الإنتاج: كأن الـ endpoint مش موجود

        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
            return BadRequest(new AuthErrorResponse
            {
                Message = "كلمة المرور لازم تكون 8 حروف على الأقل"
            });

        try
        {
            var userName = req.UserName.Trim();

            // ── موجود بالفعل؟ ──────────────────────────────────────────────
            var existing = await _users.Users.FirstOrDefaultAsync(u => u.UserName == userName, ct);
            if (existing is not null)
            {
                return BadRequest(new AuthErrorResponse
                {
                    Message = $"المستخدم '{userName}' موجود بالفعل (Id = {existing.Id})"
                });
            }

            // ── الدور ADMIN موجود؟ ─────────────────────────────────────────
            var adminRole = await FindAdminRoleAsync(ct);
            if (adminRole is null)
            {
                return BadRequest(new AuthErrorResponse
                {
                    Message = "دور ADMIN مش موجود في قاعدة البيانات — شغّل سكربت الـ Seed الأول"
                });
            }

            // ── نعمل المستخدم ──────────────────────────────────────────────
            var user = new ApplicationUser
            {
                UserName   = userName,
                Email      = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
                FullName   = req.FullName.Trim(),
                UserStatus = "Active",
                IsActive   = true,
                CreatedAt  = DateTime.UtcNow
            };

            var created = await _users.CreateAsync(user, req.Password);

            if (!created.Succeeded)
            {
                return BadRequest(new AuthErrorResponse
                {
                    Message = string.Join(" · ", created.Errors.Select(e => e.Description))
                });
            }

            // ── نربطه بالدور ────────────────────────────────────────────────
            var addRole = await _users.AddToRoleAsync(user, adminRole.Name ?? "SystemAdministrator");

            if (!addRole.Succeeded)
            {
                return BadRequest(new AuthErrorResponse
                {
                    Message = "المستخدم اتعمل بس فشل ربطه بالدور: " +
                              string.Join(" · ", addRole.Errors.Select(e => e.Description))
                });
            }

            var perms = await _tokens.GetPermissionsAsync(user.Id, ct);

            _logger.LogInformation("✅ اتعمل مستخدم مدير: {UserName} (Id = {Id})", userName, user.Id);

            return Ok(new
            {
                success     = true,
                message     = $"✅ اتعمل المستخدم '{userName}' وربط بدور مدير النظام",
                userId      = user.Id,
                role        = adminRole.NameAr,
                permissions = perms.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل إنشاء مستخدم مدير");
            return StatusCode(500, new AuthErrorResponse
            {
                Message    = "فشل إنشاء المستخدم",
                ErrorType  = ex.GetType().Name
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  POST /api/auth/reset-password   🔧 Development فقط
    //  أداة مطورين: لو المدير نسى الباسورد — نعيّن واحد جديد من غير
    //  محاولات تخمين (الحساب بيتقفل بعد 5 محاولات غلط).
    //  ⚠️ في الـ Production الـ endpoint ده بيرجّع 404.
    // ═══════════════════════════════════════════════════════════════════

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = "بيانات ناقصة" });

        if (req.NewPassword.Length < 8)
            return BadRequest(new AuthErrorResponse
            {
                Message = "كلمة المرور لازم تكون 8 حروف على الأقل"
            });

        try
        {
            var userName = req.UserName.Trim();

            var user = await _users.Users.FirstOrDefaultAsync(
                u => u.UserName == userName || u.Email == userName, ct);

            if (user is null)
                return NotFound(new AuthErrorResponse
                {
                    Message = $"مفيش مستخدم باسم '{userName}'"
                });

            // ── نشيل الباسورد القديم ونحط الجديد (Identity بيتحقق من الـ Policy) ──
            var remove = await _users.RemovePasswordAsync(user);
            if (!remove.Succeeded)
                return BadRequest(new AuthErrorResponse
                {
                    Message = "فشل إزالة كلمة المرور القديمة: " +
                              string.Join(" · ", remove.Errors.Select(e => e.Description))
                });

            var add = await _users.AddPasswordAsync(user, req.NewPassword);
            if (!add.Succeeded)
                return BadRequest(new AuthErrorResponse
                {
                    Message = "كلمة المرور مرفوضة: " +
                              string.Join(" · ", add.Errors.Select(e => e.Description))
                });

            // ── نفتح الحساب لو اتقفل من المحاولات الغلط ونصفر العدّاد ──
            user.LockoutEnd = null;
            await _users.UpdateAsync(user);
            await _users.ResetAccessFailedCountAsync(user);

            _logger.LogWarning("🔧 اتعاد تعيين كلمة مرور المستخدم {UserName} من أدوات المطور", userName);

            return Ok(new
            {
                success = true,
                message = $"اتعاد تعيين كلمة مرور '{userName}' واتفتح الحساب — سجّل دخول دلوقتي"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل إعادة تعيين كلمة المرور");
            return StatusCode(500, new AuthErrorResponse
            {
                Message   = "فشل إعادة التعيين",
                ErrorType = ex.GetType().Name
            });
        }
    }

    /// <summary>بيجيب دور مدير النظام (<c>ADMIN</c> / <c>SystemAdministrator</c>).</summary>
    private Task<ApplicationRole?> FindAdminRoleAsync(CancellationToken ct) =>
        _roles.Roles.FirstOrDefaultAsync(
            r => r.RoleCode == "ADMIN" || r.Name == "SystemAdministrator", ct);
}
