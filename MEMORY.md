# 🧠 FastCom — ملف الذاكرة

> **آخر تحديث:** 2026-08-30
> **ده ملف الذاكرة الرئيسي. اقرأه الأول قبل أي حاجة.**
> **المسار:** `FastCom/MEMORY.md`

---

## 0) 🚦 إحنا فين دلوقتي بالظبط

| Step | الاسم | الحالة |
|---|---|---|
| 0 | مراجعة + اختيار التقنية | ✅ |
| 1 | قاعدة البيانات (db65922) | ✅ **مؤكد من المستخدم** |
| 2 | Solution skeleton + Scaffold + Merge + Build | ✅ **مؤكد من المستخدم** |
| **3** | **الواجهة + Shell + Login** | 🔵 **شغالين فيه** |
| 4 | مستخدمين / أدوار / صلاحيات | ⏳ |
| 5 | البيانات الأساسية | ⏳ |
| 6 | الحجوزات | ⏳ |
| 7 | العمليات + الرحلات + الحاويات | ⏳ |
| 8 | العهد + المصروفات | ⏳ |
| 9 | الفواتير + التحصيل | ⏳ |
| 10 | الخزينة + كشف حساب العميل | ⏳ |
| 11 | المستندات + الإشعارات | ⏳ |
| 12 | التقارير + الـ Dashboards | ⏳ |

### تفاصيل Step 3

| | الشغل | الحالة |
|---|---|---|
| 3.1 | Client project + MudBlazor + RTL | ✅ |
| 3.2 | Design System + Loading screen | ✅ |
| 3.2b | البحث العام في الشريط العلوي | ✅ كود مكتوب |
| **3.3** | **Login + JWT** | ✅ شغال + شاشة الدخول اتعادت تصميمها — دخول مُختبر JWT ✅ |
| 3.3b | الهوية البصرية الرسمية (أزرق `#0B56DF` + برتقالي `#FF8015`) | ✅ |
| 3.4 | ربط القائمة بالصلاحيات | ✅ كود مكتوب — Build 0 errors (فلترة كاملة بـ `Can()`) |
| 3.5 | حماية الـ Controllers | ✅ مُختبر — مصفوفة أمنية 10/10 (تفاصيل تحت) |

---

## 1) ⏸️ آخر حاجة — مستنيين من المستخدم

### ✅ 1) مشكلة الدخول — **اتحلّت نهائيًا (2026-08-30 23:56)**
- **السبب (من اللوج):** باسورد الـ admin كان غلط (4 محاولات فاشلة: 22:19→23:56) — **مش** قفل ولا مشكلة DB.
- **الحل:** endpoint تطوير آمن `POST /api/auth/reset-password` (**Development فقط**) بيعيّن باسورد جديد + يفتح الحساب + يصفّر العدّاد. اتستخدم → `AccessFailedCount=0 · LockoutEnd=NULL` (متأكد من الـ DB مباشرة).
- **دخول مُختبر ✅** — JWT اتولد (8 ساعات · ADMIN · 107 صلاحية). الباسورد الجديد: **المستخدم عارفه** (اتبعتله في الشات — ماتكتبش قيمته هنا، الملف متتبّع في git).
- شاشة الدخول اتعادت تصميمها بالكامل وفيها زرار "نسيت كلمة المرور" بيفتح أدوات المطور (Development بس).
- السيرفر شغال: `https://localhost:7001` (بروفايل `FastCom.Server`).
- 🔴 **فخ البورت:** شغّل `dotnet run --project src\FastCom.Server` **من غير** `--launch-profile` (أي اسم غلط = يقع صامت على بورت 5000).

### ⚠️ 2) التنظيف — خلص تقريبًا
النسخ المكررة اتشالت، الأسرار في user-secrets، والريبو اتعمل:
**Commit `df05644` على فرع `main` (124 ملف)** — نضيف تمامًا (صفر أسرار).
باقي `docs/brand/` فيه 3 PNG مقفولة بعملية خارجية — **gitignored ومش هتأثر على أي حاجة**؛ امسحها يدويًا بعد إعادة تشغيل الجهاز.
### 🔴 مؤرشف — اتحلّ (شوف أعلى القسم ده)
~~TCP 1433 بيفشل timeout~~ → **اتصلت لوحدها 22:18** وكل الاختبارات نجحت بعدها (health + login pipeline + SPA).

