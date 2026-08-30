using FastCom.Client;
using FastCom.Client.Auth;
using FastCom.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

/* ==========================================================
   1) 🔐 Auth — Step 3.3
   ⚠️ الترتيب مهم:
      TokenStore ← AuthService ← AuthMessageHandler ← HttpClient
   ========================================================== */
builder.Services.AddScoped<TokenStore>();
builder.Services.AddScoped<AuthService>();

// 🔐 HttpClient بيضيف الـ Bearer Token تلقائيًا لكل طلب
builder.Services.AddTransient<AuthMessageHandler>();

// ── HTTP ──────────────────────────────────────────────────────────────────────
// ⚠️ الـ BaseAddress فاضي = relative URLs.
//    ده مهم لأن الـ Client والـ API على نفس الـ IIS site:
//      /            -> Blazor WASM
//      /api/...     -> Web API
//    فـ fetch("api/health") بيروح للـ API صح من غير CORS.
builder.Services.AddHttpClient("FastCom", client =>
    {
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
        client.Timeout     = TimeSpan.FromSeconds(60);
    })
    .AddHttpMessageHandler<AuthMessageHandler>();

// الـ HttpClient الافتراضي = المزوّد بالـ Auth handler
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>()
      .CreateClient("FastCom"));

/* ==========================================================
   2) 🔐 حالة تسجيل الدخول لـ Blazor
   ========================================================== */
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthStateProvider>();
builder.Services.AddAuthorizationCore();

/* ==========================================================
   3) MudBlazor
   ========================================================== */
builder.Services.AddMudServices();

/* ==========================================================
   4) خدمات FastCom
   ========================================================== */
builder.Services.AddScoped<CompanyService>();
builder.Services.AddScoped<HealthService>();

await builder.Build().RunAsync();
