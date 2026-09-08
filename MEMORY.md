# 🧠 FastCom — ملف الذاكرة

> **آخر تحديث: 2026-09-08 — إصلاح الـ Git (كل الشغل اتحفظ في 5 Commits منطقية) + إنشاء `AGENT-MEMORY.md` + `docs/REMEDIATION-PLAN.md` + `docs/TOOLKIT-SKILLS.md` + `.gitattributes`. البناء Debug النهائي: 0 أخطاء / 0 تحذيرات.**
> **آخر تحديث: 2026-09-06 (مساءً)** — خلصنا Steps 4→11 + لوحة المؤشرات. فاضل التقارير + البحث + الإعدادات.
> **ده ملف الذاكرة الرئيسي. اقرأه الأول قبل أي حاجة.**
> **المسار:** `FastCom/MEMORY.md`

---

## 0) 🚦 إحنا فين دلوقتي بالظبط

> **آخر تحديث 2026-09-06.** الخطوات 4→11 + لوحة المؤشرات **اتكتبوا كلهم**.
> 🔴 **مافيش .NET SDK ولا SQL Server هنا** — كل حاجة **فحص استاتيكي بس، عمرها ما اتترجمت**.
> المستخدم هو اللي بيعمل الـ Build ويبلّغ بالأخطاء (وده اللي حصل فعلًا 4 مرات يوم 09-06).

| Step | الاسم | الحالة |
|---|---|---|
| 0 | مراجعة + اختيار التقنية | ✅ |
| 1 | قاعدة البيانات (`db65922`) | ✅ مؤكد من المستخدم |
| 2 | Solution skeleton + Scaffold + Merge | ✅ مؤكد من المستخدم |
| 3 | الواجهة + Shell + Login | ✅ |
| 4 | مستخدمين / أدوار / صلاحيات | ✅ مقبول |
| 5 | البيانات الأساسية (عملاء·موردين·سواقين·أسطول) | ✅ |
| 6 | الحجوزات | ✅ |
| 7 | الحاويات + العمليات + الرحلات | ✅ |
| 8 | المصروفات + العهد | ✅ |
| 9 | الفواتير + التحصيل | ✅ |
| 10 | الخزينة + كشف حساب العميل | ✅ مقبول («تمام زى الفل اكمل») |
| 11 | المستندات + الإشعارات + تسليم الفواتير + **بوابة العميل** | ✅ اتكتب — build عدّى |
| **12** | **التقارير + `/reports`** | 🔵 **الجاي** |
| 12b | لوحة المؤشرات على `Home.razor` | ✅ اتعملت (2026-09-06) |
| 12c | إصلاح `GlobalSearch` (7 لينكات بايظة) | ⏳ |
| 12d | `/settings` (بيانات الشركة + `SystemSettings`) | ⏳ |

### 🔴 ترتيب الشغل بكره

1. **`/reports`** — `ReportsController` + صفحة 4 تبويبات (تشغيل · مالي · أسطول · عملاء) + تصدير Excel بـ **ClosedXML 0.104.1** (مثبّت أصلًا)
2. **إصلاح `GlobalSearch`** — سريعة (شوف قسم 7)
3. **`/settings`** — `BrandingController` لسه **GET بس** + `SystemSettings` (DbSet موجود)
4. تحديث الملف ده في الآخر

### ⚠️ حاجات معلّقة لازم تسأل المستخدم عنها

| | |
|---|---|
| 🔴 **`sql/FastCom-operations-standalone.sql`** | **اتنفذ في SSMS ولا لأ؟** — Step 7b بيعتمد عليه |
| 🔴 **`vw_OperationProfitability`** | مش متظبطة في الـ DbContext. **قرار مقترح:** نضيفها keyless entity (تعديل واحد في `FastComDbContext`) ونقرأ منها في التقارير — بدل ما نعيد حساب توزيع تكلفة الرحلات في الكود |
| زرار «التقارير» + `/reports` | **بيودّوا 404** دلوقتي — الصفحة لسه مش موجودة |
| `MailKit` مش مثبّت | مافيش إرسال إيميل حقيقي. قنوات التسليم: واتساب · بوابة · فاكس · يدوي بس |

---