### 🟡 2) تنظيف بسيط — بعد إعادة تشغيل VS Code
3 ملفات PNG قديمة في `docs/brand/` مقفولة بجلسة terminal قديمة (مستثناة من git أصلًا عبر `.gitignore`).
بعد Restart: `Remove-Item docs\brand -Recurse -Force`

### ⚪ 3) اللوجو — متوقف بقرار المستخدم (مؤجل)
الاستخراج معقد: CMYK/YCCK معكوس + SMask بـ TIFF Predictor 2 + الباتش مستطيل مش لوجو شفاف.
السكربت محفوظ في `tools/extract-logo.ps1` لو حبنا نكمل. الشغّال حاليًا: ألوان الهوية + Favicon SVG "FC" بخلفية `#0B56DF`.

### ✅ Step 3.5 اتعمل (2026-08-31 فجرًا) — حماية الـ API كلها
- **ملف جديد `src/FastCom.Server/Auth/PermissionAuthorization.cs`:**
  - `PermissionRequirement` + `PermissionAuthorizationHandler` (**fail-closed**: لو مفيهوش perm claim = رفض) + `PermissionPolicyProvider` (بينشئ policies ديناميكيًا من `[Authorize(Permission = "X.Y")]` من غير تسجيل مسبق).
  - الـ handler بيقرا claim `"perm"` اللي بيبنيها `TokenService` وقت الـ login (من `GetPermissionsAsync`).
- **الـ Controllers:** `SearchController` + `DiagController` كانوا **مفتوحين تمامًا** — اتقفلوا `[Authorize]`. `AuthController` المستوى: `[Authorize]` على الكلاس + `[AllowAnonymous]` لـ `login`/`seed-admin`/`reset-password` (الاثنين الأخيرين بيحرسهم فحص Development جوه الكود).
- **مفتوحين بقصد** (محتاجينهم قبل الدخول): `HealthController` + `BrandingController`.
- **Program.cs:** تسجيل الـ handler والـ policy provider في الـ DI (`AddSingleton`).
- **مصفوفة الاختبار الأمنية (10/10 ✅):** بدون توكن → `health/branding` 200 (مقصود) + `me/logout/search/diag` **401**. بتوكن admin → كلها **200** + `/me` رجّعت الـ 107 صلاحية. واختبارات بوابة الـ Development على `seed-admin`/`reset-password` نجحت برسائل أمان واضحة وصفر side-effects.
- 🔧 **درس معماري:** الصلاحيات **مش في الـ JWT** — الـ claims بتتقرا من `/api/auth/me` في الـ Client، لكن الـ **API بيتحقق من claim `perm` في الـ token نفسه** (اتبنى وقت الـ login) — فالحماية server-side حقيقية مش معتمدة على الواجهة.

### 🚀 اللي بعده
**Step 4: شاشة المستخدمين والأدوار** (CRUD + ربط صلاحيات) — أول شاشة بناء على نظام الصلاحيات الجاهز.

### ✅ Step 3.4 اتعمل (2026-08-30 مساءً)
- **`NavMenu.razor`** اتكتب من جديد: كل عنصر قائمة مرتبط بكود صلاحية من `AppPermissions` (107 كود) — بيظهر بس لو `User.Can("X.Y")` صحيح، والمجموعات (`MudNavGroup`) بتظهر لو فيها أي عنصر متاح. ADMIN يشوف كل حاجة (bypass جاهز في `AuthUser`).
- **`Pages/Health.razor`** كان **Layout ميت من غير route** — اتشال وعُوّض بصفحة حقيقية `@page "/health"` + `[Authorize]`: كروت حالة (DB/جداول/صلاحيات/أدوار) + قسم أخطاء تفصيلي + زر تحديث. لينك "حالة النظام" في القائمة كان ميت من الأول — دلوقتي شغال ويظهر لـ ADMIN بس.
- **`HealthService`** اتنقل لملفه `Services/HealthService.cs` (كان عايش جوه `CompanyService.cs`).
- 🔧 **دروس:** `@inject HealthService Health` جوه `Health.razor` = خطأ CS0542 (اسم العضو = اسم الكلاس) → سمّيه `HealthApi`.

