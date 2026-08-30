using System.ComponentModel.DataAnnotations;

namespace FastCom.Server.Controllers;

/// <summary>
/// 🔐 DTOs بتاعة الـ Authentication.
/// <para>
/// ⚠️ الـ <see cref="AuthController"/> هو اللي بيستخدمها.
/// </para>
/// </summary>

/// <summary>طلب تسجيل الدخول.</summary>
public class LoginRequest
{
    /// <summary>اسم المستخدم أو البريد الإلكتروني.</summary>
    [Required(ErrorMessage = "اسم المستخدم مطلوب")]
    public string UserName { get; set; } = "";

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    public string Password { get; set; } = "";

    /// <summary>تذكّرني — بتمدّد مدة الـ Token.</summary>
    public bool RememberMe { get; set; }
}

/// <summary>استجابة تسجيل الدخول الناجحة.</summary>
public class LoginResponse
{
    public string Token { get; set; } = "";

    /// <summary>وقت انتهاء الـ Token (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    public string UserName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public int? BranchId { get; set; }

    /// <summary>أكواد الأدوار — زي <c>ADMIN</c> / <c>OPSMGR</c>.</summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// 🔴 الـ 107 صلاحية — مش بنحطها في الـ JWT (هيكبر أوي)،
    /// بنرجّعها هنا والـ Client بيخزّنها.
    /// </summary>
    public List<string> Permissions { get; set; } = new();
}

/// <summary>بيانات المستخدم الحالي — <c>GET /api/auth/me</c>.</summary>
public class MeResponse
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public int? BranchId { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
}

/// <summary>استجابة فشل موحّدة.</summary>
public class AuthErrorResponse
{
    public bool Success { get; set; } = false;
    public string Message { get; set; } = "";

    /// <summary>🔍 للتشخيص — نوع الاستثناء (بيظهر في Development بس).</summary>
    public string? ErrorType { get; set; }
}

/// <summary>طلب إنشاء أول مستخدم مدير (Development بس).</summary>
public class SeedAdminRequest
{
    [Required] public string UserName { get; set; } = "admin";
    [Required] public string Password { get; set; } = "";
    [Required] public string FullName { get; set; } = "مدير النظام";
    public string? Email { get; set; }
}