## 1) ⏸️ آخر حاجة — مستنيين من المستخدم

### 🟢 2026-09-06 — اللي اتعمل النهارده (Steps 11 + 12b)

**11أ — المستندات + الإشعارات + تسليم الفواتير:**
- `Controllers/Admin/DocumentsController.cs` (`api/documents`) — رفع multipart 10 ميجا، اسم الملف **GUID**، **SHA-256**، فحص الامتدادات، `HasExpiry` ⇒ تاريخ الانتهاء إجباري. التخزين: `wwwroot/App_Data/documents/{yyyy}/{MM}/{guid}{ext}`
- `Controllers/Admin/NotificationsController.cs` (`api/notifications`) — **الإشعار العام (`UserId=NULL`) بيتعلّم مقروء بنسخة خاصة للمستخدم** عشان مايتخفيش عن الباقين
- `POST api/invoices/{id}/send` + `GET .../send-logs` — بيسجّل في `InvoiceSendLogs` ويحوّل الفاتورة «مُرسلة»
- `Pages/{Documents,Notifications,InvoicePrint}.razor` + `Shared/Components/NotificationBell.razor` + `wwwroot/js/fastcom.js` (`downloadFile` بـ fetch+blob عشان الـ JWT · `printPage` · `copyText`)

**11ب — بوابة العميل (`/portal/{Token}`):**
- `Controllers/Portal/PortalController.cs` — 4 نقاط `[AllowAnonymous]` بالرابط + 4 نقاط إدارة بـ `CUSTOMER.PORTAL_TOKEN`
- **الأمان:** توكن 256-بت عشوائي · **SHA-256 بس هو اللي بيتخزّن** · Stateless · 6 فحوص (موجود·نشط·مش مُبطل·صالح·`PortalEnabled`·حد الاستخدام) · `PortalAccessLogs` بيسجّل `View/Login/Denied` · العميل بيشوف `Issued/Sent/Approved/Returned` بس
- `Shared/Layout/EmptyLayout.razor` (Layout فاضي — غير `BlankLayout` اللي بتلف في `div.fc-blank`)
- `Customers.razor` + زرار 🔗 + مودال إدارة الروابط · `CustomerListItemDto` += `PortalEnabled`

**12ب — لوحة المؤشرات:**
- `Controllers/DashboardController.cs` — `summary` (17 رقم) · `alerts?take=5` · `monthly` (6 شهور) · `top-customers?take=5`
- `Pages/Home.razor` اتكتبت من جديد (هيرو + صفّين KPIs + تنبيهات قابلة للنقر + أعمدة CSS + بلاطات + أعلى العملاء)
- **🔴 قرار: رصيد الخزينة بيتقرا من `CashBoxes.CurrentBalance`** (اللي `trg_CashTransactions_BalanceSync` بيحسبها) — مش إعادة حساب في الكود