### 🎨 شاشة الدخول — إعادة تصميم كاملة (2026-08-30 بالليل)
- `Login.razor` جديد: براند جانبي بتدرج الهوية + كارت زجاجي للفورم + زرار "نسيت كلمة المرور" (يفتح أدوات مطور Development: seed admin + reset password).
- `app.css`: قسم الـ login اتكتب من جديد بكلاسات جديدة (`fc-login-box` · `fc-login-side` · `fc-brand-glow` · `fc-devtools`...).
- `AuthController`: endpoint `reset-password` (Development) + `ResetPasswordRequest` في `AuthDtos.cs`.
- 🔧 **درس:** عدّاد الـ `div` — 3 مفتوحة و2 مقفولة = الصفحة مش بتتركب (اتصلح واتأكد بالبناء).

### ❌ لو الدخول فشل تاني
→ شوف قسم 3 (سجل الأخطاء) + ابعت رسالة الخطأ بالظبط

---

## 1b) 📁 ملفات Step 3.3 (15 ملف)

### 🆕 جديدة (11)

```
src/FastCom.Server/Auth/TokenService.cs
src/FastCom.Server/Controllers/Auth/AuthController.cs
src/FastCom.Server/Controllers/Auth/AuthDtos.cs
src/FastCom.Client/Auth/TokenStore.cs
src/FastCom.Client/Auth/AuthService.cs
src/FastCom.Client/Auth/JwtAuthStateProvider.cs
src/FastCom.Client/Auth/AuthMessageHandler.cs
src/FastCom.Client/Pages/Login.razor
src/FastCom.Client/Shared/Layout/BlankLayout.razor
src/FastCom.Client/Shared/Components/RedirectToLogin.razor
src/FastCom.Client/wwwroot/js/fastcom-storage.js
```

### ✏️ معدّلة (4)

```
src/FastCom.Server/Program.cs              ← + AddScoped<TokenService>()
src/FastCom.Client/Program.cs              ← إعادة كتابة (HttpClient factory + Auth)
src/FastCom.Client/App.razor               ← CascadingAuthenticationState + AuthorizeRouteView
src/FastCom.Client/_Imports.razor          ← + Authorization namespace
src/FastCom.Client/Shared/Layout/MainLayout.razor  ← اسم المستخدم + خروج
src/FastCom.Client/wwwroot/index.html      ← + fastcom-storage.js
src/FastCom.Client/wwwroot/css/app.css     ← + CSS الدخول
```

---

## 1c) 🔑 الـ JWT Key

```powershell
-join ((65..90) + (97..122) + (48..57) | Get-Random -Count 80 | ForEach-Object {[char]$_})
```

حط الناتج في `appsettings.json`:
```json
"Jwt": { "Key": "<الناتج هنا>" }
```

> 🔴 لو الـ Key أقل من 64 حرف، `TokenService` بيرمي
> `InvalidOperationException` — ده مقصود.

---

## 2) 📏 قواعد الشغل (من المستخدم — إلزامية)

| # | القاعدة |
|---|---|
| 1 | **🔴 مافيش زيبات.** اعرض الملفات من الـ workspace بـ `present_file` وانسخ |
| 2 | **قلّل الأدوات المخصّصة.** SSMS أحسن من سكربتات PowerShell |
| 3 | **كل hand-off = حاجة واحدة تعملها + قوللي رجعت بإيه** |
| 4 | **ماتكتبش كود قبل ما تشوف الملف الحقيقي** (غلطة اتكررت 3 مرات) |
| 5 | **ماتخترعش أسماء APIs** — الأيقونات وخصائص MudBlazor لازم تتأكد منها |
| 6 | **رد بالعربي المصري** |
| 7 | **ماتغيّرش Business Logic من غير شرح** |
| 8 | **لو نقطة بتأثر على الـ Database أو الـ Workflow → وقف واسأل** |
| 9 | **.NET 8** — اختيار المستخدم، ماتتجاوزهوش |
| 10 | **VSCode** مش Visual Studio |
| 11 | **مافيش Splash Screen** — Loading screen للـ WASM بس |
| 12 | **بيانات الشركة من `CompanyProfile`** — مش من `SystemSettings` |
| 13 | **Server-side authorization إلزامي** — الـ UI مش نقطة الحماية الوحيدة |
| 14 | **المستخدم النهائي عمره ما يشوف مصطلحات Database/Accounting/Logistics** |

