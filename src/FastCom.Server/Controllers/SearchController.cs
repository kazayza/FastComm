using System.Data;
using System.Data.Common;
using System.Text;
using FastCom.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;   // 🔑 SqlParameter (بييجي transitively مع EFCore.SqlServer)
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Controllers;

/// <summary>
/// 🔍 البحث العام في الشريط العلوي.
/// <para>
/// بيبحث في <b>7 وحدات</b> مرة واحدة: عملاء · حجوزات · عمليات · فواتير · رحلات · حاويات · سيارات.
/// </para>
/// </summary>
/// <remarks>
/// 🔴 ADO.NET مباشرة + <c>SqlParameter</c> — مافيش أي string concatenation في الـ SQL.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]          // ⏳ Step 3.3: هتبقى [Authorize] + فلترة بالصلاحيات
public class SearchController : ControllerBase
{
    /// <summary>أقصى عدد نتائج بنرجّعها.</summary>
    private const int MaxResults = 25;

    /// <summary>أقل عدد حروف للبحث.</summary>
    private const int MinLength = 2;

    /// <summary>الوحدات المدعومة — أي اسم تاني بيرجع قائمة فاضية.</summary>
    private static readonly HashSet<string> AllowedEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "customers", "bookings", "operations",
        "invoices", "trips", "containers", "vehicles"
    };

    private readonly FastComDbContext _db;
    private readonly ILogger<SearchController> _logger;

    public SearchController(FastComDbContext db, ILogger<SearchController> logger)
    {
        _db = db;
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
        // ── التحقق من المدخلات ─────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < MinLength)
            return Ok(new SearchResponse { Results = new List<SearchHit>() });

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
            cmd.CommandText  = BuildSql(take);
            cmd.CommandTimeout = 20;

            // 🔴 لازم SqlParameter (مش DbParameter) — عشان SqlDbType
            var p = new SqlParameter("@q", SqlDbType.NVarChar, 64)
            {
                Value = $"%{term}%"
            };
            cmd.Parameters.Add(p);

            await using var rd = await cmd.ExecuteReaderAsync();

            var hits = new List<SearchHit>();
            while (await rd.ReadAsync())
            {
                hits.Add(new SearchHit
                {
                    Entity    = rd.GetString(rd.GetOrdinal("Entity")),
                    Id        = rd.GetInt64(rd.GetOrdinal("Id")),
                    Code      = rd.GetString(rd.GetOrdinal("Code")),
                    Title     = rd.GetString(rd.GetOrdinal("Title")),
                    Subtitle  = rd.IsDBNull(rd.GetOrdinal("Subtitle"))
                                    ? "" : rd.GetString(rd.GetOrdinal("Subtitle")),
                    Badge     = rd.IsDBNull(rd.GetOrdinal("Badge"))
                                    ? "" : rd.GetString(rd.GetOrdinal("Badge"))
                });
            }

            return Ok(new SearchResponse { Query = term, Count = hits.Count, Results = hits });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل البحث عن: {Term}", term);
            // ⚠️ مانرجّعش 500 — البحث الفاشل مايكسرش الواجهة
            return Ok(new SearchResponse
            {
                Query = term,
                Results = new List<SearchHit>(),
                Error = ex.Message,
                ErrorType = ex.GetType().Name
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

    /// <summary>
    /// بيبني استعلام الـ UNION ALL.
    /// <para>
    /// ⚠️ كل أسماء الجداول والأعمدة <b>ثابتة في الكود</b> — المستخدم بيدخل
    /// كلمة البحث بس، ودي بتروح كـ <c>SqlParameter</c>.
    /// </para>
    /// </summary>
    private static string BuildSql(int take)
    {
        var sb = new StringBuilder(2048);
        sb.Append("SELECT TOP (").Append(take).Append(@")
       Entity, Id, Code, Title, Subtitle, Badge
FROM (
");

        // ── 1) العملاء ──────────────────────────────────────────────────────
        sb.Append(@"    SELECT TOP (").Append(take).Append(@")
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
           OR ISNULL(TaxNumber, N'') LIKE @q)
    UNION ALL
");

        // ── 2) الحجوزات ─────────────────────────────────────────────────────
        sb.Append("    SELECT TOP (").Append(take).Append(@")
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
           OR ISNULL(c.NameAr, N'') LIKE @q)
    UNION ALL
");

        // ── 3) العمليات ─────────────────────────────────────────────────────
        sb.Append("    SELECT TOP (").Append(take).Append(@")
           N'عملية', CAST(o.OperationId AS bigint),
           ISNULL(o.OperationNumber, N''),
           ISNULL(c.NameAr, N'بدون عميل'),
           ISNULL(o.Status, N''),
           CONVERT(nvarchar(10), o.PlannedDate, 23),
           3
    FROM Operations o
         LEFT JOIN Customers c ON c.CustomerId = o.CustomerId
    WHERE o.IsDeleted = 0
      AND (o.OperationNumber LIKE @q OR ISNULL(c.NameAr, N'') LIKE @q)
    UNION ALL
");

        // ── 4) الفواتير ─────────────────────────────────────────────────────
        sb.Append("    SELECT TOP (").Append(take).Append(@")
           N'فاتورة', CAST(i.InvoiceId AS bigint),
           ISNULL(i.InvoiceNumber, N''),
           ISNULL(c.NameAr, N'بدون عميل'),
           ISNULL(i.PaymentStatus, N''),
           FORMAT(i.GrandTotal, N'#,0.00'),
           4
    FROM Invoices i
         LEFT JOIN Customers c ON c.CustomerId = i.CustomerId
    WHERE i.IsDeleted = 0
      AND (i.InvoiceNumber LIKE @q OR ISNULL(c.NameAr, N'') LIKE @q)
    UNION ALL
");

        // ── 5) الرحلات ──────────────────────────────────────────────────────
        sb.Append("    SELECT TOP (").Append(take).Append(@")
           N'رحلة', CAST(t.TripId AS bigint),
           ISNULL(t.TripNumber, N''),
           ISNULL(v.PlateNumber, N'بدون سيارة'),
           ISNULL(t.Status, N''),
           CONVERT(nvarchar(10), t.PlannedStartAt, 23),
           5
    FROM Trips t
         LEFT JOIN Vehicles v ON v.VehicleId = t.VehicleId
    WHERE t.IsDeleted = 0
      AND (t.TripNumber LIKE @q OR ISNULL(v.PlateNumber, N'') LIKE @q)
    UNION ALL
");

        // ── 6) الحاويات ─────────────────────────────────────────────────────
        sb.Append("    SELECT TOP (").Append(take).Append(@")
           N'حاوية', CAST(ct.ContainerId AS bigint),
           ISNULL(ct.ContainerNumber, N''),
           ISNULL(ct.OwnerName, N''),
           ISNULL(ct.CurrentStatus, N''),
           N'',
           6
    FROM Containers ct
    WHERE ct.IsDeleted = 0
      AND (ct.ContainerNumber LIKE @q OR ISNULL(ct.OwnerName, N'') LIKE @q)
    UNION ALL
");

        // ── 7) السيارات ─────────────────────────────────────────────────────
        sb.Append("    SELECT TOP (").Append(take).Append(@")
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
           OR ISNULL(vh.Brand, N'') LIKE @q)
) x
ORDER BY x.SortOrder, x.Code");

        return sb.ToString();
    }

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

        // 🔍 للتشخيص
        public string? Error     { get; set; }
        public string? ErrorType { get; set; }
    }
}
