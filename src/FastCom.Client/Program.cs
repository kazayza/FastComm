using FastCom.Client;
using FastCom.Client.Auth;
using FastCom.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using ApexCharts;

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
// 🔴 سجلنا client باسم "ApexCharts" قبل AddApexCharts() عشان لو الحزمة
//    بتاخد الاسم ده من الـ factory يلاقيه جاهز (شوف BUG #556 تحت).
builder.Services.AddHttpClient("ApexCharts");

builder.Services.AddHttpClient("FastCom", client =>
    {
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
        client.Timeout     = TimeSpan.FromSeconds(60);
    })
    .AddHttpMessageHandler<AuthMessageHandler>();

/* 🔴 الـ HttpClient الافتراضي متسجّل AFTER AddApexCharts() — شوف السبب تحت. */

/* ==========================================================
   2) 🔐 حالة تسجيل الدخول لـ Blazor
   ========================================================== */
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthStateProvider>();
builder.Services.AddAuthorizationCore();

/* ==========================================================
   3) MudBlazor
   ========================================================== */
builder.Services.AddMudServices();
// 📊 ApexCharts — رسوم لوحة المؤشرات
builder.Services.AddApexCharts();

/* ══════════════════════════════════════════════════════════════════════
   🔴 BUG #556 — الـ HttpClient الافتراضي لازم يتسجّل هنا AFTER AddApexCharts()
   ────────────────────────────────────────────────────────────────────────
   Blazor-ApexCharts 4.x جوّا AddApexCharts() بيظلّع HttpClient بـ
   BaseAddress = "_content/Blazor-ApexCharts/" (لتحميل ملفاته الخاصة)،
   ولو سجّلناه قبليه، هو آخر تسجيل في الـ DI فيغلب بتاعنا — والنتيجة
   كانت كل طلبات التطبيق تروح لمسار غلط:
       POST _content/Blazor-ApexCharts/api/auth/login → 401 (الدخول فاشل)
   + التسجيل Transient بدل Scoped = كل خدمة بتجيب نسخة جديدة من الـ
     factory، فحتى لو ApexChartService عدّل BaseAddress على نسخته هو،
     مش هيأثر في نسخة AuthService/CompanyService (اللي المفروض تفضل
     على جذر الموقع "/").
   ══════════════════════════════════════════════════════════════════════ */
builder.Services.AddTransient(sp =>
    sp.GetRequiredService<IHttpClientFactory>()
      .CreateClient("FastCom"));

/* ==========================================================
   4) خدمات FastCom
   ========================================================== */
builder.Services.AddScoped<CompanyService>();
builder.Services.AddScoped<HealthService>();

var host = builder.Build();

await host.RunAsync();