---

## 3) 🔥 سجل الأخطاء (اقرأه قبل ما تكتب أي كود)

### أخطاء حصلت فعلًا واتحلّت

| # | الخطأ | السبب | الحل |
|---|---|---|---|
| 1 | `NU1603` Blazor-ApexCharts | **3.6.1 مش موجود** (أحدث 4.0.0) | اتشال — هيتضاف في Step 12 |
| 2 | `CS0117 LayoutProperties.AppBarHeight` | **الاسم غلط** | اتشال — الارتفاع من CSS |
| 3 | `CS0117 Icons...LocalTransport` | **الأيقونة مش موجودة** | → `DirectionsCar` |
| 4 | `CS0117 Icons...MonitorHeart` | **الأيقونة مش موجودة** | → `Info` |
| 5 | `CS1061 UseBlazorFrameworkFiles` | ناقص package في الـ Server | + `Microsoft.AspNetCore.Components.WebAssembly.Server 8.0.11` |
| 6 | `CS1061 DbParameter.SqlDbType` | `DbParameter` abstract | → `new SqlParameter(...)` + `using Microsoft.Data.SqlClient;` |
| 7 | `RZ10010 ValueChanged` مرتين | `@bind-Value` بيولّد `ValueChanged` | → `Value="_selected"` + `ValueChanged="..."` |
| 8 | `CS8826` partial method | **اسم البارامتر مختلف** (`builder` vs `modelBuilder`) | وحّدنا الاسم على `modelBuilder` |
| 9 | أصفار في `/health` | `SqlQueryRaw<T>` بيرمي + الـ catch بيبلع | → ADO.NET + إرجاع الخطأ |
| 10 | `RoleManager` مش محقون | كتبته كـ field بـ `null!` | → حقنه في الـ Constructor |
| 11 | تخزين Token معقّد | عملت `static BrowserStore` bridge | → `IJSRuntime` + `fastcom-storage.js` |
| 12 | `Icons...Visibility` مش متأكد | اخترعته | → `Lock` / `LockOpen` |
| 13 | 🔴 `CS0234 Components.Authorization` (17 خطأ) | **قولت إنها transitively — غلط** | + `Microsoft.AspNetCore.Components.Authorization 8.0.11` |
| 14 | DB `Error 10060` TCP timeout من جهاز المستخدم | شبكة/استضافة — **مش مشكلة كود** | الكود سليم (503 + رسالة واضحة للمستخدم) — مستنيين فحص panel monsterasp |
| 15 | صورة اللوجو من PDF = أبيض/أسود | CMYK/YCCK معكوس + SMask بـ TIFF Predictor 2 | اتظبط التحليل وألوان اللوجو اتحسلت (`#0B56DF`/`#FF8015`) — والصورة نفسها اتلغت (X10) |

### 🚫 قواعد مستفادة

- **`@bind-X` = `X` + `XChanged`.** ماتكتبش `XChanged` مع `@bind-X`.
- **`Icons.Material.Filled.*`** — ماتخترعش. استخدم القائمة المضمونة (تحت).
- **`SqlQueryRaw<T>`** — اشتغل بـ ADO.NET، أضمن وأوضح.
- **ماتبلعش الاستثناءات.** ارجّع الرسالة في الـ JSON.
- **PowerShell regex ≠ Python regex.** `\s` و multiline مختلفين.
- **ماتختبرش على fixture متخيّل.** شوف الملف الحقيقي الأول.
- **ماتحقنش dependencies كـ fields بـ `null!`** — حقنها في الـ Constructor.
- **localStorage في WASM = JS Interop.** مافيش طريقة static.
- **🔴 `Microsoft.AspNetCore.Components.Authorization` package منفصل.**
  **مش** transitively مع `Components.WebAssembly`.
  بدونه → `CS0234: 'Authorization' does not exist in 'Microsoft.AspNetCore.Components'`
  + `CS0246: AuthenticationStateProvider` + `AuthenticationState`.
  **الحل:** `<PackageReference Include="Microsoft.AspNetCore.Components.Authorization" Version="8.0.11" />`

---

## 4) ✅ الأيقونات المضمونة (MudBlazor 7.15)

