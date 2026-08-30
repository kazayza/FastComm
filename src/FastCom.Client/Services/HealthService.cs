using System.Net.Http.Json;
using FastCom.Client.Models;

namespace FastCom.Client.Services;

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