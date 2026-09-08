using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace FastCom.Server.Auth;

/// <summary>
/// 🔐 Step 3.5: Authorization بالصلاحيات — من قاعدة البيانات مش من الـ Token.
/// </summary>
/// <remarks>
/// <b>ليه كده؟</b>
/// <list type="bullet">
/// <item>الـ <b>107 صلاحية</b> مش في الـ JWT (قرار من Step 3.3 — عشان حجم الـ Token)</item>
/// <item>فحص الصلاحية بيتعمل <b>على الـ Server</b> لكل request — الحقيقة الوحيدة هي الـ DB</item>
/// <item>تغيير صلاحيات مستخدم بينفّذ خلال <b>60 ثانية</b> كحد أقصى (مدة الكاش) من غير Logout</item>
/// </list>
/// <b>الاستخدام:</b> <c>[Authorize(Policy = "PERM:BOOKING.VIEW")]</c>
/// </remarks>
public class PermissionRequirement : IAuthorizationRequirement
{
    /// <summary>كود الصلاحية المطلوبة — زي <c>BOOKING.VIEW</c>.</summary>
    public string Code { get; }

    public PermissionRequirement(string code) => Code = code;
}

/// <summary>
/// بيفحص: هل المستخدم عنده الصلاحية المطلوبة؟
/// <para>
/// 🔴 <b>Fail-closed:</b> أي استثناء (DB وقعت – مستخدم مش موجود) = رفض.
/// </para>
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(
        IPermissionService permissions,
        ILogger<PermissionAuthorizationHandler> logger)
    {
        _permissions = permissions;
        _logger      = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        try
        {
            // ── 1) ADMIN بياخد كل حاجة — مافيش داعي نستعلم ───────────────
            //    (AppRolePermissions بيدي ADMIN كل الـ 107 بـ CROSS JOIN،
            //     بس الـ bypass ده بيوفّر استعلام وبيضمن إنه مايتقفلش أبدًا)
            if (context.User.IsInRole("ADMIN"))
            {
                context.Succeed(requirement);
                return;
            }

            // ── 2) 🔴 A5: الـ UserId عدد صحيح في الـ claim ────────────────
            var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
            {
                _logger.LogWarning("PERM فشل: مافيش NameIdentifier صالح في الـ Token");
                return;   // رفض صامت → 403
            }

            // ── 3) الفحص (من الكاش أو الـ DB) ─────────────────────────────
            // ⚠️ AuthorizationHandlerContext مفيهوش HttpContext —
            //    الـ HttpContext هو context.Resource نفسه.
            var httpContext = context.Resource as Microsoft.AspNetCore.Http.HttpContext;

            if (await _permissions.HasAsync(userId, requirement.Code, httpContext?.RequestAborted ?? default))
            {
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning(
                    "🔒 رفض: المستخدم {User} (Id={UserId}) ماعندوش صلاحية {Code} — {Path}",
                    context.User.Identity?.Name ?? "?",
                    userId,
                    requirement.Code,
                    httpContext?.Request.Path.Value ?? "?");
            }
        }
        catch (Exception ex)
        {
            // 🔴 Fail-closed — مش بنفشل مفتوح أبدًا
            _logger.LogError(ex,
                "PERM فشل فحص الصلاحية {Code} — رفض احترازي", requirement.Code);
        }
    }
}

/// <summary>
/// بيولّد Policies من أسماء على شكل <c>PERM:&lt;كود الصلاحية&gt;</c> فورًا،
/// وبيسلّم أي Policy تانية للـ Provider الافتراضي.
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private const string Prefix = "PERM:";
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!string.IsNullOrEmpty(policyName) &&
            policyName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = policyName[Prefix.Length..];

            AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(code))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}