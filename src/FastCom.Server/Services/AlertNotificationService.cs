using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Services;

/// <summary>
/// خدمة خلفية تحوّل تنبيهات لوحة المؤشرات إلى إشعارات جرس حقيقية:
/// تفحص دوريًا (بعد الإقلاع ثم كل 6 ساعات) العمليات المتأخرة والفواتير
/// المستحقة والعهد المفتوحة قديمًا وانتهاءات الرخص والتأمينات والوثائق
/// والصيانة المستحقة، وتنشئ إشعارًا لحملة الصلاحية المعنية بكل فئة.
///
/// منع التكرار: لا يُنشأ إشعار جديد لنفس (المستخدم + النوع + الكيان)
/// ما دام إشعار سابق غير مقروء، أو أُنشئ خلال آخر 7 أيام.
/// </summary>
public class AlertNotificationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AlertNotificationService> _log;

    public AlertNotificationService(IServiceScopeFactory scopes,
        ILogger<AlertNotificationService> log)
    {
        _scopes = scopes;
        _log    = log;
    }

    private static readonly string[] OpenOpStatuses =
    {
        "Pending", "Assigned", "DriverReceived", "InTransit", "Delivered",
        "ExpensesPending", "CustodyPending", "ReadyToClose"
    };

    private static readonly string[] OpenCustodyStatuses = { "Open", "PartiallySettled", "Submitted" };

    private sealed record Alert(string Type, string EntityType, long EntityId,
        string Title, string Message, string Priority, string Permission);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FastComDbContext>();
                var created = await ScanAsync(db, stoppingToken);
                if (created > 0)
                    _log.LogInformation("خدمة التنبيهات: أُنشئ {Count} إشعارًا", created);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "فشل فحص التنبيهات الدوري — ستعاد المحاولة في الدورة التالية");
            }

            try { await Task.Delay(TimeSpan.FromHours(6), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task<int> ScanAsync(FastComDbContext db, CancellationToken ct)
    {
        var today   = DateOnly.FromDateTime(DateTime.Today);
        var todayDt = DateTime.Today;
        var soon    = today.AddDays(30);
        var alerts  = new List<Alert>();

        /* عمليات متأخرة عن تاريخها المخطط */
        var ops = await db.Operations.AsNoTracking()
            .Where(o => !o.IsDeleted && OpenOpStatuses.Contains(o.Status) && o.PlannedDate < todayDt)
            .OrderBy(o => o.PlannedDate).Take(20)
            .Select(o => new { o.OperationId, o.OperationNumber, o.PlannedDate })
            .ToListAsync(ct);
        foreach (var o in ops)
        {
            if (o.PlannedDate is null) continue;
            var days = today.DayNumber - DateOnly.FromDateTime(o.PlannedDate.Value).DayNumber;
            alerts.Add(new Alert("Alert.OverdueOperation", "Operation", o.OperationId,
                "عملية متأخرة",
                $"العملية {o.OperationNumber} متأخرة منذ {days} يوم عن تاريخها المخطط",
                days >= 7 ? "High" : "Normal", "OPERATION.VIEW"));
        }

        /* فواتير عملاء فات موعد استحقاقها وبها متبقٍّ */
        var inv = await db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.DueDate != null && i.DueDate < today
                        && i.GrandTotal - i.PaidAmount > 0)
            .OrderBy(i => i.DueDate).Take(20)
            .Select(i => new { i.InvoiceId, i.InvoiceNumber, i.DueDate, Due = i.GrandTotal - i.PaidAmount })
            .ToListAsync(ct);
        foreach (var i in inv)
            alerts.Add(new Alert("Alert.OverdueInvoice", "Invoice", i.InvoiceId,
                "فاتورة فات موعد استحقاقها",
                $"الفاتورة {i.InvoiceNumber} متبقٍّ عليها {i.Due:N0} ج.م منذ {today.DayNumber - i.DueDate!.Value.DayNumber} يوم",
                "High", "INVOICE.VIEW"));

        /* عملاء تجاوزوا حد الائتمان */
        var overLimit = await db.Invoices.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != "Draft" && i.Status != "Cancelled"
                        && i.Customer.CreditLimit > 0)
            .GroupBy(i => new { i.CustomerId, i.Customer.NameAr, i.Customer.CreditLimit })
            .Select(g => new
            {
                g.Key.CustomerId, g.Key.NameAr, g.Key.CreditLimit,
                Open = g.Sum(x => x.GrandTotal - x.PaidAmount)
            })
            .ToListAsync(ct);
        foreach (var c in overLimit.Where(x => x.Open > x.CreditLimit).Take(20))
            alerts.Add(new Alert("Alert.CreditLimit", "Customer", c.CustomerId,
                "عميل تجاوز حد الائتمان",
                $"العميل {c.NameAr} مستحق عليه {c.Open:N0} ج.م وحد ائتمانه {c.CreditLimit:N0} ج.م",
                "High", "INVOICE.VIEW"));

        /* عهد مفتوحة منذ 7 أيام فأكثر */
        var agedDt = DateTime.Today.AddDays(-7);
        var cus = await db.DriverCustodies.AsNoTracking()
            .Where(c => !c.IsDeleted && OpenCustodyStatuses.Contains(c.Status) && c.CustodyDate <= agedDt)
            .OrderBy(c => c.CustodyDate).Take(20)
            .Select(c => new { c.CustodyId, c.CustodyNumber, c.CustodyDate })
            .ToListAsync(ct);
        foreach (var c in cus)
        {
            var days = today.DayNumber - DateOnly.FromDateTime(c.CustodyDate).DayNumber;
            alerts.Add(new Alert("Alert.OpenCustody", "Custody", c.CustodyId,
                "عهدة مفتوحة منذ فترة",
                $"العهدة {c.CustodyNumber} مفتوحة منذ {days} يوم دون تسوية",
                "Normal", "CUSTODY.VIEW"));
        }

        /* رخص السائقين المنتهية أو القريبة */
        var drv = await db.Drivers.AsNoTracking()
            .Where(d => !d.IsDeleted && d.LicenseExpiryDate != null && d.LicenseExpiryDate <= soon)
            .OrderBy(d => d.LicenseExpiryDate).Take(20)
            .Select(d => new { d.DriverId, d.FullName, d.LicenseExpiryDate })
            .ToListAsync(ct);
        foreach (var d in drv)
        {
            var expired = d.LicenseExpiryDate < today;
            alerts.Add(new Alert("Alert.DriverLicense", "Driver", d.DriverId,
                expired ? "رخصة سائق منتهية" : "رخصة سائق تقترب من الانتهاء",
                expired
                    ? $"رخصة السائق {d.FullName} انتهت بتاريخ {d.LicenseExpiryDate:yyyy-MM-dd}"
                    : $"رخصة السائق {d.FullName} تنتهي بتاريخ {d.LicenseExpiryDate:yyyy-MM-dd}",
                expired ? "High" : "Normal", "DRIVER.VIEW"));
        }

        /* رخص وتأمينات المركبات */
        var veh = await db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted &&
                        ((v.LicenseExpiryDate != null && v.LicenseExpiryDate <= soon) ||
                         (v.InsuranceExpiryDate != null && v.InsuranceExpiryDate <= soon)))
            .Take(20)
            .Select(v => new { v.VehicleId, v.PlateNumber, v.LicenseExpiryDate, v.InsuranceExpiryDate })
            .ToListAsync(ct);
        foreach (var v in veh)
        {
            if (v.LicenseExpiryDate is not null && v.LicenseExpiryDate <= soon)
            {
                var expired = v.LicenseExpiryDate < today;
                alerts.Add(new Alert("Alert.VehicleLicense", "Vehicle", v.VehicleId,
                    expired ? "رخصة سيارة منتهية" : "رخصة سيارة تقترب من الانتهاء",
                    $"رخصة السيارة {v.PlateNumber} {(expired ? "انتهت" : "تنتهي")} بتاريخ {v.LicenseExpiryDate:yyyy-MM-dd}",
                    expired ? "High" : "Normal", "FLEET.VIEW"));
            }
            if (v.InsuranceExpiryDate is not null && v.InsuranceExpiryDate <= soon)
            {
                var expired = v.InsuranceExpiryDate < today;
                alerts.Add(new Alert("Alert.VehicleInsurance", "Vehicle", v.VehicleId,
                    expired ? "تأمين سيارة منتهٍ" : "تأمين سيارة يقترب من الانتهاء",
                    $"تأمين السيارة {v.PlateNumber} {(expired ? "انتهى" : "ينتهي")} بتاريخ {v.InsuranceExpiryDate:yyyy-MM-dd}",
                    expired ? "High" : "Normal", "FLEET.VIEW"));
            }
        }

        /* رخص وتأمينات المقطورات */
        var trl = await db.Trailers.AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        ((x.LicenseExpiryDate != null && x.LicenseExpiryDate <= soon) ||
                         (x.InsuranceExpiryDate != null && x.InsuranceExpiryDate <= soon)))
            .Take(20)
            .Select(x => new { x.TrailerId, x.TrailerCode, x.LicenseExpiryDate, x.InsuranceExpiryDate })
            .ToListAsync(ct);
        foreach (var x in trl)
        {
            if (x.LicenseExpiryDate is not null && x.LicenseExpiryDate <= soon)
            {
                var expired = x.LicenseExpiryDate < today;
                alerts.Add(new Alert("Alert.TrailerLicense", "Trailer", x.TrailerId,
                    expired ? "رخصة مقطورة منتهية" : "رخصة مقطورة تقترب من الانتهاء",
                    $"رخصة المقطورة {x.TrailerCode} {(expired ? "انتهت" : "تنتهي")} بتاريخ {x.LicenseExpiryDate:yyyy-MM-dd}",
                    expired ? "High" : "Normal", "FLEET.VIEW"));
            }
            if (x.InsuranceExpiryDate is not null && x.InsuranceExpiryDate <= soon)
            {
                var expired = x.InsuranceExpiryDate < today;
                alerts.Add(new Alert("Alert.TrailerInsurance", "Trailer", x.TrailerId,
                    expired ? "تأمين مقطورة منتهٍ" : "تأمين مقطورة يقترب من الانتهاء",
                    $"تأمين المقطورة {x.TrailerCode} {(expired ? "انتهى" : "ينتهي")} بتاريخ {x.InsuranceExpiryDate:yyyy-MM-dd}",
                    expired ? "High" : "Normal", "FLEET.VIEW"));
            }
        }

        /* وثائق منتهية أو قريبة */
        var docs = await db.Documents.AsNoTracking()
            .Where(d => !d.IsDeleted && d.ExpiryDate != null && d.ExpiryDate <= soon)
            .OrderBy(d => d.ExpiryDate).Take(20)
            .Select(d => new { d.DocumentId, d.OriginalFileName, d.FileName, d.ExpiryDate })
            .ToListAsync(ct);
        foreach (var d in docs)
        {
            var expired = d.ExpiryDate < today;
            var name = d.OriginalFileName ?? d.FileName;
            alerts.Add(new Alert("Alert.DocumentExpiry", "Document", d.DocumentId,
                expired ? "وثيقة منتهية" : "وثيقة تقترب من الانتهاء",
                $"الوثيقة «{name}» {(expired ? "انتهت" : "تنتهي")} بتاريخ {d.ExpiryDate:yyyy-MM-dd}",
                expired ? "High" : "Normal", "DOCUMENT.VIEW"));
        }

        /* صيانة مستحقة خلال أسبوع أو متأخرة */
        var dueLimit = today.AddDays(7);
        var maint = await db.VehicleMaintenances.AsNoTracking()
            .Where(m => !m.IsDeleted && m.NextDueDate != null && m.NextDueDate <= dueLimit
                        && m.Status != "Completed" && m.Status != "Cancelled")
            .OrderBy(m => m.NextDueDate).Take(20)
            .Select(m => new { m.MaintenanceId, m.MaintenanceNumber, m.NextDueDate, Plate = m.Vehicle.PlateNumber })
            .ToListAsync(ct);
        foreach (var m in maint)
        {
            var overdueM = m.NextDueDate < today;
            alerts.Add(new Alert("Alert.MaintenanceDue", "Maintenance", m.MaintenanceId,
                overdueM ? "صيانة متأخرة" : "صيانة مستحقة قريبًا",
                $"صيانة السيارة {m.Plate} ({m.MaintenanceNumber}) {(overdueM ? "متأخرة منذ" : "تستحق بتاريخ")} {m.NextDueDate:yyyy-MM-dd}",
                overdueM ? "High" : "Normal", "FLEET.VIEW"));
        }

        if (alerts.Count == 0) return 0;

        /* المستلمون: حملة صلاحية كل فئة — نفس منطق تجميع الصلاحيات
           (أدوار نشطة + منح صريح − منع صريح) ومستخدمون نشطون فقط */
        var perms = alerts.Select(a => a.Permission).Distinct().ToList();
        var recipients = new Dictionary<string, List<int>>();
        foreach (var p in perms)
            recipients[p] = await PermissionHoldersAsync(db, p, ct);

        /* مفاتيح الإشعارات الحديثة أو غير المقروءة لمنع التكرار */
        var since = DateTime.UtcNow.AddDays(-7);
        var existing = await db.Notifications.AsNoTracking()
            .Where(n => n.NotificationType.StartsWith("Alert.")
                        && (n.IsRead == false || n.CreatedAt >= since)
                        && n.UserId != null && n.EntityType != null && n.EntityId != null)
            .Select(n => new { n.UserId, n.NotificationType, n.EntityType, n.EntityId })
            .ToListAsync(ct);
        var seen = new HashSet<(int, string, string, long)>(
            existing.Select(x => (x.UserId!.Value, x.NotificationType, x.EntityType!, x.EntityId!.Value)));

        var created = 0;
        foreach (var a in alerts)
        {
            foreach (var uid in recipients[a.Permission])
            {
                var key = (uid, a.Type, a.EntityType, a.EntityId);
                if (!seen.Add(key)) continue;

                db.Notifications.Add(new Notification
                {
                    UserId           = uid,
                    BranchId         = null,
                    NotificationType = a.Type,
                    Title            = a.Title,
                    Message          = a.Message,
                    EntityType       = a.EntityType,
                    EntityId         = a.EntityId,
                    Priority         = a.Priority,
                    IsRead           = false,
                    CreatedAt        = DateTime.UtcNow
                });
                created++;
            }
        }

        if (created > 0) await db.SaveChangesAsync(ct);
        return created;
    }

    private static async Task<List<int>> PermissionHoldersAsync(
        FastComDbContext db, string permissionCode, CancellationToken ct)
    {
        var ids = await db.Database.SqlQuery<int>($"""
            WITH Perm AS (SELECT PermissionId FROM AppPermissions WHERE PermissionCode = {permissionCode}),
            FromRoles AS (SELECT DISTINCT ur.UserId FROM AspNetUserRoles ur
                          JOIN AppRolePermissions arp ON arp.RoleId = ur.RoleId
                          JOIN AspNetRoles r ON r.Id = ur.RoleId AND r.IsActive = 1
                          WHERE arp.PermissionId IN (SELECT PermissionId FROM Perm)),
            Granted AS (SELECT UserId FROM AppUserPermissions
                        WHERE PermissionId IN (SELECT PermissionId FROM Perm) AND IsGranted = 1),
            Denied  AS (SELECT UserId FROM AppUserPermissions
                        WHERE PermissionId IN (SELECT PermissionId FROM Perm) AND IsGranted = 0)
            SELECT UserId FROM FromRoles
            UNION SELECT UserId FROM Granted
            EXCEPT SELECT UserId FROM Denied
            """).ToListAsync(ct);

        if (ids.Count == 0) return ids;
        var active = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);
        return active;
    }
}