```
AccountBalance      AccountBalanceWallet  Add
AirportShuttle      Assessment            Badge
BookOnline          CheckCircle           CloudDone
CloudOff            Construction          Dashboard
DirectionsCar       Groups                HourglassTop
Info                List                  LocalOffer
LocalShipping       Menu                  Payments
PersonOutline       RadioButtonUnchecked  ReceiptLong
Refresh             Route                 Savings
Search              SearchOff             Settings
```

> ❌ **مش موجودة:** `LocalTransport` · `MonitorHeart`

---

## 5) 📁 الملفات — الحالة الحالية

### Server — `src/FastCom.Server/`

| الملف | الحالة |
|---|---|
| `Program.cs` | ✅ Blazor مفعّل (`UseBlazorFrameworkFiles` + `MapFallbackToFile` + `UseWebAssemblyDebugging`) |
| `FastCom.Server.csproj` | ✅ فيه `WebAssembly.Server 8.0.11` + ref للـ Client |
| `Controllers/HealthController.cs` | ✅ **ADO.NET** — 7 عدّادات، كل واحدة لوحدها |
| `Controllers/BrandingController.cs` | ✅ **ADO.NET** — يرجّع `error`/`errorType`/`errorInner` |
| `Controllers/SearchController.cs` | ✅ **ADO.NET + SqlParameter** — 7 وحدات |
| `appsettings.json` | ✅ placeholders بس — **الأسرار الحقيقية في user-secrets** (`ConnectionStrings:DefaultConnection` + `Jwt:Key`) |

### Client — `src/FastCom.Client/`

| الملف | الحالة |
|---|---|
| `FastCom.Client.csproj` | ✅ WebAssembly 8.0.11 + MudBlazor 7.15.0 (مافيش ApexCharts) |
| `Program.cs` | ✅ HttpClient + MudServices + `CompanyService` + `HealthService` |
| `FastComTheme.cs` | ✅ (مافيش `AppBarHeight`) |
| `Shared/Layout/MainLayout.razor` | ✅ + `<GlobalSearch />` |
| `Shared/Layout/NavMenu.razor` | ✅ |
| `Shared/Components/GlobalSearch.razor` | ✅ `Value` + `ValueChanged` |
| `Pages/Home.razor` | ✅ |
| `Pages/Health.razor` | ✅ + جدول تشخيص |
| `Pages/Search.razor` | ✅ "الوحدة لسه ما اتبنتش" |
| `Models/HealthInfo.cs` | ✅ + 6 حقول تشخيص |
| `Models/CompanyInfo.cs` | ✅ + `Error`/`HasError` |
| `Models/SearchResult.cs` | ✅ + `ToString()` للـ Autocomplete |
| `Services/CompanyService.cs` | ✅ **مانخزّنش الفشل في الـ Cache** + `HealthService` |
| `wwwroot/index.html` | ✅ Loading screen |
| `wwwroot/css/app.css` | ✅ + CSS البحث |

### ⚠️ فرق مهم في المسارات

| هنا في الـ workspace | عند المستخدم |
|---|---|
| `src/FastCom.Client/Shared/Components/` | `src\FastCom.Client\Shared\componant\` ← **غلطة إملائية من المستخدم** |

> مش بتأثر على الـ build. بس **خليك فاكر** لما تديله مسار.

### Infrastructure — `src/FastCom.Infrastructure/`

| الملف | الحالة |
|---|---|
| `Persistence/FastComDbContext.cs` | ✅ `partial` · البارامتر `modelBuilder` |
| `Persistence/ScaffoldedDbContext.cs` | ✅ **993 سطر · 61 DbSet · 61 Entity config** |
| `Persistence/SoftDeleteQueryFilter.cs` | ✅ |
| `DesignTimeDbContextFactory.cs` | ✅ |
| `Identity/ApplicationUser.cs` + `ApplicationRole.cs` | ✅ |

---

## 6) 🎨 الـ Design System

| | |
|---|---|
| Primary | `#0F2A47` كحلي (`NavyDark #0A1D32` · `NavyLight #1B3E63`) |
| Secondary | `#F0A500` ذهبي (`#C98600`) |
| Success / Warning / Error / Info | `#1B9E5E` · `#E8A317` · `#D64545` · `#2E86AB` |
| Surface / Text / Muted / Line | `#F5F7FA` · `#1A2332` · `#6B7A8F` · `#E3E9F0` |
| الخط | **Cairo** (Google Fonts) 300–800 |
| RTL | `dir="rtl"` + `MudRTLProvider RightToLeft="true"` |
| Radius | `--fc-radius: 12px` |

