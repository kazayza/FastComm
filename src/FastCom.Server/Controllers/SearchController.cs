using System.Data;
using System.Data.Common;
using System.Text;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;   // 🔑 SqlParameter (بييجي transitively مع EFCore.SqlServer)
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers;

/// <summary>
/// 🔍 البحث العام في الشريط العلوي.
/// <para>
/// بيبحث في <b>7 وحدات</b> مرة واحدة: عملاء · حجوزات · عمليات · فواتير · رحلات · حاويات · سيارات.
/// </para>
/// </summary>
/// <remarks>
/// 🔴 <b>Step 3.5:</b>
/// <list type="bullet">
/// <item><c>[Authorize]</c> — لازم توكن</item>
/// <item><b>فلترة بالصلاحيات</b> — كل وحدة ليها كود صلاحية، والوحدات اللي
///       المستخدم ماعندوش صلاحيتها <b>مش بتتحط في الـ SQL أصلًا</b></item>
/// </list>
/// <para>
/// 🔴 ADO.NET مباشرة + <c>SqlParameter</c> — مافيش أي string concatenation في الـ SQL.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]   // 🔐 Step 3.5: البحث للمستخدمين المسجلين بس (كان مفتوح على العالم)
public class SearchController : ControllerBase
{
    /// <summary>أقصى عدد نتائج بنرجّعها.</summary>
    private const int MaxResults = 25;

    /// <summary>أقل عدد حروف للبحث.</summary>
    private const int MinLength = 2;

    // ══════════════════════════════════════════════════════════════════
    //  🔐 كل وحدة والصلاحية المطلوبة ليها
    //  ⚠️ العنصر الأول هو اسم الوحدة اللي بيرجع في النتيجة
    // ══════════════════════════════════════════════════════════════════
    private static readonly (string Entity, string Permission)[] Modules =
    {
        ("عميل",   "CUSTOMER.VIEW"),
        ("حجز",    "BOOKING.VIEW"),
        ("عملية",  "OPERATION.VIEW"),
        ("فاتورة", "INVOICE.VIEW"),
        ("رحلة",   "TRIP.VIEW"),
        ("حاوية",  "OPERATION.VIEW"),
        ("سيارة",  "FLEET.VIEW")
    };

