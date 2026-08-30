using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FastCom.Client.Auth;

/// <summary>
/// 🔐 بيوصّل حالة تسجيل الدخول لـ Blazor.
/// </summary>
/// <remarks>
/// بيشتغل كده:
/// <list type="number">
/// <item>وقت ما التطبيق يبدأ، بيقرا الـ Token من <c>localStorage</c></item>
/// <item>لو فيه توكن صالح، بيعمل <c>ClaimsPrincipal</c> من البيانات المخزّنة</item>
/// <item>بيسمع لـ <see cref="AuthService.OnStateChanged"/> فأي دخول/خروج بيحدّث كل الصفحات</item>
/// </list>
/// </remarks>
public class JwtAuthStateProvider : AuthenticationStateProvider
{
    private readonly AuthService _auth;
    private static readonly Task<AuthenticationState> Anonymous =
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

    public JwtAuthStateProvider(AuthService auth)
    {
        _auth = auth;
        _auth.OnStateChanged += () => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var token = await _auth.GetTokenAsync();

            if (string.IsNullOrEmpty(token))
                return await Anonymous;

            // التوكن خلص وقته؟
            if (!await _auth.IsTokenValidAsync())
            {
                await _auth.ClearLocalAsync();
                return await Anonymous;
            }

            var user = await _auth.GetUserAsync();
            if (user is null) return await Anonymous;

            var identity = BuildIdentity(user);
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            // أي خطأ = اعتبره مش مسجّل دخول
            return await Anonymous;
        }
    }

    /// <summary>بيعمل الـ Claims من بيانات المستخدم.</summary>
    private static ClaimsIdentity BuildIdentity(AuthUser user)
    {
        var identity = new ClaimsIdentity(
            authenticationType: "jwt",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName));
        identity.AddClaim(new Claim("fullName", user.FullName));

        if (!string.IsNullOrWhiteSpace(user.Email))
            identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));

        if (user.BranchId is not null)
            identity.AddClaim(new Claim("branchId", user.BranchId.Value.ToString()));

        foreach (var role in user.Roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

        // 🔴 الصلاحيات كـ Claims برضه — عشان <AuthorizeView Roles="..."> يشتغل
        foreach (var perm in user.Permissions)
            identity.AddClaim(new Claim("perm", perm));

        return identity;
    }
}