### ✅ المستخدم قال: **"الألوان عاجبانى فعلا مفيش خلاف على كده"**

### اقتراحات مقدمة (مستنيين اختياره)

1. **Dark Mode** + زرار تبديل
2. **ألوان الحالات** على الـ Chips
3. **كثافة مضغوطة** للجداول
4. **Breadcrumb**
5. **شريط جانبي يتقلّص**

---

## 7) 🔍 البحث العام — التفاصيل

### `GET /api/search?q=كلمة&take=25`

| الوحدة | بيدور على |
|---|---|
| عميل | `NameAr` · `CustomerCode` · `NameEn` · `Phone` · `TaxNumber` |
| حجز | `BookingNumber` · `CustomerReference` · `Customers.NameAr` |
| عملية | `OperationNumber` · `Customers.NameAr` |
| فاتورة | `InvoiceNumber` · `Customers.NameAr` |
| رحلة | `TripNumber` · `Vehicles.PlateNumber` |
| حاوية | `ContainerNumber` · `OwnerName` |
| سيارة | `PlateNumber` · `VehicleCode` · `Brand` |

- **الاتصال:** ADO.NET · **الأمان:** `SqlParameter` (مافيش concatenation)
- **الحد الأدنى:** حرفين · **الحد الأقصى:** 25 نتيجة
- ⏳ **Step 3.5:** `[AllowAnonymous]` → `[Authorize]` + فلترة بالصلاحيات
- ⏳ **Steps 5–9:** صفحات العرض (`/customers/{id}` إلخ) لسه ما اتبنتش

---

## 8) 🗄️ قاعدة البيانات — مؤكد

```
Server   = db65922.public.databaseasp.net
Database = db65922
```

| العنصر | العدد |
|---|---|
| جداول | **68** |
| Views | **7** |
| Triggers | **8** |
| Stored Procedures | **1** (`usp_GetNextNumber`) |
| صلاحيات (`AppPermissions`) | **107** |
| أدوار (`AspNetRoles`) | **11** |
| صفوف `CompanyProfile` | **1** |

### 🔧 أدوات قاعدة البيانات

| | |
|---|---|
| **تنفيذ SQL** | **SSMS بس** — Panel الاستضافة بيبلع الأخطاء |
| **Connection** | Options >> → Connection Properties → ✅ Encrypt + ✅ Trust server certificate |
| `scaffold.ps1` | ✅ شغال → 61/61 |
| `tools/merge-dbcontext.ps1` | ✅ v6 شغال → 8/8 |
| ⛔ `tools/run-sql.ps1` | **اتحذف نهائيًا 2026-08-30** |
| 🧊 `tools/extract-logo.ps1` | مجمد — شغل اللوجو اتقفل (X10) — لو رجعنا له محتاج إصلاح flood-fill لإزالة الخلفية البيضا |
| `tools/run-extract.ps1` | مشغّل بسيط لسكربت اللوجو (نفس الحالة 🧊) |

### الترتيب الإلزامي

```
dotnet build (نظيف) → scaffold.ps1 → merge-dbcontext.ps1 → dotnet build
```

### 🧹 تنظيف 2026-08-30

- `sql/` فيه الأساسي بس: `FastCom-schema.sql` · `v2-schema.db65922.sql` · `FastCom-fix-missing-tables.sql` · `diagnose.sql` · `list-tables.sql` · `verify-schema.sql`
- اتشال: النسخ المكررة للسكيما + `docs/SETUP.md` + `docs/VSCode-Steps.md` + سكربت Cloudflare من `index.html`
- `FastCom.Client` اتضاف لـ `FastCom.sln`
- ⚠️ 3 ملفات PNG فاضية في `docs/brand/` مقفولة بprocess — هتتمسح بعد restart (مالهاش أي تأثير)

---

## 9) 📌 القرارات المثبتة (D1–D14)

