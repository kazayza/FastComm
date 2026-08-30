using System.Net;
using System.Net.Http.Headers;

namespace FastCom.Client.Auth;

/// <summary>
/// 🔐 بيضيف <c>Authorization: Bearer &lt;token&gt;</c> لكل طلب.
/// </summary>
/// <remarks>
/// 🔴 <b>ليه بيحقن <see cref="TokenStore"/> بس ومش <c>AuthService</c>؟</b>
/// <para>
/// <c>AuthService</c> بيحقن <c>HttpClient</c>، والـ <c>HttpClient</c> بيتعمل
/// بالـ handler ده. فلو حقنّا <c>AuthService</c> هنا، الـ DI هيعمل
/// <b>نسخة تانية</b> من <c>AuthService</c> — و<c>OnStateChanged</c> هينادي
/// على نسخة غير اللي الواجهة بتسمع لها.
/// </para>
/// <para>
/// الحل: الـ handler بيمسح التوكن من الـ <see cref="TokenStore"/> على طول،
/// وأول ما <see cref="JwtAuthStateProvider"/> يعمل فحص تاني هيعرف إن
/// المستخدم خرج ويحوّله لصفحة الدخول.
/// </para>
/// </remarks>
public class AuthMessageHandler : DelegatingHandler
{
    private readonly TokenStore _store;

    public AuthMessageHandler(TokenStore store) => _store = store;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _store.GetTokenAsync();

        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        // 🔴 التوكن مرفوض أو منتهي → نمسحه عشان المستخدم ما يفضلش
        //    شايف واجهة وهو أصلاً خارج
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            && !string.IsNullOrEmpty(token))
        {
            // ⚠️ login نفسه بيرجع 401 لما الباسورد غلط —
            //    في الحالة دي مافيش توكن مخزّن أصلًا، فالشرط فوق بيحمينا
            await _store.ClearAsync();
        }

        return response;
    }
}