### 🔴 2026-09-05 — مستنيين دلوقتي
- **الباسورد المعتمد: `Adminnimda@1`** (قرار المستخدم). كل النسخ اليدوية السابقة كانت مكسورة (#22 PRF=0 · #24 هيدر ناقص 8 بايت) → **النسخة النهائية الوحيدة:** `sql/FastCom-set-admin-password-FINAL.sql` — blob **61 بايت** متحقق منه بمحاكي حرفي لطريقة قراءة Identity.
- **شاشة الدخول اتبنت احترافية بدون أدوات مطور خالص** (قرار المستخدم) — الـ reset-password endpoint لسه في السيرفر (Development بس) بس مفيش UI بيوصله دلوقتي.
- **اللوجو:** فولدر جديد `src/FastCom.Client/wwwroot/img/` فيه `logo.png` (كامل) + `logo-icon.png` (أيقونة 1353×745 شفاف).
- **قائمة النسخ النهائية (6):** Login.razor · MainLayout.razor · index.html · app.css · img/logo.png · img/logo-icon.png + FINAL sql في SSMS.
- **2026-09-05 بالليل:** 🎉 الـ hash الـ 61 بايت **اشتغل** — الدخول عدّى التحقق ووصل لإصدار الـ Token. العرقلة الجديدة كانت `Jwt:Key` طوله 62 حرف (الكود بيطلب ≥64) → اتولد مفتاح 80 حرف جديد واتسلم كأمر `dotnet user-secrets set` جاهز. 🔴 المفتاح ده كمان اتبعت في الشات — يتجدد قبل النشر.
- **مسار مشروع المستخدم:** `D:\Transport\FastCom-project\FastCom\` (ظاهر في stack trace).
- **Step 5 بدأ (2026-09-05):** 5.1 العملاء اتسلّم: `Controllers/Master/CustomersController.cs` (ترقيم CST- من usp_GetNextNumber بـ OUTPUT param + حذف ناعم + toggle-active + تحقق ضريبي شركة/فرد) + `Pages/Customers.razor` بنفس نظام الهيرو. الجاي: 5.2 موردين/سائقين/أسطول ← 6 حجوزات ← ...
- **Step 4 شغّال (2026-09-05):** 4.1 سيرفر اتسلّم: `Controllers/Admin/{AdminDtos,UsersController,RolesController}.cs` — سياسات `PERM:USER.VIEW` / `PERM:USER.MANAGE` / `PERM:ROLE.MANAGE` + `Invalidate()` بعد أي تغيير صلاحيات + مافيش حذف نهائي (تعطيل بس) + ADMIN role ممنوع تعديل مصفوفته. 4.2 اتسلّم: `Pages/Users.razor` (route `/users` = نفس لينك NavMenu الموجود) + بلوك CSS صفحات الإدارة في app.css. **4.3 اتسلّم:** هيرو هيدر عرض كامل ريسبونسف (نفس الهوية) + `Pages/Roles.razor` (مصفوفة صلاحيات بالموديولات + إنشاء/تعديل/حذف دور) + مودال استثناءات الصلاحيات جوه صفحة المستخدمين + لينك `/roles` في NavMenu (`_canRoles` = ROLE.MANAGE) + `fc-mini-btn` في CSS. نظام الـ hero: `.fc-hero/.fc-hero-inner/.fc-kpis-glass/.fc-overlap`. **🔴 تعديل Layout جذري (2026-09-05):** شيلت `MudContainer` من `MainLayout` (كان بيحبس الهيرو جوه container + padding أبيض فوقه = "مش باين") → الصفحات العادية اتلفت بـ `.fc-container` (Home/Health/Search) وصفحات الإدارة بتتحكم في عرضها بنفسها.


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

### ✅ 3) اللوجو — **اتضاف (2026-09-05)**
المستخدم بعت PNG جديد (أزرق/برتقالي 3D). اتعالج بـ PIL: شفافية الخلفية (max channel ≤ 28 → alpha 0) + قص الأيقونة لوحدها (عنق الصفوف عند y≈751).
- `src/FastCom.Client/wwwroot/img/logo.png` — اللوجو كامل (أيقونة + كلمة Fastcomm) → لوحة البراند في شاشة الدخول.
- `src/FastCom.Client/wwwroot/img/logo-icon.png` — الأيقونة بس (1353×745) → كارت الدخول + الهيدر + شاشة التحميل + الـ favicon.
- التعديلات: `Login.razor` (سطر 21 و42) · `MainLayout.razor` (سطر 22) · `index.html` (favicon سطر 10 + loading-mark سطر 28) · `app.css` (4 كلاسات + أنيميشن جديد `fc-logo-pulse`).

### ✅ Step 3.5 اتعمل (2026-08-31 فجرًا) — حماية الـ API كلها
- **ملف جديد `src/FastCom.Server/Auth/PermissionAuthorization.cs`:**
  - `PermissionRequirement` + `PermissionAuthorizationHandler` (**fail-closed**: لو مفيهوش perm claim = رفض) + `PermissionPolicyProvider` (بينشئ policies ديناميكيًا من `[Authorize(Permission = "X.Y")]` من غير تسجيل مسبق).
  - الـ handler بيقرا claim `"perm"` اللي بيبنيها `TokenService` وقت الـ login (من `GetPermissionsAsync`).
- **الـ Controllers:** `SearchController` + `DiagController` كانوا **مفتوحين تمامًا** — اتقفلوا `[Authorize]`. `AuthController` المستوى: `[Authorize]` على الكلاس + `[AllowAnonymous]` لـ `login`/`seed-admin`/`reset-password` (الاثنين الأخيرين بيحرسهم فحص Development جوه الكود).
- **مفتوحين بقصد** (محتاجينهم قبل الدخول): `HealthController` + `BrandingController`.
- **Program.cs:** تسجيل الـ handler والـ policy provider في الـ DI (`AddSingleton`).
- **مصفوفة الاختبار الأمنية (10/10 ✅):** بدون توكن → `health/branding` 200 (مقصود) + `me/logout/search/diag` **401**. بتوكن admin → كلها **200** + `/me` رجّعت الـ 107 صلاحية. واختبارات بوابة الـ Development على `seed-admin`/`reset-password` نجحت برسائل أمان واضحة وصفر side-effects.
- 🔧 **درس معماري:** الصلاحيات **مش في الـ JWT** (عشان 107 صلاحية = 4–5 KB في كل Header).

> 🔴 **تصحيح 2026-09-05:** الفقرة دي كانت بتقول إن الـ handler بيقرا claim `"perm"` من الـ token.
> **ده كان النسخة القديمة.** الكود الحالي بيجيب الصلاحيات **من قاعدة البيانات** عن طريق
> `TokenService.GetPermissionsAsync()` مع **كاش 60 ثانية**.
>
> **الميزة:** تغيير صلاحيات مستخدم بينفّذ خلال دقيقة من غير Logout.
> **الـ Client** لسه بياخد الصلاحيات من `/api/auth/me` عشان يفلتر القائمة.

### 🟢 2026-09-05 — سحب `d264bb0` + تكملة الناقص

**المستخدم رفع commit `d264bb0`** (Step 3.5 + README + مسح logo.pdf + `Components`).
سحبنا وكملنا **8 حاجات ناقصة**:

| # | الناقص | الأثر لو ما اتعملش |
|---|---|---|
| 1 | **`FallbackPolicy`** | أي Controller جديد في Step 4+ هيبقى **مفتوح** لو نسيت `[Authorize]` |
| 2 | **`MapFallbackToFile().AllowAnonymous()`** | 🔴 **التطبيق كله كان هيقع على 401** — شاشة الدخول مش هتظهر |
| 3 | **فلترة البحث بالصلاحيات** | أي مستخدم مسجّل كان يقدر يدور في الفواتير والعملاء |
| 4 | `policyName` null check | NullReferenceException محتمل |
| 5 | **`IPermissionService`** (ملف جديد) | الـ Handler والـ SearchController كان هيكرروا نفس الاستعلام |
| 6 | ADMIN bypass في الـ Handler | استعلام زيادة لكل طلب |
| 7 | `Models/SearchResult.cs` + `GlobalSearch.razor` | الـ Client مش هيعرف الوحدات المتاحة |
| 8 | `docs/` (4 ملفات) | ما اترفعتش في الـ commit |

#### 📁 `src/FastCom.Server/Auth/` — الشكل النهائي

| الملف | فيه إيه |
|---|---|
| `TokenService.cs` | JWT + `GetPermissionsAsync` (ADO.NET CTE) |
| `PermissionAuthorization.cs` | `PermissionRequirement` · `PermissionAuthorizationHandler` · `PermissionPolicyProvider` |
| **`PermissionService.cs`** 🆕 | `IPermissionService` + `PermissionService` (كاش 60 ثانية + `Invalidate(userId)`) |

> 🔴 **الفولدر `Auth/` مش `Authorization/`** — والـ namespace `FastCom.Server.Auth`.
> **مافيش فولدر `Authorization/` خالص** — ده كان سبب الـ CS0101.

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
| 21 | 🔴 `CS0246 AuthorizeAttribute could not be found` | ناقص `@using Microsoft.AspNetCore.Authorization` | اتحط في **`_Imports.razor`** (مش في كل صفحة) |
| 20 | 🔴 `RZ1017 Unexpected literal following the 'attribute' directive` | **تعليق على نفس سطر `@attribute`** | التعليق في سطر لوحده **فوق** الـ directive |
| 19 | 🔴 `CS1061 AuthorizationHandlerContext.HttpContext` | **الخاصية مش موجودة** — الـ HttpContext هو `context.Resource` | `context.Resource as HttpContext` |
| 15 | صورة اللوجو من PDF = أبيض/أسود | CMYK/YCCK معكوس + SMask بـ TIFF Predictor 2 | اتظبط التحليل وألوان اللوجو اتحسلت (`#0B56DF`/`#FF8015`) — والصورة نفسها اتلغت (X10) |
| 22 | 🔴 الدخول بيفشل دايمًا بعد سكريبت SQL للباسورد | **كتبت PRF id = 0** في الـ hash، لكن Identity V3 بيكتب **2** (`KeyDerivationPrf.HMACSHA512`). وقت التحقق بيشتق بـ SHA1 → مفيش تطابق أبدًا | الـ hash اتولد بـ `prf=2` + محاكاة كاملة لطريقة تحقق Identity قبل التسليم (القديم يفشل ❌ / الجديد ينجح ✅) |
| 23 | أنيميشن شاشة التحميل مش شغال | تعريفين `@keyframes fc-pulse` (الأزرق + الأخضر) — **التاني بيOverride الأول** | أنيميشن اللوجو اتسمى `fc-logo-pulse` |
| 24 | 🔴🔴 الـ hash اليدوي يفشل حتى بعد تصحيح PRF | فورمات V3 الحقيقي = **13 بايت هيدر**: `0x01 \| prf(4) \| iterCount(4) \| saltSize(4)` — أنا كنت كاتب 5 بايت بس (53 إجمالي بدل **61**). Identity بيقرأ saltSize من جوه الـ salt → استثناء → Failed | الـ blob اتولد 61 بايت (prf=2, iter=100000, saltSize=16) + محاكي حرفي لطريقة قراءة Identity قبل التسليم |
| 25 | 🔴 `CS0119 'Documents.Size(long)' is a method` | **عضو في الكومبوننت اسمه نفس Enum بتاع MudBlazor** مستخدم كـ static (`Size="Size.Small"`) | غيّر اسم العضو (`Size(long)` → `FileSize(long)`). **الكاشف:** `tools/razor-member-scan.py` |
| 26 | 🔴 `CS0542 member names cannot be the same as their enclosing type` | `private class Count { public int Count {...} }` | غيّر اسم الكلاس (`Count` → `UnreadCount`) |
| 27 | 🔴 `CS7036 + CS1026` في Razor | **نص C# جوه attribute بفواصل `"`** — `OnClick="() => SetTab("x")"` | الأفضل: **ميثود مساعدة** (`OnClick="BackToList"`). أو فواصل `'` |
| 28 | 🔴 `RZ1010 Unexpected "{" after "@"` | **`@{ }` جوه بلوك كود** (`@if/@else/@foreach`) | حاسبها خالص — استخدم خاصية أو ميثود (`Numbered()` · `Peak`) |
| 29 | 🔴 `CS0102 already contains a definition for 'Summary'` | ميثود `Summary()` + record `Summary` في نفس الكلاس | record → `SummaryOut`. **الكاشف:** `tools/type-scan.py` |
| 30 | 🔴🔴 `CS0019 Operator '<' cannot be applied to 'DateTime?' and 'DateOnly'` | **أنا مفترض أنواع أعمدة التاريخ.** الحقيقة: `Operation.PlannedDate`/`ActualDeliveryAt` = `DateTime?` · `DriverCustody.CustodyDate`/`Expense.ExpenseDate`/`Trip.PlannedStartAt` = `DateTime` · `Invoice.InvoiceDate`/`DueDate`/`Payment.PaymentDate`/`Driver.LicenseExpiryDate`/`Vehicle.InsuranceExpiryDate` = `DateOnly(?)` | اقرا النوع من `Persistence/Generated/*.cs` **قبل** ما تكتب. **الكاشف:** `tools/type-scan.py` |
| 31 | 🔴 `CS1503 cannot convert 'DateTime?' to 'DateTime'` | `DateOnly.FromDateTime(o.PlannedDate)` والعمود Nullable — الـ `Where` ضمن إنها مش null **بس Roslyn مابيعرفش** | `if (o.PlannedDate is null) continue;` + `.Value` |
| 32 | 🔴 `CS1061 'Customer' does not contain 'Mobile'` | اخترعت خاصية | `Customer` فيها `Phone` و`Email` بس |
| 33 | 🔴 `CS1061 'InvoiceItem' does not contain 'LineNo'` | **مافيش عمود `LineNo` في `InvoiceItems`** | الترتيب بـ `InvoiceItemId` |
| 34 | 🔴 `CS1061 'Payment' does not contain 'MethodName'` | اخترعت | → `Payment.PaymentMethod.NameAr` |
| 35 | 🔴 `CS1061 'int' does not contain 'Value'` | `var days = req?.X ?? 7;` → النوع بقى `int` مش `int?` | شيل `.Value`. **الكاشف:** `tools/type-scan.py` |
| 36 | 🔴 `CS8602 Dereference of a possibly null reference` | **Roslyn مابيعرفش من فكّ الـ tuple** إن `cust` مش null لما `t` مش null | حارس صريح: `if (cust is null) return ...;` — الـ `!` بتسكت بس مش بتحل |
| 37 | 🔴🔴 **سلّمت ملفات والمستخدم بنسخ قديمة** — رجع يشتكي من أخطاء أنا صلّحتها | النسخ اليدوي | **ابعت MD5 + سطور محددة يتأكد بيها** («دوّر على `if (cust is null)` — لازم تلاقيها 6 مرات») + قولله يعمل **Reload Window** |


### 🚫 قواعد مستفادة

- **`@bind-X` = `X` + `XChanged`.** ماتكتبش `XChanged` مع `@bind-X`.
- **`Icons.Material.Filled.*`** — ماتخترعش. استخدم القائمة المضمونة (تحت).
- **`SqlQueryRaw<T>`** — اشتغل بـ ADO.NET، أضمن وأوضح.
- **ماتبلعش الاستثناءات.** ارجّع الرسالة في الـ JSON.
- **PowerShell regex ≠ Python regex.** `\s` و multiline مختلفين.
- **ماتختبرش على fixture متخيّل.** شوف الملف الحقيقي الأول.
- **ماتحقنش dependencies كـ fields بـ `null!`** — حقنها في الـ Constructor.
- **localStorage في WASM = JS Interop.** مافيش طريقة static.
- **🔴 `[Authorize]` في الـ Client محتاج `@using Microsoft.AspNetCore.Authorization`.**
  مش نفسه `Microsoft.AspNetCore.Components.Authorization` (ده بتاع `AuthorizeView`).
  **الاتنين موجودين في `_Imports.razor`** — ماتشيلش واحد فيهم.
- **🔴 `@attribute` / `@page` / `@inject` / `@using` = سطر لوحده.**
  أي تعليق `@* *@` بعدهم في نفس السطر → **RZ1017**.
  الصح:
  ```razor
  @* التعليق هنا *@
  @attribute [Authorize]
  ```
- **🔴 `AuthorizationHandlerContext` مفيهوش `HttpContext`.**
  الموجود: `User` · `Requirements` · `Resource` · `PendingRequirements` · `HasFailed` · `Succeed()` · `Fail()`.
  الـ HttpContext = `context.Resource as HttpContext`.
- **🔴 `Microsoft.AspNetCore.Components.Authorization` package منفصل.**
  **مش** transitively مع `Components.WebAssembly`.
  بدونه → `CS0234: 'Authorization' does not exist in 'Microsoft.AspNetCore.Components'`
  + `CS0246: AuthenticationStateProvider` + `AuthenticationState`.
  **الحل:** `<PackageReference Include="Microsoft.AspNetCore.Components.Authorization" Version="8.0.11" />

### 🔴🔴 قواعد جديدة (2026-09-06) — **اقرأها قبل ما تكتب أي كود**

#### أ) الأدوات التلاتة — **شغّلهم قبل كل تسليم**
```bash
python3 tools/razor-attr-scan.py      # nested " جوه attributes   → لازم TOTAL: 0
python3 tools/razor-member-scan.py    # CS0119 + CS0542           → لازم TOTAL: 0
python3 tools/type-scan.py            # أنواع التواريخ + CS0102 + CS1503 → TOTAL: 0
```
> ⚠️ **`razor-attr-scan.py` كان line-based في الأول وكان بيكدب** (قال 0 وفيه 8 عيوب).
> **أي أداة فحص لازم تتأكد منها بملف اختبار فيه العيب المعروف** — متثقش في `TOTAL: 0` على طول.

#### ب) أنواع أعمدة التاريخ — **ممنوع التخمين**
```
DateTime?  → Operation.PlannedDate · Operation.ActualDeliveryAt · Trip.PlannedStartAt
DateTime   → DriverCustody.CustodyDate · Expense.ExpenseDate
DateOnly   → Invoice.InvoiceDate · Payment.PaymentDate
DateOnly?  → Invoice.DueDate · Driver.LicenseExpiryDate · Vehicle.InsuranceExpiryDate
```
- `DateTime` **ماتتقارنش** بـ `DateOnly` → `x.ToDateTime(TimeOnly.MinValue)` أو `DateOnly.FromDateTime(x)`
- **مافيش `DateOnly.ToString()` ولا تركيب نصوص جوه LINQ-to-Entities** → `ToListAsync()` الأول وبعدين ابني في الذاكرة
- **Roslyn مابيعرفش من الـ tuple** → حارس null صريح بعد كل `(a, b, err) = await Resolve()`

#### ج) Razor — 4 قواعد بتتكرر
1. **أي نص C# جوه attribute ⇒ فواصل `'`** — أو الأفضل ميثود مساعدة
2. **مافيش `@{ }` جوه بلوك كود** (RZ1010) — خاصية أو ميثود
3. **مافيش `@bind:after` على `<select>` عادي** — `value=` + `@onchange`
4. **ممنوع اسم عضو = اسم Enum** (`Size`/`Color`/`Variant`/`Severity`/`Typo`/`Align`/...)

#### د) Entity properties — **ممنوع الاختراع**
- `DbSet` مش دايمًا باسم الـ entity: **`VehicleMaintenances`** (جمع)
- `InvoiceItem.LineSubtotal/LineTax/LineTotal` كلهم **`decimal?`** (أعمدة محسوبة PERSISTED)
- `Customer` فيها `Phone`/`Email` — **مافيش `Mobile`**
- **`InvoiceItems` مفيهاش `LineNo`**
- قبل ما تكتب أي خاصية: `grep` في `src/FastCom.Infrastructure/Persistence/Generated/`

#### هـ) التسليم
- **مافيش zips** — مسارات بس
- **MD5 + عدد الأسطر + سطور محددة يتأكد بيها المستخدم** (مشكلة النسخ القديمة حصلت أكتر من مرة)
- **قول بصراحة: «مافيش SDK هنا — static audit بس»**
`

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

## 5) 📁 الملفات — الحالة الحالية (2026-09-06)

### Server — `src/FastCom.Server/Controllers/`  (27 ملف)

| الفولدر | الملفات |
|---|---|
| `Admin/` | `AdminDtos` · `UsersController` · `RolesController` · **`DocumentsController`** · **`NotificationsController`** |
| `Auth/` | `AuthController` · `AuthDtos` |
| `Master/` | `Customers` · `Suppliers` · `Drivers` · `Fleet` · `Containers` · `Options` |
| `Operations/` | `Bookings` · `Operations` · `Trips` |
| `Finance/` | `Expenses` · `Custodies` · `Invoices` · `Payments` · `Treasury` |
| **`Portal/`** | **`PortalController`** — بوابة العميل (4 نقاط `[AllowAnonymous]` بالرابط) |
| الجذر | `HealthController` · `BrandingController` (**GET بس**) · `SearchController` · **`DashboardController`** · `DiagController` |

### Client — `src/FastCom.Client/`

| | |
|---|---|
| **`Pages/` (26)** | Home · Login · Health · Search · Users · Roles · Customers · Suppliers · Drivers · Vehicles · Containers · Bookings · **BookingForm** · Operations · **OperationForm** · Trips · **TripForm** · Expenses · Custodies · Invoices · **InvoicePrint** · Payments · Treasury · **Documents** · **Notifications** · **Portal** |
| **`Shared/Layout/`** | `MainLayout` (+ `<GlobalSearch/>` + `<NotificationBell/>`) · `NavMenu` · `BlankLayout` (لـ Login — بتلف في `div.fc-blank`) · **`EmptyLayout`** (لبوابة العميل — `@Body` بس) |
| **`Shared/Components/`** | `GlobalSearch` ⚠️ · **`NotificationBell`** · `RedirectToLogin` |
| **`wwwroot/`** | `index.html` · `css/app.css` (**1321 سطر · 175 كلاس `.fc-`**) · `js/fastcom-storage.js` · **`js/fastcom.js`** · `img/` |

### 🔴 الـ Routes الموجودة (29) — ممنوع تكرارها
```
/  /login  /health  /search  /users  /roles  /customers  /suppliers  /drivers  /vehicles
/containers  /bookings  /bookings/new  /bookings/new/{Id:long}
/operations  /operations/new  /operations/new/{Id:long}
/trips  /trips/new  /trips/new/{Id:long}
/expenses  /custodies  /invoices  /invoices/print/{Id:long}
/payments  /treasury  /documents  /notifications  /portal/{Token}
```
**⚠️ `/reports` و `/settings` موجودين في NavMenu بس مافيش صفحات → 404**

### 🛠️ أدوات الفحص — `tools/`

| الأداة | بتكشف | ملاحظة |
|---|---|---|
| ⭐ `razor-attr-scan.py` | `"` متداخلة جوه attributes | **كانت line-based وبتكدب** — اتعدلت whole-file |
| ⭐ `razor-member-scan.py` | CS0119 (عضو = Enum) + CS0542 | جديدة 09-06 |
| ⭐ `type-scan.py` | أنواع التواريخ (CS0019/CS1503) + CS0102 | جديدة 09-06 — بتقرا الأنواع من `Generated/*.cs` |
| `merge-dbcontext.ps1` | — | ✅ v6 |
| ~~`run-sql.ps1`~~ | — | ⛔ **ميت** |

### Infrastructure — `src/FastCom.Infrastructure/`

| الملف | الحالة |
|---|---|
| `Persistence/FastComDbContext.cs` | ✅ extends **`IdentityDbContext`** · **مافيش global query filter** (⇒ `!IsDeleted` في كل query) |
| `Persistence/ScaffoldedDbContext.cs` | ✅ 61 DbSet · البارامتر `modelBuilder` |
| `Persistence/Generated/*.cs` | ✅ namespace **`FastCom.Domain.Entities`** — **المصدر الوحيد لأنواع الأعمدة** |

### ⚠️ فرق مهم في المسارات

| هنا في الـ workspace | عند المستخدم |
|---|---|
| `src/FastCom.Client/Shared/Components/` | `src\FastCom.Client\Shared\componant\` ← **غلطة إملائية من المستخدم** |

> مش بتأثر على الـ build. بس **خليك فاكر** لما تديله مسار.

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
- ✅ **Steps 5–11 اتبنت** — بس **صفحات التفاصيل الفردية لأ** (مافيش `/customers/{id}`)

### 🔴 عيب لازم يتصلّح (12c) — `GlobalSearch.razor` فيه 7 لينكات بايظة

`Routes` dict (سطر 75) بيبعت على راوتات **مش موجودة** → 404:
```
["عميل"]   = "/customers/{0}"      ❌ الموجود /customers بس
["حجز"]    = "/bookings/{0}"       ❌
["عملية"]  = "/operations/{0}"     ❌
["فاتورة"] = "/invoices/{0}"       ❌
["رحلة"]   = "/trips/{0}"          ❌
["حاوية"]  = "/containers/{0}"     ❌
["سيارة"]  = "/fleet/vehicles/{0}" ❌ (مافيش /fleet خالص — الصفحة /vehicles)
```
**الحل المقترح:** يودّي على صفحة الليستة مع فلتر (`/customers?q=...`) — مش على تفاصيل فردية

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

*آخر تحديث: **2026-09-06** — Steps 11أ (مستندات · إشعارات · تسليم فواتير · طباعة) + 11ب (بوابة العميل) + 12ب (لوحة المؤشرات).*
*الـ Build عدّى عند المستخدم بعد 4 جولات إصلاح أخطاء (CS0119 · CS0542 · RZ1010 · CS0102 · CS0019 · CS1503 · CS8602 · CS1061).*
*الجاي: **`/reports`** ← إصلاح `GlobalSearch` ← `/settings` ← تحديث الملف ده.*
