using FastCom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Services;

/// <summary>
/// قراءة إعدادات النظام من جدول <c>SystemSettings</c> بكاش قصير.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>ليه كاش 60 ثانية؟</b> الإعدادات بتتقرا كتير (كل عملية / كل عهدة / كل تنبيه)
/// وبتتغيّر نادراً. نفس نمط <see cref="IPermissionService"/>.
/// </para>
/// <para>
/// 🔴 <b>Fail-open بالقيم الافتراضية.</b> لو الجدول فاضي أو فيه مشكلة،
/// النظام يشتغل بالـ Defaults — مش يقع.
/// </para>
/// </remarks>
public interface ISettingsService
{
    /// <summary>كل الإعدادات كقاموس (كاش 60 ثانية).</summary>
    Task<Dictionary<string, string?>> GetAllAsync(CancellationToken ct = default);

    /// <summary>قيمة نصية. <c>null</c> لو المفتاح مش موجود.</summary>
    Task<string?> GetStringAsync(string key, CancellationToken ct = default);

    /// <summary>قيمة نصية — بترجع <paramref name="fallback"/> لو فاضية أو مش موجودة.</summary>
    Task<string> GetStringAsync(string key, string fallback, CancellationToken ct = default);

    /// <summary>قيمة رقمية صحيحة.</summary>
    Task<int> GetIntAsync(string key, int fallback = 0, CancellationToken ct = default);

    /// <summary>قيمة عشرية.</summary>
    Task<decimal> GetDecimalAsync(string key, decimal fallback = 0m, CancellationToken ct = default);

    /// <summary>قيمة منطقية — بيفهم <c>true/1/yes/on</c> (بدون حساسية لحالة الحروف).</summary>
    Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken ct = default);

    /// <summary>قائمة مفصولة بفاصلة — بتشيل الفراغات.</summary>
    Task<List<string>> GetListAsync(string key, CancellationToken ct = default);

    /// <summary>بيفضي الكاش. بيتنادى بعد أي تعديل من <c>/settings</c>.</summary>
    void Invalidate();
}

/// <inheritdoc cref="ISettingsService"/>
public sealed class SettingsService : ISettingsService
{
    /// <summary>اسم مفتاح الكاش — عشان أي实例 تانية في نفس الـ Scope تشوف نفس البيانات.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly FastComDbContext _db;
    private readonly ILogger<SettingsService> _log;

    private Dictionary<string, string?>? _cache;
    private DateTime _cachedAt = DateTime.MinValue;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService(FastComDbContext db, ILogger<SettingsService> log)
    {
        _db  = db;
        _log = log;
    }

    public async Task<Dictionary<string, string?>> GetAllAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var fresh = _cache;
        if (fresh is not null && now - _cachedAt < CacheTtl) return fresh;

        await _gate.WaitAsync(ct);
        try
        {
            // double-check بعد ما دخلنا الـ gate
            if (_cache is not null && DateTime.UtcNow - _cachedAt < CacheTtl) return _cache;

            // 🔴 SystemSettings مالهاش IsDeleted — فمافيش فلتر حذف
            var rows = await _db.SystemSettings
                .AsNoTracking()
                .Select(s => new { s.SettingKey, s.SettingValue })
                .ToListAsync(ct);

            var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
                map[r.SettingKey] = r.SettingValue;   // المفتاح UNIQUE في الداتابيز

            _cache    = map;
            _cachedAt = DateTime.UtcNow;
            return map;
        }
        catch (Exception ex)
        {
            // 🔴 Fail-open: مانوقّعش النظام عشان جدول إعدادات
            _log.LogError(ex, "SettingsService: فشل قراءة SystemSettings — هنستخدم القيم الافتراضية");
            return _cache ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
    }

    public async Task<string> GetStringAsync(string key, string fallback, CancellationToken ct = default)
        => await GetStringAsync(key, ct) ?? fallback;

    public async Task<int> GetIntAsync(string key, int fallback = 0, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, ct);
        if (raw is null) return fallback;
        return int.TryParse(raw, out var v) ? v : fallback;
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback = 0m, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, ct);
        if (raw is null) return fallback;
        // 🔴 InvariantCulture: الداتابيز بتخزّن 50000 مش ٥٠٠٠٠
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, ct);
        if (raw is null) return fallback;
        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true"  or "yes" or "y" or "on"  => true,
            "0" or "false" or "no"  or "n" or "off" => false,
            _ => fallback
        };
    }

    public async Task<List<string>> GetListAsync(string key, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, ct);
        if (raw is null) return new List<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .ToList();
    }

    public void Invalidate()
    {
        _cache    = null;
        _cachedAt = DateTime.MinValue;
    }
}

/// <summary>
/// 🔴 أسماء مفاتيح الإعدادات في مكان واحد — عشان ماحدّش يكتب string بالغلط.
/// <para>لازم تطابق <c>sql/FastCom-settings-seed.sql</c> بالظبط.</para>
/// </summary>
public static class SettingKeys
{
    public const string CustodyMaxAmount       = "CUSTODY.MAX_AMOUNT";
    public const string CustodyAlertDays       = "CUSTODY.ALERT_DAYS";

    public const string InvoiceDefaultTerms    = "INVOICE.DEFAULT_TERMS_CODE";
    public const string InvoiceSendChannels    = "INVOICE.SEND_CHANNELS";

    public const string OperationDelayDays     = "OPERATION.DELAY_DAYS";

    public const string FleetInsuranceAlertDays = "FLEET.INSURANCE_ALERT_DAYS";
    public const string FleetLicenseAlertDays   = "FLEET.LICENSE_ALERT_DAYS";

    public const string PortalTokenDays        = "PORTAL.TOKEN_DEFAULT_DAYS";
    public const string PortalTokenMaxUses     = "PORTAL.TOKEN_DEFAULT_MAXUSES";

    public const string ExpenseMaxAmount       = "EXPENSE.MAX_AMOUNT";
    public const string PaymentMaxAmount       = "PAYMENT.MAX_AMOUNT";

    public const string ShowTaxBreakdown       = "BRANDING.SHOW_TAX_BREAKDOWN";
    public const string EInvoiceEnabled        = "ETA.EINVOICE_ENABLED";
}

/// <summary>
/// القيم الافتراضية — بتُستخدم لو الجدول فاضي.
/// 🔴 لازم تطابق <c>sql/FastCom-settings-seed.sql</c>.
/// </summary>
public static class SettingDefaults
{
    public const decimal CustodyMaxAmount        = 50_000m;
    public const int     CustodyAlertDays        = 7;
    public const int     OperationDelayDays      = 0;
    public const int     FleetInsuranceAlertDays = 30;
    public const int     FleetLicenseAlertDays   = 30;
    public const int     PortalTokenDays         = 7;
    public const int     PortalTokenMaxUses      = 50;
    public const decimal PaymentMaxAmount        = 100_000_000m;
}
