using System.Net.Http.Json;
using FastCom.Client.Models;

namespace FastCom.Client.Services;

/// <summary>
/// بيجيب بيانات الشركة من <c>/api/branding/company</c> ويعملها Cache.
/// <para>
/// 🔴 D11: المصدر هو جدول <c>CompanyProfile</c>.
/// </para>
/// </summary>
public class CompanyService
{
    private readonly HttpClient _http;
    private CompanyInfo? _cached;

    public CompanyService(HttpClient http) => _http = http;

    public async Task<CompanyInfo> GetAsync()
    {
        if (_cached is not null) return _cached;

        CompanyInfo? info = null;

        try
        {
            info = await _http.GetFromJsonAsync<CompanyInfo>("api/branding/company");
        }
        catch (Exception ex)
        {
            // ⚠️ مانكسرش الواجهة — بس مانخزّنش الفشل
            info = new CompanyInfo
            {
                LegalNameAr = "فاست كوم",
                Error       = ex.Message,
                ErrorType   = ex.GetType().Name
            };
        }

        info ??= new CompanyInfo { LegalNameAr = "فاست كوم", Error = "الـ API رجّع null" };

        // 🔴 مهم: مانخزّنش (Cache) إلا لو البيانات سليمة.
        //    لو خزّنّا الفشل، الـ Header هيفضل بايظ لحد ما تعمل Refresh.
        if (!info.HasError && !string.IsNullOrWhiteSpace(info.LegalNameAr))
            _cached = info;

        return info;
    }
}

/// <summary>بيجيب حالة النظام من <c>/api/health</c>.</summary>
public class HealthService
{
    private readonly HttpClient _http;
    public HealthService(HttpClient http) => _http = http;

    public async Task<HealthInfo?> GetAsync()
    {
        try { return await _http.GetFromJsonAsync<HealthInfo>("api/health"); }
        catch { return null; }
    }
}
