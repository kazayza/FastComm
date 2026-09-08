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
/// 🔐 Step 4 — إدارة الأدوار + مصفوفة الصلاحيات.
/// </summary>
/// <remarks>
/// 🔴 دور النظام (IsSystem = ADMIN) ممنوع تعديله أو حذفه —
/// الـ ADMIN عنده bypass كامل في الـ PermissionAuthorizationHandler أصلًا.
/// </remarks>
[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly FastComDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly ILogger<RolesController> _logger;

    public RolesController(
        RoleManager<ApplicationRole> roles,
        FastComDbContext db,
        IPermissionService permissions,
        ILogger<RolesController> logger)
    {
        _roles       = roles;
        _db          = db;
        _permissions = permissions;
        _logger      = logger;
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    // ════════════════════════ LIST ════════════════════════

    /// <summary>كل الأدوار + عدد المستخدمين + عدد الصلاحيات.</summary>
    [HttpGet]
    [Authorize(Policy = "PERM:USER.VIEW")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var roles = await _db.Roles.AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var userCounts = await _db.UserRoles
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.C, ct);

        var permCounts = await _db.AppRolePermissions
            .GroupBy(p => p.RoleId)
            .Select(g => new { RoleId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.C, ct);

        return Ok(roles.Select(r => new RoleListItemDto(
            r.Id,
            r.RoleCode,
            r.NameAr,
            r.NameEn,
            r.IsSystem,
            r.IsActive,
            userCounts.TryGetValue(r.Id, out var uc) ? uc : 0,
            permCounts.TryGetValue(r.Id, out var pc) ? pc : 0)));
    }

    // ════════════════════════ CREATE ════════════════════════

    /// <summary>إنشاء دور جديد (بيبدأ بصلاحيات صفر).</summary>
    [HttpPost]
    [Authorize(Policy = "PERM:ROLE.MANAGE")]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.RoleCode) || string.IsNullOrWhiteSpace(req?.NameAr))
            return BadRequest(new { message = "كود الدور والاسم العربي مطلوبين" });

        var code = req.RoleCode.Trim().ToUpperInvariant();

        if (await _db.Roles.AnyAsync(r => r.RoleCode == code))
            return BadRequest(new { message = $"فيه دور بنفس الكود {code}" });

        var role = new ApplicationRole
        {
            Name     = code,          // الـ Identity Name = الكود (بيتستخدم في AddToRolesAsync)
            RoleCode = code,
            NameAr   = req.NameAr.Trim(),
            NameEn   = string.IsNullOrWhiteSpace(req.NameEn) ? null : req.NameEn.Trim(),
            IsSystem = false,
            IsActive = true
        };

        var res = await _roles.CreateAsync(role);
        if (!res.Succeeded)
            return BadRequest(new { message = string.Join(" · ", res.Errors.Select(e => e.Description)) });

        _logger.LogInformation("دور جديد: {Code} بواسطة {By}", code, CurrentUserId());
        return Ok(new { id = role.Id, message = "✅ اتعمل الدور — وزّع له صلاحيات من المصفوفة" });
    }

    // ════════════════════════ UPDATE ════════════════════════

    /// <summary>تعديل اسم دور / تفعيله / تعطيله.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:ROLE.MANAGE")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRoleRequest req)
    {
        var role = await _roles.FindByIdAsync(id.ToString());
        if (role is null) return NotFound(new { message = "الدور مش موجود" });

        if (role.IsSystem)
            return BadRequest(new { message = "الأدوار الأساسية (زي مدير النظام) مش قابلة للتعديل" });

        if (string.IsNullOrWhiteSpace(req?.NameAr))
            return BadRequest(new { message = "الاسم العربي مطلوب" });

        role.NameAr   = req.NameAr.Trim();
        role.NameEn   = string.IsNullOrWhiteSpace(req.NameEn) ? null : req.NameEn.Trim();
        role.IsActive = req.IsActive;

        var res = await _roles.UpdateAsync(role);
        if (!res.Succeeded)
            return BadRequest(new { message = string.Join(" · ", res.Errors.Select(e => e.Description)) });

        // تعطيل دور = صلاحيات مستخدميه تتغير → نضّف كاش الكل اللي فيه
        if (!req.IsActive)
            await InvalidateRoleUsers(id);

        _logger.LogInformation("تعديل دور {Id} بواسطة {By}", id, CurrentUserId());
        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    // ════════════════════════ DELETE ════════════════════════

    /// <summary>حذف دور — ممنوع للنظام أو لو عليه مستخدمين.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:ROLE.MANAGE")]
    public async Task<IActionResult> Delete(int id)
    {
        var role = await _roles.FindByIdAsync(id.ToString());
        if (role is null) return NotFound(new { message = "الدور مش موجود" });

        if (role.IsSystem)
            return BadRequest(new { message = "الأدوار الأساسية مش قابلة للحذف" });

        var usersOnIt = await _db.UserRoles.CountAsync(ur => ur.RoleId == id);
        if (usersOnIt > 0)
            return BadRequest(new { message = $"فيه {usersOnIt} مستخدم على الدور ده — انقلهم الأول" });

        var res = await _roles.DeleteAsync(role);
        if (!res.Succeeded)
            return BadRequest(new { message = string.Join(" · ", res.Errors.Select(e => e.Description)) });

        _logger.LogInformation("حذف دور {Id} بواسطة {By}", id, CurrentUserId());
        return Ok(new { message = "✅ اتحذف الدور" });
    }

    // ════════════════════════ PERMISSIONS CATALOG ════════════════════════

    /// <summary>كتالوج الصلاحيات الـ 107 متجمع بالموديول.</summary>
    [HttpGet("/api/permissions")]
    [Authorize(Policy = "PERM:USER.VIEW")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        var perms = await _db.AppPermissions.AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .Select(p => new PermissionItemDto(
                p.PermissionId, p.PermissionCode, p.NameAr, p.NameEn,
                p.ModuleCode, p.ActionCode, p.SortOrder))
            .ToListAsync(ct);

        var modules = perms
            .GroupBy(p => p.ModuleCode)
            .Select(g => new PermissionModuleDto(g.Key, g.ToList()))
            .ToList();

        return Ok(modules);
    }

    // ════════════════════════ ROLE PERMISSION MATRIX ════════════════════════

    /// <summary>الـ ids الممنوحة لدور.</summary>
    [HttpGet("{id:int}/permissions")]
    [Authorize(Policy = "PERM:ROLE.MANAGE")]
    public async Task<IActionResult> GetMatrix(int id, CancellationToken ct)
    {
        if (!await _db.Roles.AnyAsync(r => r.Id == id, ct))
            return NotFound(new { message = "الدور مش موجود" });

        var ids = await _db.AppRolePermissions.AsNoTracking()
            .Where(p => p.RoleId == id)
            .Select(p => p.PermissionId)
            .OrderBy(x => x)
            .ToListAsync(ct);

        return Ok(ids);
    }

    /// <summary>حفظ مصفوفة دور (استبدال كامل) + تنظيف كاش مستخدميه.</summary>
    [HttpPut("{id:int}/permissions")]
    [Authorize(Policy = "PERM:ROLE.MANAGE")]
    public async Task<IActionResult> PutMatrix(int id, [FromBody] List<int> permissionIds, CancellationToken ct)
    {
        var role = await _roles.FindByIdAsync(id.ToString());
        if (role is null) return NotFound(new { message = "الدور مش موجود" });

        if (role.IsSystem)
            return BadRequest(new { message = "صلاحيات الأدوار الأساسية مش قابلة للتعديل" });

        permissionIds ??= new();
        var ids = permissionIds.Distinct().ToList();

        var existing = await _db.AppPermissions
            .Where(p => ids.Contains(p.PermissionId))
            .Select(p => p.PermissionId)
            .ToListAsync(ct);

        if (existing.Count != ids.Count)
            return BadRequest(new { message = "فيه صلاحيات غير موجودة في الكتالوج" });

        var old = await _db.AppRolePermissions.Where(p => p.RoleId == id).ToListAsync(ct);
        _db.AppRolePermissions.RemoveRange(old);

        var by = CurrentUserId();
        _db.AppRolePermissions.AddRange(ids.Select(pid => new AppRolePermission
        {
            RoleId       = id,
            PermissionId = pid,
            GrantedBy    = by
        }));

        await _db.SaveChangesAsync(ct);
        await InvalidateRoleUsers(id);

        _logger.LogInformation("حفظ مصفوفة الدور {Id}: {Count} صلاحية بواسطة {By}", id, ids.Count, by);
        return Ok(new { message = $"✅ اتحفظت {ids.Count} صلاحية — بتتفعّل خلال دقيقة" });
    }

    // ════════════════════════ helpers ════════════════════════

    /// <summary>تنضيف كاش الصلاحيات لكل مستخدمي دور.</summary>
    private async Task InvalidateRoleUsers(int roleId)
    {
        var userIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync();

        foreach (var uid in userIds)
            _permissions.Invalidate(uid);
    }
}
