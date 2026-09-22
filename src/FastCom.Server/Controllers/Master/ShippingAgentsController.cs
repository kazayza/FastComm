using System.Data;
using System.Data.Common;
using System.Security.Claims;
using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers.Master;

/// <summary>
/// 🆕 الوكلاء الملاحيين — CRUD كامل
/// </summary>
[ApiController]
[Route("api/shipping-agents")]
[Authorize]
public class ShippingAgentsController : ControllerBase
{
    private readonly FastComDbContext _db;
    private readonly ILogger<ShippingAgentsController> _logger;

    public ShippingAgentsController(FastComDbContext db, ILogger<ShippingAgentsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;

    public record ShippingAgentDto(
        int ShippingAgentId, string Code, string NameAr, string? NameEn,
        string? Phone, string? Email, string? Address, string? Notes, bool IsActive);

    public record ShippingAgentUpsertRequest(
        string NameAr, string? NameEn, string? Phone, string? Email,
        string? Address, string? Notes, bool IsActive);

    // ═══════════════ LIST ═══════════════

    [HttpGet]
    [Authorize(Policy = "PERM:SHIPPING_AGENT.VIEW")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _db.ShippingAgents.AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.ShippingAgentId)
            .Select(s => new ShippingAgentDto(
                s.ShippingAgentId, s.Code, s.NameAr, s.NameEn,
                s.Phone, s.Email, s.Address, s.Notes, s.IsActive))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ GET BY ID ═══════════════

    [HttpGet("{id:int}")]
    [Authorize(Policy = "PERM:SHIPPING_AGENT.VIEW")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var s = await _db.ShippingAgents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShippingAgentId == id && !x.IsDeleted, ct);

        if (s is null) return NotFound(new { message = "الوكيل مش موجود" });

        return Ok(new ShippingAgentDto(
            s.ShippingAgentId, s.Code, s.NameAr, s.NameEn,
            s.Phone, s.Email, s.Address, s.Notes, s.IsActive));
    }

    // ═══════════════ CREATE ═══════════════

    [HttpPost]
    [Authorize(Policy = "PERM:SHIPPING_AGENT.CREATE")]
    public async Task<IActionResult> Create([FromBody] ShippingAgentUpsertRequest req, CancellationToken ct)
    {
        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        string code;
        try
        {
            code = await GetNextCodeAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل ترقيم وكيل ملاحي جديد");
            return StatusCode(500, new { message = "تعذّر توليد كود الوكيل — حاول تاني" });
        }

        var s = new ShippingAgent
        {
            Code = code,
            NameAr = req.NameAr.Trim(),
            NameEn = Blank(req.NameEn),
            Phone = Blank(req.Phone),
            Email = Blank(req.Email),
            Address = Blank(req.Address),
            Notes = Blank(req.Notes),
            IsActive = req.IsActive,
            CreatedBy = CurrentUserId()
        };

        _db.ShippingAgents.Add(s);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("وكيل ملاحي جديد {Code} {Name}", code, s.NameAr);
        return Ok(new { id = s.ShippingAgentId, code, message = $"✅ اتعمل الوكيل بكود {code}" });
    }

    // ═══════════════ UPDATE ═══════════════

    [HttpPut("{id:int}")]
    [Authorize(Policy = "PERM:SHIPPING_AGENT.EDIT")]
    public async Task<IActionResult> Update(int id, [FromBody] ShippingAgentUpsertRequest req, CancellationToken ct)
    {
        var err = Validate(req);
        if (err is not null) return BadRequest(new { message = err });

        var s = await _db.ShippingAgents.FirstOrDefaultAsync(x => x.ShippingAgentId == id && !x.IsDeleted, ct);
        if (s is null) return NotFound(new { message = "الوكيل مش موجود" });

        s.NameAr = req.NameAr.Trim();
        s.NameEn = Blank(req.NameEn);
        s.Phone = Blank(req.Phone);
        s.Email = Blank(req.Email);
        s.Address = Blank(req.Address);
        s.Notes = Blank(req.Notes);
        s.IsActive = req.IsActive;
        s.UpdatedAt = DateTime.UtcNow;
        s.UpdatedBy = CurrentUserId();

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "✅ اتحدّث الوكيل" });
    }

    // ═══════════════ TOGGLE ACTIVE ═══════════════

    [HttpPost("{id:int}/toggle-active")]
    [Authorize(Policy = "PERM:SHIPPING_AGENT.EDIT")]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var s = await _db.ShippingAgents.FirstOrDefaultAsync(x => x.ShippingAgentId == id && !x.IsDeleted, ct);
        if (s is null) return NotFound(new { message = "الوكيل مش موجود" });

        s.IsActive = !s.IsActive;
        s.UpdatedAt = DateTime.UtcNow;
        s.UpdatedBy = CurrentUserId();

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = s.IsActive ? "✅ اتفعّل الوكيل" : "🚫 اتعطّل الوكيل", isActive = s.IsActive });
    }

    // ═══════════════ DELETE ═══════════════

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "PERM:SHIPPING_AGENT.DELETE")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var s = await _db.ShippingAgents.FirstOrDefaultAsync(x => x.ShippingAgentId == id && !x.IsDeleted, ct);
        if (s is null) return NotFound(new { message = "الوكيل مش موجود" });

        s.IsDeleted = true;
        s.UpdatedAt = DateTime.UtcNow;
        s.UpdatedBy = CurrentUserId();

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "🗑️ اتحذف الوكيل" });
    }

    // ═══════════════ OPTIONS ═══════════════

    [HttpGet("options")]
    [Authorize(Policy = "PERM:SHIPPING_AGENT.VIEW")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        var rows = await _db.ShippingAgents.AsNoTracking()
            .Where(s => !s.IsDeleted && s.IsActive)
            .OrderBy(s => s.NameAr)
            .Select(s => new { id = s.ShippingAgentId, label = s.NameAr })
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ═══════════════ HELPERS ═══════════════

    private static string? Validate(ShippingAgentUpsertRequest? req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.NameAr))
            return "الاسم بالعربي مطلوب";

        return null;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task<string> GetNextCodeAsync(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "usp_GetNextNumber";
        cmd.CommandTimeout = 30;

        var pType = cmd.CreateParameter();
        pType.ParameterName = "@DocumentType"; pType.Value = "SHIPPING_AGENT";
        cmd.Parameters.Add(pType);

        var pBranch = cmd.CreateParameter();
        pBranch.ParameterName = "@BranchId"; pBranch.Value = DBNull.Value;
        cmd.Parameters.Add(pBranch);

        var pOut = cmd.CreateParameter();
        pOut.ParameterName = "@NextNumber";
        pOut.Direction = ParameterDirection.Output;
        pOut.Size = 40;
        cmd.Parameters.Add(pOut);

        await cmd.ExecuteNonQueryAsync(ct);
        return (string)pOut.Value!;
    }
}