| # | القرار |
|---|---|
| D1 | **Consolidation = YES** · `TripOperations` m2m · `TripCostAllocations` · العهد والمصروفات على **الرحلة** |
| D2 | أرقام الحاويات = **الاتنين** |
| D3 | بعض الفواتير بضريبة وبعضها لأ |
| D4 | الأسطول **مختلط** (مملوك + مؤجر) → Payables حقيقية |
| D5 | 4–5 مستخدمين متزامنين |
| D6 | **السواقين مش هيستخدموا النظام** → مافيش PWA/Mobile/Offline |
| D7 | **monsterasp.net Premium Single $1.95** · EU |
| D8 | **Blazor WASM + Web API** على نفس الـ IIS site |
| D9 | **.NET 8** (اختيار المستخدم · EOL 2026-11-10) |
| D10 | مافيش Splash → Loading screen |
| D11 | **`CompanyProfile`** singleton · `SystemSettings` Key-Value بس |
| D12 | **بوابة العملاء** عبر `CustomerPortalTokens` + Magic Link (`TokenHash` SHA256) |
| D13 | SQL لازم يكون آمن للـ Panel (مافيش `CREATE DATABASE`/`USE`/`dbo.`) |
| D14 | أدوات EF معزولة لكل مشروع (`dotnet dotnet-ef`) |

### X — قرارات إضافية

- **X1** توزيع تكلفة الرحلة = **يدوي**
- **X3** Portal = Magic Link
- **X5** HR = Phase 2
- **X7** `CST-` = عميل · `CUS-` = عهد
- **X8** فرع واحد دلوقتي
- **X9** الهوية الرسمية: أزرق `#0B56DF` + برتقالي `#FF8015` (مستخرجة بقياس بكسل من `logo.pdf`)
- **X10** **إيقاف استخراج صورة اللوجو** — قرار المستخدم ("سيبك من اللوجو") — علامة "FC" مؤقتة في الـ UI
- **X11** **رفض LocalDB نهائيًا** — قاعدة سحابية بس (`db65922`) حتى مع مشاكل الشبكة
- **X12** الأسرار في **user-secrets** (Development) + `appsettings.Production.json` (gitignored) — مافيش أسرار في `appsettings.json`

### 🚫 خارج النطاق

Multi-tenancy · Load balancing/Redis · دفتر الأستاذ في MVP · الفاتورة الإلكترونية في MVP

---

## 10) ❓ أسئلة مفتوحة

| # | السؤال | الأثر |
|---|---|---|
| **Q3** | نسبة الضريبة بالظبط + التسجيل في ETA + الأسعار شاملة ضريبة؟ | **🔴 بيوقّف A9** |
| Q3b | مسجّل في منظومة الفاتورة الإلكترونية؟ | Phase 2 |
| Q4 | فرع واحد ولا أكتر؟ العملاء مشتركين؟ | |
| Q6 | دفتر الأستاذ في المرحلة الأولى؟ | |
| Q7 | التسعير لكل حاوية / عربية / كم؟ حد أدنى؟ | |
| Q9 | سقف العهد + مين يعتمد التسوية؟ | |
| Q13/Q14 | نطاق بوابة العملاء | |

### ⚠️ Placeholder في البيانات

- `CompanyProfile.TaxNumber = '000000000'`
- `Services.DefaultSellingPrice = 0`
- `CompanyProfile.DefaultTaxRateId` **مافيش FK**
- FKs للـ Audit User (`CreatedBy`/`UpdatedBy`) **ما اتعملتش**

---

## 11) ✅ Step 3.3 — Login + JWT (اتنفذت وشغالة)

> ✅ **تم التنفيذ كامل** — الدخول شغال بالـ JWT. القسم ده سِجِل تاريخي.

### المطلوب (اتنفذ)

| # | الشغل |
|---|---|
| 1 | **`AuthController`** — `POST /api/auth/login` + `POST /api/auth/refresh` + `GET /api/auth/me` |
| 2 | **JWT** — `Jwt:Key` (≥64 حرف) + `Issuer` + `Audience` + `ExpireMinutes` في `appsettings.json` |
| 3 | **`Program.cs`** — `AddAuthentication().AddJwtBearer(...)` + `AddAuthorization()` + Policies |
| 4 | **Client:** `Pages/Login.razor` + `Services/AuthService.cs` + تخزين الـ Token |
| 5 | **Client:** `AuthorizationMessageHandler` أو `HttpClient` interceptor لإضافة الـ Header |
| 6 | **`MainLayout`** — اسم المستخدم + زرار خروج |

