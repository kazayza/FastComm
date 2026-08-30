using Microsoft.AspNetCore.Identity;

namespace FastCom.Infrastructure.Identity;

/// <summary>
/// دور النظام — الجدول في قاعدة البيانات: AspNetRoles
/// </summary>
public class ApplicationRole : IdentityRole<int>
{
    /// <summary>ADMIN / GM / OPSMGR / OPSEMP / FINMGR / ACCT / HRMGR / FLTMGR / DATAENT / VIEWER / PORTAL</summary>
    public string RoleCode { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }

    /// <summary>الأدوار الأساسية (ADMIN + PORTAL) ممنوع حذفها</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;
}
