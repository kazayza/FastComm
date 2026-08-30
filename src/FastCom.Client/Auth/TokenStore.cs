using Microsoft.JSInterop;

namespace FastCom.Client.Auth;

/// <summary>
/// 🔐 تخزين الـ Token وبيانات المستخدم في <c>localStorage</c>.
/// </summary>
/// <remarks>
/// ⚠️ <b>ملاحظة أمان:</b> في Blazor WASM أي حاجة في الـ Browser ممكن تتقرا
/// من JavaScript. عشان كده <b>كل الحماية الحقيقية على الـ Server</b> —
/// الـ Token هنا مجرد إثبات هوية، والـ Server هو اللي بيتحقق منه ويقرر.
/// </remarks>
public class TokenStore
{
    private readonly IJSRuntime _js;

    public TokenStore(IJSRuntime js) => _js = js;

    private const string KToken  = "fastcom.auth.token";
    private const string KUser   = "fastcom.auth.user";
    private const string KExpire = "fastcom.auth.expires";

    /// <summary>بيخزّن كل حاجة بعد تسجيل دخول ناجح.</summary>
    public async Task SaveAsync(LoginResponse data, AuthUser user)
    {
        await _js.InvokeVoidAsync("fastcomStore.set", KToken,  data.Token);
        await _js.InvokeVoidAsync("fastcomStore.set", KExpire, data.ExpiresAt.ToString("o"));
        await _js.InvokeVoidAsync("fastcomStore.set", KUser,
            System.Text.Json.JsonSerializer.Serialize(user));
    }

    /// <summary>بيحدّث بيانات المستخدم بس (من <c>/api/auth/me</c>).</summary>
    public Task SaveUserAsync(AuthUser user) =>
        _js.InvokeVoidAsync("fastcomStore.set", KUser,
            System.Text.Json.JsonSerializer.Serialize(user)).AsTask();

    /// <summary>بيمسح كل حاجة (تسجيل خروج).</summary>
    public async Task ClearAsync()
    {
        await _js.InvokeVoidAsync("fastcomStore.remove", KToken);
        await _js.InvokeVoidAsync("fastcomStore.remove", KUser);
        await _js.InvokeVoidAsync("fastcomStore.remove", KExpire);
    }

    /// <summary>التوكن المخزّن — أو <c>null</c>.</summary>
    public Task<string?> GetTokenAsync() =>
        _js.InvokeAsync<string?>("fastcomStore.get", KToken).AsTask();

    /// <summary>بيانات المستخدم المخزّنة — أو <c>null</c>.</summary>
    public async Task<AuthUser?> GetUserAsync()
    {
        var json = await _js.InvokeAsync<string?>("fastcomStore.get", KUser);
        if (string.IsNullOrEmpty(json)) return null;

        try { return System.Text.Json.JsonSerializer.Deserialize<AuthUser>(json); }
        catch { return null; }
    }

    /// <summary>التوكن لسه صالح بالوقت؟</summary>
    public async Task<bool> IsNotExpiredAsync()
    {
        var raw = await _js.InvokeAsync<string?>("fastcomStore.get", KExpire);
        if (string.IsNullOrEmpty(raw)) return true;

        return DateTime.TryParse(raw, null,
                   System.Globalization.DateTimeStyles.RoundtripKind, out var exp)
               && exp > DateTime.UtcNow;
    }
}