### 🔑 توليد مفتاح JWT

```powershell
-join ((65..90) + (97..122) + (48..57) | Get-Random -Count 80 | ForEach-Object {[char]$_})
```

### ⚠️ انتبه

- **Identity keys = `int`** (A5)
- الجداول: `AspNetUsers` · `AspNetRoles` · `AspNetUserRoles` · `AppPermissions` · `AppRolePermissions` · `AppUserPermissions`
- **الصلاحيات 107** — لازم تتحمل في الـ Claims أو تتجلب من DB
- ⚠️ `AddApplicationServices()` / `AddInfrastructureServices()` **لسه commented out** في `Program.cs`

---

## 12) 🧾 متأخرات Step 2

- [ ] `AddApplicationServices()` / `AddInfrastructureServices()` commented out
- [ ] `Jwt:Key` محتاج قيمة حقيقية ≥64 حرف
- [ ] مافيش `README.md`
- [ ] مافيش `Directory.Build.props`
- [ ] مافيش `.vscode/launch.json`
- [ ] `Encrypt=True` → `appsettings.Production.json` (مأجّل)
- [ ] `QuestPDF` مثبّت على **2024.10.3** — أحدث إصدار **2026.8.0**

---

## 13) 🚨 تحذيرات بيئة

| | |
|---|---|
| **مافيش .NET SDK هنا** | أي كود أكتبه **مش بيتترجم** — قول كده بصراحة |
| **مافيش SQL Server هنا** | كل الـ SQL **فحص استاتيكي بس** |
| **الـ Panel بيبلع الأخطاء** | كل الـ DDL/DML عبر **SSMS** |
| **`LineNo` كلمة محجوزة** | → `[LineNo]` |

---

## 14) 📚 مسارات مهمة في الـ workspace

| المسار | إيه ده |
|---|---|
| `uploads/MASTER PROMPT — ....md` | المتطلبات — 1841 سطر، 67 قسم |
| `uploads/New Text Document.txt` | ⭐ **جرد قاعدة البيانات الحقيقي** من المستخدم |
| `reviews/01…06` | A1–A11 + سجل القرارات + مراجعة الـ Schema |
| `design/01-entity-map.md` | ✅ خريطة الكيانات المعتمدة v2 |
| `sql/FastCom-schema.sql` | ⭐ **الـ Schema المطبّق** — 2770 سطر |
| `sql/FastCom-recovery.sql` | 59 KB — السكربت اللي كمّل الـ DB |
| `sql/check-inventory.sql` | جرد الجداول |
| `tools/merge-dbcontext.ps1` | ✅ v6 |
| `scaffold.ps1` | ✅ |
| `docs/SETUP.md` · `docs/VSCode-Steps.md` | ⚠️ قديمين من Phase 6 |

### خريطة أقسام `FastCom-schema.sql`

```
01 ORGANIZATION 34    · 02 MASTER 135       · 03 PARTIES 306
04 EMPLOYEES 393      · 05 FLEET 428        · 06 PRICING 551
07 BOOKING 612        · 08 OPERATIONS 735   · 09 CUSTODY 907
10 EXPENSES 960       · 11 SALES 1012       · 12 PAYMENTS 1101
13 PAYABLES 1169      · 14 TREASURY 1266    · 15 DOCUMENTS 1337
16 SECURITY 1427      · 17 PERMISSIONS 1543 · 18 AUDIT 1584
19 INDEXES 1694       · 20 TRIGGERS 1780    · 21 SEED 2016
22 PERM SEED 2252     · 23 VIEWS 2527
```

---

## 15) ✍️ تحديث الملف ده

**حدّث الملف ده في نهاية كل خطوة:**

1. جدول "إحنا فين" (قسم 0)
2. "آخر حاجة — مستنيين من المستخدم" (قسم 1)
3. سجل الأخطاء (قسم 3) — **أي خطأ جديد يتسجل فورًا**
4. حالة الملفات (قسم 5)

> 🔴 **أهم حاجة: سجل الأخطاء.** كل خطأ بيتكرر بيكلّف المستخدم وقت وإحباط.

---

*آخر تحديث: 2026-08-30 — بعد كتابة Step 3.3 (Login + JWT)، 15 ملف، مستنيين build*
