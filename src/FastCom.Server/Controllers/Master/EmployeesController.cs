using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// الموظفين — البيانات الأساسية للـ HR.
/// <para>بيستخدمهم: العهود (<c>OwnerType=Employee</c>) · السائقين الداخليين · ربط المستخدمين.</para>
/// <para>🔴 الكود بيتولد من <c>usp_GetNextNumber</c> (سلسلة EMPLOYEE) — ماتكتبهوش يدوي.</para>
/// </summary>
[ApiController]
[Route("api/employees")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly INumberingService _numbers;

    public EmployeesController(FastComDbContext db, INumberingService numbers)
    { _db = db; _numbers = numbers; }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    private async Task<int?> ResolveBranchIdAsync(CancellationToken ct)
    {
        if (int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0) return b;
        return await _db.Branches.AsNoTracking()
            .OrderBy(x => x.BranchId).Select(x => (int?)x.BranchId).FirstOrDefaultAsync(ct);
    }

    // ═══════════════ DTOs ═══════════════

    public record ListItem(int EmployeeId, string EmployeeCode, string FullNameAr,
        string? DepartmentName, string? JobTitleName, string? Mobile, DateOnly? HireDate,
        string EmploymentStatus);

    public record Detail(int EmployeeId, string EmployeeCode, string FullNameAr, string? FullNameEn,
        string? NationalId, string? Mobile, string? Email, string? Address, DateOnly? HireDate,
        int? DepartmentId, int? JobTitleId, int? BranchId, int? ManagerEmployeeId,
        string EmploymentStatus);

    public record Upsert(string FullNameAr, string? FullNameEn, string? NationalId, string? Mobile,
        string? Email, string? Address, string? HireDate, int? DepartmentId, int? JobTitleId,
        int? BranchId, int? ManagerEmployeeId, string EmploymentStatus);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:EMPLOYEE.VIEW")]
    public async Task<IActionResult> List(string? status, int? departmentId, string? q,
        int take = 300, CancellationToken ct = default)
    {
        if (take <= 0 || take > 1000) take = 300;

        var query = _db.Employees.AsNoTracking().Where(e => !e.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(e => e.EmploymentStatus == status);
        if (departmentId is not null)
            query = query.Where(e => e.DepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(e =>
                e.FullNameAr.Contains(s) || e.EmployeeCode.Contains(s) ||
                (e.Mobile != null && e.Mobile.Contains(s)));
        }

        var rows = await query
            .OrderByDescending(e => e.EmployeeId)
            .Take(take)
            .Select(e => new ListItem(
                e.EmployeeId, e.EmployeeCode, e.FullNameAr,
                e.Department != null ? e.Department.NameAr : null,
                e.JobTitle != null ? e.JobTitle.NameAr : null,
                e.Mobile, e.HireDate, e.EmploymentStatus))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET ONE ═══════════════

    [HttpGet("{id:int}")]
    [Authorize(Policy = "PERM:EMPLOYEE.VIEW")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var e = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "الموظف غير موجود" });

        return Ok(new Detail(e.EmployeeId, e.EmployeeCode, e.FullNameAr, e.FullNameEn,
            e.NationalId, e.Mobile, e.Email, e.Address, e.HireDate,
            e.DepartmentId, e.JobTitleId, e.BranchId, e.ManagerEmployeeId, e.EmploymentStatus));
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:EMPLOYEE.CREATE")]
    public async Task<IActionResult> Create([FromBody] Upsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, selfId: null, ct);
        if (err is not null) return BadRequest(new { message = err });

        var branchId = await ResolveBranchIdAsync(ct);

        var e = new Employee
        {
            EmployeeCode      = await _numbers.NextAsync("EMPLOYEE", ct),
            FullNameAr        = req.FullNameAr.Trim(),
            FullNameEn        = B(req.FullNameEn),
            NationalId        = B(req.NationalId),
            Mobile            = B(req.Mobile),
            Email             = B(req.Email),
            Address           = B(req.Address),
            HireDate          = ParseDate(req.HireDate),
            DepartmentId      = req.DepartmentId,
            JobTitleId        = req.JobTitleId,
            BranchId          = branchId,
            ManagerEmployeeId = req.ManagerEmployeeId,
            EmploymentStatus  = req.EmploymentStatus,
            CreatedBy         = CurrentUserId()
        };
        _db.Employees.Add(e);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = e.EmployeeId, message = $"✅ اتعمل الموظف بكود {e.EmployeeCode}" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:EMPLOYEE.EDIT")]
    public async Task<IActionResult> Update(int id, [FromBody] Upsert req, CancellationToken ct)
    {
        var err = await ValidateAsync(req, selfId: id, ct);
        if (err is not null) return BadRequest(new { message = err });

        var e = await _db.Employees.FirstOrDefaultAsync(x => x.EmployeeId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "الموظف مش موجود" });

        e.FullNameAr        = req.FullNameAr.Trim();
        e.FullNameEn        = B(req.FullNameEn);
        e.NationalId        = B(req.NationalId);
        e.Mobile            = B(req.Mobile);
        e.Email             = B(req.Email);
        e.Address           = B(req.Address);
        e.HireDate          = ParseDate(req.HireDate);
        e.DepartmentId      = req.DepartmentId;
        e.JobTitleId        = req.JobTitleId;
        e.BranchId          = req.BranchId ?? e.BranchId;
        e.ManagerEmployeeId = req.ManagerEmployeeId;
        e.EmploymentStatus  = req.EmploymentStatus;
        e.UpdatedAt         = DateTime.UtcNow;
        e.UpdatedBy         = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحفظ التعديل" });
    }

    // ═══════════════ DELETE (ناعم) ═══════════════

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:EMPLOYEE.DELETE")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.EmployeeId == id && !x.IsDeleted, ct);
        if (e is null) return NotFound(new { message = "الموظف مش موجود" });

        // 🔴 حواجز الارتباط — الموظف بيبقى مطلوب في أماكن حساسة
        if (await _db.Drivers.AnyAsync(d => d.EmployeeId == id && !d.IsDeleted, ct))
            return BadRequest(new { message = "الموظف مربوط بسائق داخلي — افصل الربط الأول" });
        if (await _db.Users.AnyAsync(u => u.EmployeeId == id))
            return BadRequest(new { message = "الموظف مربوط بحساب مستخدم — افصل الربط الأول" });
        if (await _db.DriverCustodies.AnyAsync(c =>
                !c.IsDeleted && c.OwnerType == "Employee" && c.OwnerId == id &&
                c.Status != "Closed", ct))
            return BadRequest(new { message = "فيه عهدة مفتوحة على الموظف — صفّيها الأول" });
        if (await _db.Employees.AnyAsync(x => x.ManagerEmployeeId == id && !x.IsDeleted, ct))
            return BadRequest(new { message = "الموظف مدير لموظفين تانيين — غيّر المدير الأول" });

        e.IsDeleted  = true;
        e.DeletedAt  = DateTime.UtcNow;
        e.DeletedBy  = CurrentUserId();
        e.EmploymentStatus = "Terminated";
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحذف (ناعم)" });
    }

    // ═══════════════ Helpers ═══════════════

    private static string? B(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : null;

    private async Task<string?> ValidateAsync(Upsert? r, int? selfId, CancellationToken ct)
    {
        if (r is null || string.IsNullOrWhiteSpace(r.FullNameAr)) return "الاسم بالعربي مطلوب";
        if (r.FullNameAr.Trim().Length < 3) return "الاسم قصير أوي";

        if (r.EmploymentStatus is not ("Active" or "OnLeave" or "Suspended" or "Terminated"))
            return "الحالة مش صالحة";

        if (!string.IsNullOrWhiteSpace(r.Email) && !r.Email.Contains('@'))
            return "الإيميل مش صالح";

        if (r.DepartmentId is not null &&
            !await _db.Departments.AnyAsync(d => d.DepartmentId == r.DepartmentId && !d.IsDeleted, ct))
            return "القسم مش موجود";

        if (r.JobTitleId is not null &&
            !await _db.JobTitles.AnyAsync(j => j.JobTitleId == r.JobTitleId && !j.IsDeleted, ct))
            return "المسمى الوظيفي مش موجود";

        if (r.ManagerEmployeeId is not null)
        {
            if (selfId is not null && r.ManagerEmployeeId == selfId)
                return "الموظف مش ممكن يكون مدير نفسه";
            if (!await _db.Employees.AnyAsync(e => e.EmployeeId == r.ManagerEmployeeId && !e.IsDeleted, ct))
                return "المدير مش موجود";
        }

        return null;
    }
}