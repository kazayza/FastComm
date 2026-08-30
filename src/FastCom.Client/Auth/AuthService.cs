using System.Net.Http.Json;

namespace FastCom.Client.Auth;

/// <summary>بيانات المستخدم المسجّل دخوله.</summary>
public class AuthUser
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public int? BranchId { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();

    /// <summary>الاسم اللي بيتعرض في الـ Header.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(FullName) ? UserName : FullName;

    /// <summary>عنده الصلاحية دي؟</summary>
    public bool Can(string permission) =>
        IsAdmin || Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    /// <summary>مدير النظام؟</summary>
    public bool IsAdmin => Roles.Contains("ADMIN", StringComparer.OrdinalIgnoreCase);
}

/// <summary>طلب تسجيل الدخول.</summary>
public class LoginRequest
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
}

/// <summary>استجابة <c>POST /api/auth/login</c>.</summary>
public class LoginResponse
{
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string UserName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public int? BranchId { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
}

/// <summary>استجابة الفشل من الـ API.</summary>
public class AuthError
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? ErrorType { get; set; }
}

/// <summary>
/// 🔐 تسجيل الدخول / الخروج / بيانات المستخدم.
/// </summary>
/// <remarks>
/// 🔴 <b>مهم:</b> الخدمة دي <c>Scoped</c>، وفي Blazor WASM الـ Scoped =
/// عمر التطبيق كله. يعني <see cref="OnStateChanged"/> بتشتغل لكل الصفحات.
/// </remarks>
public class AuthService
{
    private readonly HttpClient _http;
    private readonly TokenStore _store;

    /// <summary>بيتنادى وقت الدخول أو الخروج — الـ AuthStateProvider بيسمع له.</summary>
    public event Action? OnStateChanged;

    public AuthService(HttpClient http, TokenStore store)
    {
        _http  = http;
        _store = store;
    }

    /// <summary>فيه توكن مخزّن؟</summary>
    public async Task<bool> HasTokenAsync() =>
        !string.IsNullOrEmpty(await _store.GetTokenAsync());

    /// <summary>
    /// 🔐 تسجيل الدخول.
    /// </summary>
    /// <returns><c>(نجح؟، رسالة الخطأ)</c></returns>
    public async Task<(bool Success, string Message)> LoginAsync(
        string userName, string password, bool rememberMe)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest
            {
                UserName   = userName,
                Password   = password,
                RememberMe = rememberMe
            });

            if (!res.IsSuccessStatusCode)
            {
                AuthError? err = null;
                try { err = await res.Content.ReadFromJsonAsync<AuthError>(); }
                catch { /* الاستجابة مش JSON */ }

                return (false, err?.Message ?? $"فشل تسجيل الدخول (HTTP {(int)res.StatusCode})");
            }

            var data = await res.Content.ReadFromJsonAsync<LoginResponse>();

            if (data is null || string.IsNullOrEmpty(data.Token))
                return (false, "استجابة غير متوقعة من السيرفر");

            var user = new AuthUser
            {
                UserName    = data.UserName,
                FullName    = data.FullName,
                Email       = data.Email,
                BranchId    = data.BranchId,
                Roles       = data.Roles,
                Permissions = data.Permissions
            };

            await _store.SaveAsync(data, user);

            // نكمّل الـ UserId والصلاحيات من /me
            await RefreshMeAsync();

            OnStateChanged?.Invoke();
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, "مش قادر أوصل للسيرفر: " + ex.Message);
        }
    }

    /// <summary>بيجيب بيانات المستخدم من <c>/api/auth/me</c>.</summary>
    public async Task<AuthUser?> RefreshMeAsync()
    {
        try
        {
            var me = await _http.GetFromJsonAsync<AuthUser>("api/auth/me");
            if (me is null) return null;

            await _store.SaveUserAsync(me);
            return me;
        }
        catch
        {
            return null;   // مانكسرش
        }
    }

    /// <summary>بيانات المستخدم المخزّنة.</summary>
    public Task<AuthUser?> GetUserAsync() => _store.GetUserAsync();

    /// <summary>التوكن المخزّن.</summary>
    public Task<string?> GetTokenAsync() => _store.GetTokenAsync();

    /// <summary>التوكن لسه صالح بالوقت؟</summary>
    public Task<bool> IsTokenValidAsync() => _store.IsNotExpiredAsync();

    /// <summary>تسجيل الخروج.</summary>
    public async Task LogoutAsync()
    {
        try { await _http.PostAsync("api/auth/logout", null); }
        catch { /* مش مهم — الأهم إننا نمسح من الـ Browser */ }

        await _store.ClearAsync();
        OnStateChanged?.Invoke();
    }

    /// <summary>بيمسح من الـ Browser بس (لو التوكن بايظ).</summary>
    public async Task ClearLocalAsync()
    {
        await _store.ClearAsync();
        OnStateChanged?.Invoke();
    }
}
