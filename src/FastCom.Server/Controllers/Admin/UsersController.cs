using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Identity;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Admin;

/// <summary>
/// 🔐 Step 4 — إدارة المستخدمين (عرض/إنشاء/تعديل/تعطيل/كلمة مرور/استثناءات صلاحيات).
/// </summary>
/// <remarks>
/// 🔴 مافيش حذف نهائي — التعطيل بس (IsActive=0 + UserStatus=Inactive)،
/// عشان الـ Audit والـ FKs بتاعت الإشعارات والحركات.
/// </remarks>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly FastComDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        UserManager<ApplicationUser> users,
        FastComDbContext db,
        IPermissionService permissions,
        ILogger<UsersController> logger)
    {
        _users       = users;
        _db          = db;
        _permissions = permissions;
        _logger      = logger;
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    // ════════════════════════ LIST ════════════════════════

    /// <summary>قائمة المستخدمين + أدوارهم.</summary>
    [HttpGet]
    [Authorize(Policy = "PERM:USER.VIEW")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                u.Id, u.UserName, u.FullName, u.Email,
                u.IsActive, u.UserStatus, u.LastLoginAt, u.CreatedAt
            })
            .ToListAsync(ct);

        var roleRows = await _db.UserRoles
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);

        var byUser = roleRows.GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name ?? "").ToList());

        return Ok(users.Select(u => new UserListItemDto(
            u.Id,
            u.UserName ?? "",
            u.FullName,
            u.Email,
            u.IsActive,
            u.UserStatus,
            u.LastLoginAt,
            u.CreatedAt,
            string.Join(" · ", byUser.TryGetValue(u.Id, out var r) ? r : new List<string>()))));
    }

    // ════════════════════════ GET ONE ════════════════════════

    /// <summary>تفاصيل مستخدم (للفورم).</summary>
    [HttpGet("{id:int}")]
    [Authorize(Policy = "PERM:USER.VIEW")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound(new { message = "المستخدم مش موجود" });

        var branchName = u.BranchId is null
            ? null
            : await _db.Branches.AsNoTracking()
                .Where(b => b.BranchId == u.BranchId)
                .Select(b => b.NameAr).FirstOrDefaultAsync(ct);

        var roleRows = await _db.UserRoles
            .Where(ur => ur.UserId == id)
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { r.Id, r.Name })
            .ToListAsync(ct);

        return Ok(new UserDetailDto(
            u.Id,
            u.UserName ?? "",
            u.FullName,
            u.Email,
            u.BranchId,
            branchName,
            u.UserStatus,
            u.IsActive,
            u.LastLoginAt,
            u.CreatedAt,
            u.UpdatedAt,
            roleRows.Select(r => r.Name ?? "").ToList(),
            roleRows.Select(r => r.Id).ToList()));
    }

    // ════════════════════════ CREATE ════════════════════════

    /// <summary>إنشاء مستخدم — الـ hash بيطلع من Identity نفسه (فورمات مضمون).</summary>
    [HttpPost]
    [Authorize(Policy = "PERM:USER.MANAGE")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.UserName) ||
            string.IsNullOrWhiteSpace(req?.FullName) ||
            string.IsNullOrWhiteSpace(req?.Password))
        {
            return BadRequest(new { message = "اسم المستخدم والاسم الكامل وكلمة المرور مطلوبين" });
        }

        if (req.Password.Length < 8)
            return BadRequest(new { message = "كلمة المرور لازم تكون 8 حروف على الأقل" });

        var user = new ApplicationUser
        {
            UserName   = req.UserName.Trim(),
            FullName   = req.FullName.Trim(),
            Email      = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            BranchId   = req.BranchId,
            IsActive   = true,
            UserStatus = "Active"
        };

        var created = await _users.CreateAsync(user, req.Password);
        if (!created.Succeeded)
        {
            return BadRequest(new
            {
                message = "فشل إنشاء المستخدم: " +
                          string.Join(" · ", created.Errors.Select(e => e.Description))
            });
        }

        // ── الأدوار ──
        if (req.RoleIds is { Count: > 0 })
        {
            var names = await _db.Roles.Where(r => req.RoleIds.Contains(r.Id))
                .Select(r => r.Name).ToListAsync(ct);
            var added = await _users.AddToRolesAsync(user, names!);
            if (!added.Succeeded)
            {
                _logger.LogWarning("المستخدم اتعمل بس إضافة الأدوار فشلت: {Errors}",
                    string.Join(" · ", added.Errors.Select(e => e.Description)));
            }
        }

        _logger.LogInformation("مستخدم جديد: {UserName} بواسطة {By}", user.UserName, CurrentUserId());
        return Ok(new { id = user.Id, message = "✅ اتعمل المستخدم" });
    }

    // ════════════════════════ UPDATE ════════════════════════

    /// <summary>تعديل بيانات مستخدم + مزامنة الأدوار.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:USER.MANAGE")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound(new { message = "المستخدم مش موجود" });

        if (req is null || string.IsNullOrWhiteSpace(req.FullName))
            return BadRequest(new { message = "الاسم الكامل مطلوب" });

        if (req.UserStatus is not ("Active" or "Inactive" or "Locked"))
            return BadRequest(new { message = "حالة المستخدم مش صالحة" });

        // 🔴 ماتقدرش تعطّل نفسك
        if (id == CurrentUserId() && (!req.IsActive || req.UserStatus != "Active"))
            return BadRequest(new { message = "مش ممكن تعطّل حسابك أنت" });

        user.FullName   = req.FullName.Trim();
        user.Email      = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        user.BranchId   = req.BranchId;
        user.IsActive   = req.IsActive;
        user.UserStatus = req.UserStatus;
        user.UpdatedAt  = DateTime.UtcNow;
        user.UpdatedBy  = CurrentUserId();

        var saved = await _users.UpdateAsync(user);
        if (!saved.Succeeded)
            return BadRequest(new { message = string.Join(" · ", saved.Errors.Select(e => e.Description)) });

        // ── مزامنة الأدوار ──
        var currentNames = await _users.GetRolesAsync(user);
        if (currentNames.Count > 0)
            await _users.RemoveFromRolesAsync(user, currentNames);

        if (req.RoleIds is { Count: > 0 })
        {
            var names = await _db.Roles.Where(r => req.RoleIds.Contains(r.Id))
                .Select(r => r.Name).ToListAsync(ct);
            await _users.AddToRolesAsync(user, names!);
        }

        // تغيير الأدوار = تغيير صلاحيات → نضّف الكاش فورًا
        _permissions.Invalidate(id);

        _logger.LogInformation("تعديل مستخدم {Id} بواسطة {By}", id, CurrentUserId());
        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    // ════════════════════════ DISABLE (soft delete) ════════════════════════

    /// <summary>تعطيل مستخدم — مافيش حذف نهائي.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:USER.MANAGE")]
    public async Task<IActionResult> Disable(int id)
    {
        if (id == CurrentUserId())
            return BadRequest(new { message = "مش ممكن تعطّل حسابك أنت" });

        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound(new { message = "المستخدم مش موجود" });

        user.IsActive   = false;
        user.UserStatus = "Inactive";
        user.UpdatedAt  = DateTime.UtcNow;
        user.UpdatedBy  = CurrentUserId();

        await _users.UpdateAsync(user);
        _permissions.Invalidate(id);

        _logger.LogInformation("تعطيل مستخدم {Id} بواسطة {By}", id, CurrentUserId());
        return Ok(new { message = "✅ اتعطّل المستخدم — تقدر تفعّله تاني من التعديل" });
    }

    // ════════════════════════ RESET PASSWORD ════════════════════════

    /// <summary>مدير النظام يعيّن كلمة مرور جديدة لأي مستخدم + يفتح القفل.</summary>
    [HttpPost("{id:int}/reset-password")]
    [Authorize(Policy = "PERM:USER.MANAGE")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] AdminResetPasswordRequest req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
            return BadRequest(new { message = "كلمة المرور لازم تكون 8 حروف على الأقل" });

        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound(new { message = "المستخدم مش موجود" });

        var remove = await _users.RemovePasswordAsync(user);
        if (!remove.Succeeded)
            return BadRequest(new
            {
                message = "فشل إزالة كلمة المرور القديمة: " +
                          string.Join(" · ", remove.Errors.Select(e => e.Description))
            });

        var add = await _users.AddPasswordAsync(user, req.NewPassword);
        if (!add.Succeeded)
            return BadRequest(new
            {
                message = "كلمة المرور مرفوضة: " +
                          string.Join(" · ", add.Errors.Select(e => e.Description))
            });

        // فتح الحساب لو مقفول + تصفير العدّاد
        user.LockoutEnd = null;
        await _users.UpdateAsync(user);
        await _users.ResetAccessFailedCountAsync(user);

        _logger.LogInformation("تعيين كلمة مرور للمستخدم {Id} بواسطة {By}", id, CurrentUserId());
        return Ok(new { message = "✅ اتعيّنت كلمة المرور واتفتح الحساب" });
    }

    // ════════════════════════ USER PERMISSION OVERRIDES ════════════════════════

    /// <summary>الاستثناءات المباشرة بتاعة مستخدم (منح/منع).</summary>
    [HttpGet("{id:int}/permissions")]
    [Authorize(Policy = "PERM:USER.MANAGE")]
    public async Task<IActionResult> GetOverrides(int id, CancellationToken ct)
    {
        var rows = await _db.AppUserPermissions.AsNoTracking()
            .Where(p => p.UserId == id)
            .Select(p => new UserPermissionOverrideDto(
                p.PermissionId, p.IsGranted ? 1 : 0, p.Notes))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>حفظ الاستثناءات (استبدال كامل).</summary>
    [HttpPut("{id:int}/permissions")]
    [Authorize(Policy = "PERM:USER.MANAGE")]
    public async Task<IActionResult> PutOverrides(
        int id, [FromBody] List<UserPermissionOverrideDto> overrides, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound(new { message = "المستخدم مش موجود" });

        overrides ??= new();

        // نتأكد إن كل الصلاحيات موجودة فعلًا
        var ids = overrides.Select(o => o.PermissionId).Distinct().ToList();
        var existing = await _db.AppPermissions
            .Where(p => ids.Contains(p.PermissionId))
            .Select(p => p.PermissionId)
            .ToListAsync(ct);

        if (existing.Count != ids.Count)
            return BadRequest(new { message = "فيه صلاحيات غير موجودة في القائمة" });

        var old = await _db.AppUserPermissions.Where(p => p.UserId == id).ToListAsync(ct);
        _db.AppUserPermissions.RemoveRange(old);

        var by = CurrentUserId();
        _db.AppUserPermissions.AddRange(overrides.Select(o => new AppUserPermission
        {
            UserId       = id,
            PermissionId = o.PermissionId,
            IsGranted    = o.IsGranted != 0,
            GrantedBy    = by,
            Notes        = string.IsNullOrWhiteSpace(o.Notes) ? null : o.Notes
        }));

        await _db.SaveChangesAsync(ct);
        _permissions.Invalidate(id);

        _logger.LogInformation("تعديل استثناءات صلاحيات المستخدم {Id} ({Count}) بواسطة {By}",
            id, overrides.Count, by);
        return Ok(new { message = "✅ اتحفظت الاستثناءات — بتتفعّل خلال دقيقة" });
    }
}