    private readonly FastComDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        FastComDbContext db,
        IPermissionService permissions,
        ILogger<SearchController> logger)
    {
        _db = db;
        _permissions = permissions;
        _logger = logger;
    }

    /// <summary>
    /// <c>GET /api/search?q=كلمة&amp;take=25</c>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] int take = MaxResults)
    {
        // ── 1) 🔐 أنهي وحدات المستخدم يقدر يشوفها؟ ────────────────────────
        var isAdmin = User.IsInRole("ADMIN");

        var allowed = new List<(string Entity, string Permission)>();
        var denied  = new List<string>();

        if (isAdmin)
        {
            allowed.AddRange(Modules);
        }
        else if (int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid))
        {
            foreach (var m in Modules)
            {
                if (await _permissions.HasAsync(uid, m.Permission, HttpContext.RequestAborted))
                    allowed.Add(m);
                else
                    denied.Add(m.Entity);
            }
        }
        else
        {
            denied.AddRange(Modules.Select(m => m.Entity));
        }

        // ── 2) مافيش أي صلاحية بحث؟ ───────────────────────────────────────
        if (allowed.Count == 0)
        {
            return Ok(new SearchResponse
            {
                Query          = q?.Trim() ?? "",
                Results        = new List<SearchHit>(),
                SearchableIn   = new List<string>(),
                DeniedEntities = denied,
                Message        = "ماعندكش صلاحية عرض أي وحدة من وحدات البحث"
            });
        }

        // ── 3) التحقق من المدخلات ─────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < MinLength)
        {
            return Ok(new SearchResponse
            {
                Results        = new List<SearchHit>(),
                SearchableIn   = allowed.Select(m => m.Entity).ToList(),
                DeniedEntities = denied
            });
        }

        var term = q.Trim();
        if (term.Length > 60) term = term.Substring(0, 60);
        take = Math.Clamp(take, 1, MaxResults);

        DbConnection? conn = null;
        bool weOpened = false;

        try
        {
            conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
                weOpened = true;
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText  = BuildSql(take, allowed);
            cmd.CommandTimeout = 20;

            // 🔴 لازم SqlParameter (مش DbParameter) — عشان SqlDbType
            cmd.Parameters.Add(new SqlParameter("@q", SqlDbType.NVarChar, 64)
            {
                Value = $"%{term}%"
            });

            await using var rd = await cmd.ExecuteReaderAsync();

            var hits = new List<SearchHit>();
            while (await rd.ReadAsync())
            {
                hits.Add(new SearchHit
                {
                    Entity   = rd.GetString(rd.GetOrdinal("Entity")),
                    Id       = rd.GetInt64(rd.GetOrdinal("Id")),
                    Code     = rd.GetString(rd.GetOrdinal("Code")),
                    Title    = rd.GetString(rd.GetOrdinal("Title")),
                    Subtitle = rd.IsDBNull(rd.GetOrdinal("Subtitle"))
                                   ? "" : rd.GetString(rd.GetOrdinal("Subtitle")),
                    Badge    = rd.IsDBNull(rd.GetOrdinal("Badge"))
                                   ? "" : rd.GetString(rd.GetOrdinal("Badge"))
                });
            }

            return Ok(new SearchResponse
            {
                Query          = term,
                Count          = hits.Count,
                Results        = hits,
                SearchableIn   = allowed.Select(m => m.Entity).ToList(),
                DeniedEntities = denied
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل البحث عن: {Term}", term);

            // ⚠️ مانرجّعش 500 — البحث الفاشل مايكسرش الواجهة
            return Ok(new SearchResponse
            {
                Query          = term,
                Results        = new List<SearchHit>(),
                SearchableIn   = allowed.Select(m => m.Entity).ToList(),
                DeniedEntities = denied,
                Error          = ex.Message,
                ErrorType      = ex.GetType().Name
            });
        }
        finally
        {
            if (weOpened && conn is not null)
            {
                try { await conn.CloseAsync(); } catch { /* تجاهل */ }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  بناء الـ SQL
    //  🔴 الوحدات المرفوضة مش بتتحط في الاستعلام أصلًا
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// بيبني استعلام الـ UNION ALL للوحدات المسموحة بس.
    /// <para>
    /// ⚠️ كل أسماء الجداول والأعمدة <b>ثابتة في الكود</b> — المستخدم بيدخل
    /// كلمة البحث بس، ودي بتروح كـ <c>SqlParameter</c>.
    /// </para>
    /// </summary>
    private static string BuildSql(int take, List<(string Entity, string Permission)> allowed)
    {
        var parts = new List<string>();

        foreach (var (entity, _) in allowed)
        {
            var frag = Fragment(entity, take);
            if (frag is not null) parts.Add(frag);
        }

        if (parts.Count == 0)
            return "SELECT TOP (0) N'' AS Entity, 0 AS Id, N'' AS Code, N'' AS Title, N'' AS Subtitle, N'' AS Badge";

        return "SELECT TOP (" + take + @")
       Entity, Id, Code, Title, Subtitle, Badge
FROM (
" + string.Join("    UNION ALL\n", parts) + @"
) x
ORDER BY x.SortOrder, x.Code";
    }

    /// <summary>جزء الـ SELECT الخاص بكل وحدة.</summary>
    private static string? Fragment(string entity, int take) => entity switch
    {
        // ── 1) العملاء ────────────────────────────────────────────────────
        "عميل" => $@"    SELECT TOP ({take})
           N'عميل' AS Entity,
           CAST(CustomerId AS bigint) AS Id,
           ISNULL(CustomerCode, N'') AS Code,
           ISNULL(NameAr, N'') AS Title,
           ISNULL(Phone, N'') AS Subtitle,
           ISNULL(CityAr, N'') AS Badge,
           1 AS SortOrder
    FROM Customers
    WHERE IsDeleted = 0
      AND (NameAr LIKE @q OR CustomerCode LIKE @q
           OR ISNULL(NameEn, N'') LIKE @q
           OR ISNULL(Phone, N'') LIKE @q
           OR ISNULL(TaxNumber, N'') LIKE @q)",

        // ── 2) الحجوزات ───────────────────────────────────────────────────
        "حجز" => $@"    SELECT TOP ({take})
           N'حجز', CAST(b.BookingId AS bigint),
           ISNULL(b.BookingNumber, N''),
           ISNULL(c.NameAr, N'بدون عميل'),
           ISNULL(b.Status, N''),
           CONVERT(nvarchar(10), b.BookingDate, 23),
           2
    FROM Bookings b
         LEFT JOIN Customers c ON c.CustomerId = b.CustomerId
    WHERE b.IsDeleted = 0
      AND (b.BookingNumber LIKE @q
           OR ISNULL(b.CustomerReference, N'') LIKE @q
           OR ISNULL(c.NameAr, N'') LIKE @q)",

        // ── 3) العمليات ───────────────────────────────────────────────────
        "عملية" => $@"    SELECT TOP ({take})
           N'عملية', CAST(o.OperationId AS bigint),
           ISNULL(o.OperationNumber, N''),
           ISNULL(c.NameAr, N'بدون عميل'),
           ISNULL(o.Status, N''),
           CONVERT(nvarchar(10), o.PlannedDate, 23),
           3
    FROM Operations o
         LEFT JOIN Customers c ON c.CustomerId = o.CustomerId
    WHERE o.IsDeleted = 0
      AND (o.OperationNumber LIKE @q OR ISNULL(c.NameAr, N'') LIKE @q)",

        // ── 4) الفواتير ───────────────────────────────────────────────────
        "فاتورة" => $@"    SELECT TOP ({take})
           N'فاتورة', CAST(i.InvoiceId AS bigint),
           ISNULL(i.InvoiceNumber, N''),
           ISNULL(c.NameAr, N'بدون عميل'),
           ISNULL(i.PaymentStatus, N''),
           FORMAT(i.GrandTotal, N'#,0.00'),
           4
    FROM Invoices i
         LEFT JOIN Customers c ON c.CustomerId = i.CustomerId
    WHERE i.IsDeleted = 0
      AND (i.InvoiceNumber LIKE @q OR ISNULL(c.NameAr, N'') LIKE @q)",

        // ── 5) الرحلات ────────────────────────────────────────────────────
        "رحلة" => $@"    SELECT TOP ({take})
           N'رحلة', CAST(t.TripId AS bigint),
           ISNULL(t.TripNumber, N''),
           ISNULL(v.PlateNumber, N'بدون سيارة'),
           ISNULL(t.Status, N''),
           CONVERT(nvarchar(10), t.PlannedStartAt, 23),
           5
    FROM Trips t
         LEFT JOIN Vehicles v ON v.VehicleId = t.VehicleId
    WHERE t.IsDeleted = 0
      AND (t.TripNumber LIKE @q OR ISNULL(v.PlateNumber, N'') LIKE @q)",

        // ── 6) الحاويات ───────────────────────────────────────────────────
        "حاوية" => $@"    SELECT TOP ({take})
           N'حاوية', CAST(ct.ContainerId AS bigint),
           ISNULL(ct.ContainerNumber, N''),
           ISNULL(ct.OwnerName, N''),
           ISNULL(ct.CurrentStatus, N''),
           N'',
           6
    FROM Containers ct
    WHERE ct.IsDeleted = 0
      AND (ct.ContainerNumber LIKE @q OR ISNULL(ct.OwnerName, N'') LIKE @q)",

        // ── 7) السيارات ───────────────────────────────────────────────────
        "سيارة" => $@"    SELECT TOP ({take})
           N'سيارة', CAST(vh.VehicleId AS bigint),
           ISNULL(vh.PlateNumber, N''),
           ISNULL(vh.VehicleCode, N''),
           ISNULL(vh.Status, N''),
           ISNULL(vh.Brand, N'') + N' ' + ISNULL(vh.Model, N''),
           7
    FROM Vehicles vh
    WHERE vh.IsDeleted = 0
      AND (vh.PlateNumber LIKE @q
           OR ISNULL(vh.VehicleCode, N'') LIKE @q
           OR ISNULL(vh.Brand, N'') LIKE @q)",

        _ => null
    };

    /// <summary>نتيجة بحث واحدة.</summary>
    public class SearchHit
    {
        public string Entity   { get; set; } = "";
        public long   Id       { get; set; }
        public string Code     { get; set; } = "";
        public string Title    { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Badge    { get; set; } = "";
    }

    /// <summary>استجابة الـ endpoint.</summary>
    public class SearchResponse
    {
        public string Query { get; set; } = "";
        public int Count { get; set; }
        public List<SearchHit> Results { get; set; } = new();

        /// <summary>🔐 الوحدات اللي المستخدم يقدر يبحث فيها.</summary>
        public List<string> SearchableIn { get; set; } = new();

        /// <summary>🔐 الوحدات اللي ماعندوش صلاحيتها.</summary>
        public List<string> DeniedEntities { get; set; } = new();

        public string? Message { get; set; }

        // 🔍 للتشخيص
        public string? Error     { get; set; }
        public string? ErrorType { get; set; }
    }
}
