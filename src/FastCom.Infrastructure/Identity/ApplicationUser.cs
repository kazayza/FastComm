using Microsoft.AspNetCore.Identity;

namespace FastCom.Infrastructure.Identity;

/// <summary>
/// 🔴 A5: مستخدم النظام — IdentityUser&lt;int&gt; بدل جدول AppUsers يدوي.
/// الجدول في قاعدة البيانات: AspNetUsers
/// </summary>
public class ApplicationUser : IdentityUser<int>
{
    public string FullName { get; set; } = string.Empty;

    public int? EmployeeId { get; set; }
    public int? BranchId { get; set; }

    /// <summary>مسار صورة البروفايل — StoragePath في جدول Documents</summary>
    public string? ProfileImagePath { get; set; }

    /// <summary>Active / Inactive / Locked</summary>
    public string UserStatus { get; set; } = "Active";

    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}
