namespace FastCom.Server.Controllers.Admin;

// ════════════════════════════════════════════════════════════════════
//  Step 4 — DTOs المستخدمين / الأدوار / الصلاحيات
// ════════════════════════════════════════════════════════════════════

/// <summary>سطر مستخدم في القوائم.</summary>
public record UserListItemDto(
    int Id,
    string UserName,
    string FullName,
    string? Email,
    bool IsActive,
    string UserStatus,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    string Roles)
{
    public string RolesDisplay => string.Join(" · ", Roles);
}

/// <summary>فرع بسيط للعرض.</summary>
public record BranchDto(int BranchId, string NameAr);

/// <summary>تفاصيل مستخدم واحد + أدواره.</summary>
public record UserDetailDto(
    int Id,
    string UserName,
    string FullName,
    string? Email,
    int? BranchId,
    string? BranchName,
    string UserStatus,
    bool IsActive,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<string> Roles,
    List<int> RoleIds)
{
    public string RolesDisplay => string.Join(" · ", Roles);
}

/// <summary>إنشاء مستخدم جديد.</summary>
public record CreateUserRequest(
    string UserName,
    string Password,
    string FullName,
    string? Email,
    int? BranchId,
    List<int> RoleIds);

/// <summary>تعديل مستخدم.</summary>
public record UpdateUserRequest(
    string FullName,
    string? Email,
    int? BranchId,
    bool IsActive,
    string UserStatus,
    List<int> RoleIds);

/// <summary>تعيين كلمة مرور جديدة (مدير النظام).</summary>
public record AdminResetPasswordRequest(string NewPassword);

/// <summary>استثناء صلاحية لمستخدم معيّن.</summary>
public record UserPermissionOverrideDto(
    int PermissionId,
    int IsGranted,
    string? Notes);

/// <summary>سطر دور في القوائم.</summary>
public record RoleListItemDto(
    int Id,
    string RoleCode,
    string NameAr,
    string? NameEn,
    bool IsSystem,
    bool IsActive,
    int UserCount,
    int PermissionCount)
{
    public string DisplayName => string.IsNullOrEmpty(NameAr) ? (NameEn ?? RoleCode) : NameAr;
}

/// <summary>إنشاء دور.</summary>
public record CreateRoleRequest(string RoleCode, string NameAr, string? NameEn);

/// <summary>تعديل دور.</summary>
public record UpdateRoleRequest(string NameAr, string? NameEn, bool IsActive);

/// <summary>صلاحية واحدة.</summary>
public record PermissionItemDto(
    int PermissionId,
    string PermissionCode,
    string NameAr,
    string? NameEn,
    string ModuleCode,
    string ActionCode,
    int SortOrder);

/// <summary>موديول صلاحيات (تجميع للـ UI).</summary>
public record PermissionModuleDto(string ModuleCode, List<PermissionItemDto> Items);
