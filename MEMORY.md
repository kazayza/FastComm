# 🧠 FastCom — ملف الذاكرة

> **آخر تحديث: 2026-09-27 — جلسة FAB شاشة العميل: إصلاح تنسيق زر الإجراءات العائم في `CustomerForm.razor` (إضافة CSS ناقص `glow-fab-*` + تمييز primary/danger) ثم توحيد لونه للأزرق `#0B56DF` بهوية البرنامج. ⚠️ الـ Build لم يُتحقق منه — التيرمنال كان مشغولًا بعملية `dotnet run` للسيرفر.**
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

## 20) ✅ 2026-09-27 — إصلاح تنسيق FAB شاشة العميل + توحيده للأزرق

**الملف الوحيد:** `src/FastCom.Client/Pages/CustomerForm.razor` (+ تحديث `MEMORY.md`).

**التشخيص:** أسفل الشاشة كان يظهر زرّان بتنسيق غلط (`حفظ بيانات العميل` + `إلغاء والعودة للدليل`) — السبب أن ماركب `glow-fab-*` كان موجودًا من غير أي CSS (`glow-fab-*` = صفر تعريف في الملف)، فظهر بتنسيق المتصفح الافتراضي. القرار: **تعديل التصميم وليس حذف الـ FAB** — بصلاحية الشاشة دي فقط.

### اللى اتعمل (خطوتان)

1. **إضافة بلوك CSS `glow-fab-*` كامل (~80 سطر):** زر رئيسي دائري 60px + قائمة بيضاء فوقه (حفظ مميزة + إلغاء رمادية) + `align-items: flex-end` لإصلاح RTL (نفس درس `Home.razor`) + إخفاء افتراضي وظهور عند `is-open` + `scrim` + دوران 45° + حلقات نبض + دعم `print` و `prefers-reduced-motion` + إصلاح تعليق CSS متعدد السطور لصيغة Razor آمنة.
2. **توحيد اللون للأزرق بهوية البرنامج:** الزر من `linear-gradient(#22c55e→#16a34a)` أخضر إلى `linear-gradient(#0B56DF→#08409f)` + الظل/الحلقات/الهالة/الأيقونة/تمييز الحفظ كلها `rgba(11,86,223) / #e8f1ff / #b0c8f5`. زر الهيرو `حفظ العميل` الأخضر **لم يُلمس**.

### ⚠️ تحقق معلّق

- التعديل CSS + classes فقط — لا منطق `@code` اتلمس.
- **`dotnet build` لم يُنفذ** — التيرمنال كان مشغولًا بـ `dotnet run --project src/FastCom.Server` عند المستخدم. **لازم Build من جهته قبل الـ Push.**

---

*آخر تحديث: **2026-09-06** — Steps 11أ (مستندات · إشعارات · تسليم فواتير · طباعة) + 11ب (بوابة العميل) + 12ب (لوحة المؤشرات).*
*الـ Build عدّى عند المستخدم بعد 4 جولات إصلاح أخطاء (CS0119 · CS0542 · RZ1010 · CS0102 · CS0019 · CS1503 · CS8602 · CS1061).*
*الجاي: **`/reports`** ← إصلاح `GlobalSearch` ← `/settings` ← تحديث الملف ده.*

---

## 16) ✅ 2026-09-24 — تنظيف الواجهات + معيار الحقول

**تم في هذه الجلسة (Build: 0 أخطاء / 0 تحذيرات):**

1. **إصلاح تداخل `Home.razor`** — إلغاء `margin-top: -70px` (وعمل `-60px` على الموبايل) في `.home-content` داخل `home.css`؛ الكروت بقت تحت الهيرو بدل ما تغطي الـ KPIs. + توحيد شريط الهيرو: العنوان `26px`، الشبائح `border-radius: 20px` زي باقي الشاشات.
2. **`Bookings.razor`** — حذف زرّي الهيدر المتكررين («حجز جديد» + «تحديث») لأنهم اتنقلوا لقائمة الزر العائم `fc-fab-container` (عنصر `fc-fab-menu` فيه «حجز جديد» + «تحديث البيانات» + سكريم `.bl-fab-scrim`).
3. **`BookingForm.razor` Tab 3** — زر «إضافة حاوية (متبقي X)» بقى **جنب حقل ملاحظات الحاوية** (`.bk-notes-action-row`) بدل رأس القسم القديم؛ رأس «أرقام الحاويات الفعلية» ورسالة «مافيش حاويات» اتشالوا، والصفوف بتظهر فقط عند وجود `.Details` (`.bk-cc-detail-row` grid 6 أعمدة + زر حذف).
4. **📝 معيار الحقول: `docs/FIELD-DESIGN.md`** — المرجع الموحّد لحقول الإدخال (`.form-field` / `.field-label` / `.field-input` — ارتفاع 42px، border `#d8e2f0`، focus أزرق `rgba(30,110,245,.10)`). النمط `.field-*` هو المعتمد؛ الأنماط القديمة `.fc-field-*` و `.svc-field-*` مرشحة للترحيل لاحقًا.
5. **🐛 إصلاح موضع زر الإجراءات العائم في `Home.razor`** — `.glow-fab-wrap` كان فيه `flex-direction: column` **بدون** `align-items`، فقاعدة `flex-start` في سياق RTL كانت تثبّت الزر على الطرف الأيمن من صندوق القائمة (عند `x≈125px` بدل `left: 18px`) والقائمة تنحاز عنه. أُضيف `align-items: flex-end` مع تعليق موضّح، فصار مثبَّتًا في الركن السفلي الأيسر والقائمة داخل الشاشة.

### ⚠️ ملاحظات بيئة (من نفس الجلسة)

- البناء النهائي: **0 Warning / 0 Error**.
- التحقق الحيّ بالـPlaywright تم على منفذ جديد (`127.0.0.1:5099`) فسقطت جلسة الدخول (نفس الـlocalStorage لا يُشارَك بين منافذ مختلفة) ولا يوجد حساب اختبار موثّق في المستودع — فالقياس الحيّ مؤجَّل للمستخدم.
- **تحذير مهم للفحص البصري:** لقطات/قياسات الصفحة القديمة كانت تُقرأ من بناء قديم يقدّم كود `HEAD` لا من الملفات المعدَّلة. اعتمد على `git diff` + البناء بدل اعتقاد أن الصفحة تعكس التعديل.
- الملفات المؤقتة (`final-build.log` · `dll-check.txt` · `build-check*.log` · `client-run.*` · `server-run.*`) اتمسحت بعد الفحص.


---

## 17) ✅ 2026-09-25 — شروط الدفع: إكمال إعادة التصميم + إصلاح 8 أخطاء بناء

**الملف:** `src/FastCom.Client/Pages/PaymentTerms.razor` (بعد الإصلاح: 934 سطر · راوت `/master/paymentterms`).
**البناء:** `dotnet build src/FastCom.Client/FastCom.Client.csproj` → **`0 Error(s)`** / 4 تحذيرات قديمة (`MUDBLAZOR.MUD0002` و `CS0169` في Containers · Trailers · SupplierPayments · NotificationForm — **مش من PaymentTerms**).

### الأخطاء اللى كانت مكسّرة البناء (8 أخطاء · كلها من نفس الملف)

| الخطأ | الموضع | السبب الحقيقي |
|---|---|---|
| `RZ9980: Unclosed tag 'div'` | 15,1 | `</div>` قافل `.pt-shell` كان مفقود (وكمان كتلة المودال كاملة) |
| `RZ9980: Unclosed tag 'section'` | 18,5 | `</section>` بتاع الهيرو كان مفقود — الـKPIs جوه الهيرو (خلفيات `.pt-kpi` شفافة بيضاء) |
| `CS1525: Invalid expression term ')'` | 122 · 126 · 130 · 190 | `@onclick="() => _filter = "all""` — تنصيص مزدوج متداخل جوه attribute مخلي Razor يقفل القيمة بدري |
| `CS1002: ; expected` + `CS1513: } expected` | 190,76 | نفس السبب — الكتلة `{ _search = ...; _filter = "all"; }` بقت مقطوعة |

### اللى اتعمل

1. **التنصيص (السبب الجذري لأخطاء الـCS):** اتوحّد على نمط المستودع المقبول `@onclick='() => _filter = "all"'` — نفس اللى مستخدم في `Customers.razor` و `Bookings.razor`. **القاعدة: أي lambda فيها string جوّه attribute لازم الـattribute يبقى بعلامة تنصيص واحدة `'` ومايكونش `"`.**
2. **قفل الوسوم:** اتضاف `</section>` بعد كروت الـKPIs + `</div>` قافل `.pt-shell`.
3. **المودال:** اتبنى كامل بنفس لغة `Services.razor` → `.pt-modal-backdrop` (زوم/blur) + `.pt-modal-head` جريدنت `#0A1B3B → #16306B` + أيقونة create/edit + `.pt-modal-foot`، وبحقول `.field-*` المعتمدة (ارتفاع 42px · focus أزرق · نجمة `field-required`) + قفل حقل الكود عند `CodeLocked` (شرط مرتبط بعملاء) + شرائح المدد السريعة (`Presets` ← `SetPreset`) + رسالة خطأ تحت كل حقل عند `_tried` + معاينة `DaysLabel`.
4. **CSS:** وسم `<style>` كان **مقطوع** (الـCSS كان نص حر جوه الماركب) — اتفتح/اتقفل صح، وقسم الـCSS الثاني (toolbar · search · filters · states · card · table · badges · buttons · modal) اللى كان **مفقود بالكامل** اتعاد كتابته ببادئة `pt-`. التعليقات جوه `<style>` اتحوّلت من `@* *@` إلى `/* */`، ومفاتيح الأنيميشن/الميديا بقت `@@keyframes` / `@@media`.
5. **الحالة (`@code`):** اتضافت `SetPreset(int days)` (باقي الدوال كانت موجودة ومظبوطة على `TermItem` / `TermUpsert` من `MasterDataController`).

### قيود لسه واقفة (لا تتجاوزها)

- **مفيش `IsActive` ومفيش DELETE** في الـAPI → **مفيش toggle تنشيط ولا زر حذف** في الشاشة (وده مقصود، مش نساوة).
- **`Code` مقفول عند التعديل لما `UsageCount > 0`** — نفس قيد السيرفر، والـUI بيوضّح السبب في `field-hint`.
- **التحقق البصري الحيّ مؤجَّل:** مافيش حساب اختبار موثّق في المستودع، والـ`localStorage` مش بيتشارك بين المنافذ. التحقق المتاح = بناء نظيف + `git diff`.

### ⚠️ درس متكرر: الحذر من التعديل الجزئي على ملفات `@code`/`<style>`

الملف كان اتشوّه في الجلسة السابقة لأن **قطع كبيرة منه اتمسحت** (المودال + `</section>` + `</div>` + وسم `<style>` + قسم CSS كامل) من غير ما حد يقرا الـbuild log. **لو `RZ9980 Unclosed tag` ظهرت في ملف بلazor: دوّر على وسم الإغلاق المفقود في الآخر الأول، مش على الوسم المفتوح.** وكمان **اقرأ سجل البناء قبل ما تعتبر الشغل خلص**.

---

## 18) ✅ 2026-09-24 — شروط الدفع: هيرو + زر عائم بنمط شاشة الخدمات (المحاولة الأولى كانت غلط)

**الملف:** `src/FastCom.Client/Pages/PaymentTerms.razor`.
**البناء:** `FastCom.Client` ✅ **0 أخطاء من الملف** (التحذيرات القديمة بس: `MUD0002` ×3 في Containers/Trailers/SupplierPayments · `CS0169` في NotificationForm · `CS8602` في CustodiesController). أي `Build FAILED` بـ`MSB3027/MSB3021` سببه عملية `FastCom.Server` شغّالة وقافلة الـ`exe` (بيئة، مش كود).
**🔴 المحاولة الأولى (مرفوضة):** اتعمل الهيرو بالنمط الأول (`fc-hero` › `fc-page-head` › `fc-head-actions` + `fc-kpis-glass`) والزر جوه الهيرو — المستخدم رفض: «راجع صفحة الخدمات واعمل زيها، والزر عيبقى زر عائم». النسخة النهائية = **نمط شاشة الخدمات بالحرف + FAB**.

### المشكلة
الشاشة كانت بتستخدم **هيرو محلي** (`.pt-hero`) جوه حاوية محلية (`.pt-shell` بـ`max-width: 1320px`) — يعني:
1. الهيرو شكله مختلف عن باقي الشاشات (كارت أزرق `#1e6ef5 → #0d47a1` بحواف دائرية جوه الصفحة، بدل شريط كحلي كامل العرض).
2. عرض المحتوى 1320px بدل `--fc-page-w` (1560px) ⇒ الصفحة مش واخدة عرض الشاشة ومش على نفس خط باقي الصفحات.

### 🔴 القاعدة المعمارية (احفظها لكل شاشة جديدة)
- **عرض المحتوى متحكَّم فيه بمتغيّر واحد:** `--fc-page-w: 1560px` في `app.css:12`.
  - الجسم: `.fc-page { max-width: var(--fc-page-w); margin-inline: auto; width: 100% }` — `app.css:976`.
  - الهيرو: `.fc-hero-inner { max-width: var(--fc-page-w); margin-inline: auto }` — `app.css:1152`.
  - **ممنوع `max-width` محلي للصفحة** — لو الهيرو والجسم ماخدوش نفس القيمة، النص بيخرج عن الخط.
- **الهيرو الموحّد (النمط الأغلب — مستخدم في 35+ شاشة):** مثال حيّ `Trailers.razor:12-43` و `Vehicles.razor:11-45`:
  ```razor
  <div class="fc-hero">
      <div class="fc-hero-inner">
          <div class="fc-page-head">
              <div>
                  <div class="fc-crumb">البيانات الأساسية · XXX</div>
                  <h1 class="fc-hero-title">XXX</h1>
                  <p class="fc-hero-sub">وصف مختصر</p>
              </div>
              <div class="fc-head-actions">
                  <MudIconButton Icon="@Icons.Material.Filled.Refresh" title="تحديث" OnClick="LoadAsync" Style="color:#9FB4CC" />
                  @if (CanManage) { <MudButton Variant="Variant.Filled" Color="Color.Secondary" StartIcon="@Icons.Material.Filled.Add" OnClick="OpenAdd">XXX جديد</MudButton> }
              </div>
          </div>
          <div class="fc-kpis fc-kpis-glass">
              <div class="fc-kpi">
                  <div class="fc-kpi-ic fc-kpi-blue"><svg …/></div>
                  <div><div class="fc-kpi-num">@n</div><div class="fc-kpi-lb">العنوان</div></div>
              </div>
          </div>
      </div>
  </div>
  <div class="fc-page"> … الجسم … </div>
  ```
  - كلاسات الهيرو والـKPIs كلها **عامة في `app.css`** (`fc-crumb` 978 · `fc-page-head` 979 · `fc-hero-title` 1154 · `fc-hero-sub` 1155 · `fc-head-actions` 982 · `fc-kpis`/`fc-kpi`/`fc-kpi-ic`/`fc-kpi-blue|green|red|gold`/`fc-kpi-num`/`fc-kpi-lb` 985-999 · `fc-kpis-glass` 1162-1171) ⇒ **مفيش CSS محلي مطلوب**.
  - ألوان الـKPI الجاهزة: `fc-kpi-blue` · `fc-kpi-green` · `fc-kpi-gold` · `fc-kpi-red` (مش `slate` ولا أي لون خارج القائمة).
  - `fc-kpis-glass` بيستخدم `flex: 1 1 190px` فيستوعب 3 أو 4 أو 5 كروت، وعند `900px` بيتحوّل لعمودين (`app.css:1124-1128`).
- **النمط التاني — «نمط القوائم/المستندات» (10 شاشات): `hero-wrapper` + `fc-hero fc-hero-slim` + `doc-hero-row` (`.doc-hero-left` + `.doc-hero-right`/`.doc-hero-chip`) + `doc-kpis-row`/`doc-kpi` + `.doc-body` + **FAB عائم للإنشاء** (`Services` · `PaymentMethods` · `Documents` · `PriceLists` · `TripTypes` · `Audit` · **`PaymentTerms`**).
  - **⚠️ التصحيح الأهم:** شاشات عيلة `Services`/`PaymentMethods` (شروط الدفع · طرق الدفع · الخدمات · قوائم الأسعار) بتاخد **النمط التاني**، والإنشاء فيها **دايمًا FAB** — مفيش زر «إضافة» جوه الهيرو.
  - **قبل ما تقرر نمط أي شاشة: قلّد الشاشة الشقيقة اللي المستخدم بيشاور عليها بالحرف** (نفس الأب المفتوح · نفس الكلاسات · نفس الـ`<style>` المحلي · نفس الـ`@code`). ممنوع تختار نمط «الأكثر انتشارًا» من عندك — ده اللي خلّى المحاولة الأولى مرفوضة.
  - التفاصيل الكاملة (ماركب + CSS + نقاط التجاوب + FAB): **`docs/FIELD-DESIGN.md` قسم 7** — ومثال حيّ: `Services.razor`.

### اللى اتعمل بالظبط

> ⚠️ **الجدول ده كان المرحلة الأولى (نمط `fc-page-head` + زر جوه الهيرو) واتلغى** — النسخة النهائية تحت.

| قبل | بعد |
|---|---|
| هوية الهيرو: `fc-crumb` مكتوب يدويًا بـ`pt-` | `fc-crumb` العام (لون ذهبي `--fc-gold`) |
| `.pt-hero` كارت أزرق داخل الصفحة | `.fc-hero` شريط كحلي كامل العرض + جريد خلفي (app.css) |
| `.pt-kpi` شفاف أبيض جوه الهيرو | `.fc-kpi` زجاجي معياري + أيقونات `fc-kpi-blue/green/gold/red` |
| `.pt-shell` بـ`max-width: 1320px` | `.fc-page` بـ`--fc-page-w` = 1560px (نفس عرض الهيرو) |
| `pt-hero-btn` مخصص | `MudButton` + `MudIconButton` جوه `fc-head-actions` (زي `Trailers`/`Vehicles`) |
| `@media 1024px` للـKPIs | قواعد الـKPIs العامة (900px) + `900px` للـtoolbar/البحث (زي `PaymentMethods:1475`) |
| شريحة تاريخ `pt-chip` | اتشالت — الهيرو المعياري مافيهوش شريحة تاريخ (موجودة بس في نمط `doc-hero-chip`) |

### ✅ النسخة النهائية (نمط `Services` بالحرف + FAB)

| قبل | بعد |
|---|---|
| `.pt-shell` (1320px) | `.hero-wrapper` + `.doc-body` (الاتنين `max-width: var(--fc-page-w, 1440px)`) |
| `.pt-hero` كارت أزرق محلي | `.hero-wrapper` › `.fc-hero.fc-hero-slim` › `.fc-hero-inner` › `.doc-hero-row` |
| `pt-crumb` يدوي | `.doc-hero-crumb` + أيقونة SVG 12px (زي الخدمات) |
| شريحة تاريخ `pt-chip` (اتشالت ثم رجعت) | `.doc-hero-chip` — رجعت بنفس شكل الخدمات (`rgba(255,255,255,.08)` + حدود `.14`) |
| `.pt-kpis` / `.pt-kpi` | `.doc-kpis-row` (`repeat(4, 1fr)`) + `.doc-kpi` ×4 (blue · green · amber · red) |
| أيقونات KPI بـSVG يدوي | `<MudIcon Icon="@Icons.Material.Filled.X" Size="Size.Small"/>` جوه `.doc-kpi-icon` |
| «شرط دفع جديد» + «تحديث» جوه الهيرو | **اتشالوا** — الهيرو بلا أي زر، وكل الإجراءات في **الـFAB** |
| — (جديد) | `.fc-fab-scrim` + `.fc-fab-container` › `.fc-fab-menu` («شرط دفع جديد» · «تحديث البيانات») + `.fc-fab-main.fc-glow-gold` (60px · نبضة برتقالية · دوران 135°) |
| `@media 1024px` | `900px` (KPIs عمودين) · `680px` (هيرو/جسم/FAB) · `480px` (KPIs) |

- **اتشال من CSS:** `.pt-shell` · `.pt-hero*` · `.pt-chip` · `.pt-kpis` · `.pt-kpi*` · `.pt-ic-*` (45 سطر) — واستُبدلوا بـ**قسم جديد (~300 سطر)** فيه `hero-wrapper` · `doc-body` · `doc-hero-*` · `doc-kpi*` · `fc-fab-*` · `fc-glow-gold` **منسوخين من `Services.razor` بالحرف**.
- **مهم:** كلاسات `doc-*` و`fc-glow-gold` **مش** في `app.css` ⇒ لازم تتنسخ محليًا جوه `<style>`. أما `fc-fab-*` فموجودة في `app.css:2262-2358` (نسخة 56px بسيطة) بس **النسخة المحلية بتغلبها** (الـ`<style>` بتاع الصفحة بييجي بعد `app.css`) ⇒ انسخ المحلية زي `Services`/`PaymentMethods`.
- **`@code` اتزاد:** `_fabOpen` + `ToggleFab()` + `HandleReload()` (يقفل القايمة → `LoadAsync` → `Snackbar.Add("تم تحديث البيانات")`) + `HandleOpenCreate()` (يقفل → `Task.Delay(180)` → `OpenAdd()`).
- **اللي اتساب زي ما هو:** الـtoolbar · البحث · الفلاتر · الحالات · الجدول · المودال · معيار الحقول `.field-*` — كله ببادئة `pt-` وزي ما هو.
- **اتأكد إن:** توازن الوسوم، صفر كلاسات قديمة (`pt-shell|pt-hero|pt-kpis|pt-kpi|pt-ic-|pt-chip|pt-crumb`)، والبناء 0 أخطاء من `PaymentTerms.razor`.

### ⚠️ الدرس (محدَّث بعد التصحيح)

1. **مش «الهيرو المعياري» — «هيرو الشاشة الشقيقة»:** قبل أي شاشة حدّد عيلتها: عيلة `Services`/`PaymentMethods` (`doc-hero` + FAB) ولا عيلة `Trailers`/`Vehicles` (`fc-page-head` + أزرار في الهيرو). لو مفيش يقين **اسأل المستخدم** — ما تختارش لوحدك (المحاولة الأولى اتلغت عشان كده).
2. **زر الإنشاء في العيلة الأولى = FAB عائم** (أسفل-يسار · `fc-glow-gold`) — مش زر جوه الهيرو ولا في الـtoolbar.
3. **مش كل الأنماط «عامة»:** `fc-hero`/`fc-page`/`fc-kpis` عامة في `app.css`، لكن `hero-wrapper` · `doc-body` · `doc-hero-*` · `doc-kpi*` · `fc-glow-gold` **محلية ولازم تنسخها** من الشاشة المرجعية.
4. **عرض واحد للصفحة:** `--fc-page-w` — الهيرو (`.hero-wrapper`) والجسم (`.doc-body`) بنفس القيمة، ومفيش `max-width` محلي من عندك.


## 19) ✅ 2026-09-24 — الفواتير: ترحيل من النمط التاني للنمط الأول (هيرو `doc-hero` + FAB)

**الملف:** `src/FastCom.Client/Pages/Invoices.razor` (3284 سطر بعد التغيير).
**البناء:** `FastCom.Client` ✅ **Build succeeded — 0 Warning(s) · 0 Error(s)**.

### اللي اتغير (بالظبط)

| قبل | بعد |
|---|---|
| `fc-hero` › `fc-page-head` › `fc-crumb` + `fc-head-actions` (زر «فاتورة جديدة» + تحديث **جوه** الهيرو) | `.hero-wrapper` › `.fc-hero.fc-hero-slim` › `.fc-hero-inner` › `.doc-hero-row` (`.doc-hero-left` + `.doc-hero-right`) — **الهيرو بلا أي زر** |
| شريحة/شريحتان في `doc-hero-right` | `.doc-hero-chip` تاريخ اليوم + `.doc-hero-chip.doc-chip-warn` «متأخرة: N» **شرطية** على `Overdue.Any()` |
| `fc-kpis fc-kpis-glass` + `fc-kpi` ×4 | `.doc-kpis-row` + `.doc-kpi doc-kpi-{blue,red,amber,green}` ×4 (MudIcon) — **موجودة بس جوه `@if (CanView)`** |
| جسم الصفحة `.inv-body` (كان `max-width: 1400px` محلي) | `.doc-body` + اتفضّى كله من CSS القديم (0 occurrence) |
| مفيش FAB | `@if (CanCreate && !_loading)` › `.fc-fab-scrim` (لو مفتوح) + `.fc-fab-container` › `.fc-fab-menu` («فاتورة جديدة» → `HandleOpenCreate` · «تحديث البيانات» → `HandleReload`) + `.fc-fab-main.fc-glow-gold` — **مكانه بعد `</div>` بتاع `.doc-body` وقبل `@* MODAL *@`** |
| — | `@code`: `bool _fabOpen` · `ToggleFab()` · `HandleReload()` (يقفل → `Reload()` → `Snackbar.Add("تم تحديث البيانات", Severity.Success)`) · `HandleOpenCreate()` (يقفل → `Task.Delay(180)` → `New()`) — **`New()`/`Reload()` هما الإجراءات القديمة بتاعة الشاشة، متغيّرت** |
| `<style>` بالكلاسات القديمة | قسم CSS جديد منسوخ من `PaymentTerms.razor`/`Services.razor`: `.hero-wrapper` · `.doc-body` · `.doc-hero-*` (+`.doc-chip-warn`) · `.doc-kpis-row` · `.doc-kpi*` · `fc-fab-*` المحلية + `.fc-glow-gold` مع `@@keyframes fc-gold-pulse`/`fc-ring-expand` · `@@media` عند **900 / 680 / 480px** |

### اللي اتساب زي ما هو (مهم)

الـtoolbar · البحث · الفلاتر · `inv-card` الجدول · مودال التفاصيل (`_detailOpen`) · مودال الإنشاء/التعديل (`_modalOpen`) · مودال الحذف · معيار الحقول `.field-*` · كل دوال `@code` القديمة (`LoadAsync`/`New`/`Save`/`Delete`/`Close`) — **كله بالبادئة `inv-` وملمسوش**. كمان `.inv-state-box` اتسابت زي ما هي.

### التحقق

- `inv-body` = 0 · `fc-kpis` = 0 · `fc-kpi-ic` = 0 · `fc-crumb|fc-page-head|fc-head-actions` = 0 · `doc-kpi` = 51 · `<div` 198 = `</div>` 198 (موازنة) · `hero-wrapper` 8 · `doc-body` 6 · `fc-fab-container` 4.
- البناء: **Build succeeded · 0 Warning(s) · 0 Error(s)** (`dotnet build src\FastCom.Client\FastCom.Client.csproj`).

### ⚠️ الدرس

1. **قبل ما تعدّل شاشة: اقرأ الشقيقة المرجعية (`Services.razor`) أول حاجة** — الماركب بالترتيب الإلزامي (wrapper → hero → inner → row → KPIs) بيتكرر حرفًا، والـCSS المحلي **لازم يتنسخ مش يُكتب من الذاكرة**.
2. **الـ`@code` ما يتغيّرش** — الشاشة ليها إجراءات جاهزة (`New`/`Reload`)؛ الـFAB handlers بتلفّها مع إغلاق القايمة و`Task.Delay(180)` بس. ممنوع تسمّي دوال جديدة من غير سبب.
3. **`CanCreate` مش `CanManage`** — الفارق في اسم صلاحية الشاشة؛ شروط §7.2 بتقول «صلاحية الإنشاء بتاعة الشاشة».
4. **`@@media`/`@@keyframes`** جوه `<style>` بتاع `.razor`.
5. **الموازنة بتتأكد بالأرقام مش بالعين:** عدّ `<div` مقابل `</div>` قبل ما تعتبر خلص.

---

## 20) تقسيم الفواتير: قائمة + صفحة إنشاء/تعديل مستقلة (2026-09-26)

**الطلب:** قسّم شاشة الفواتير لشاشتين (قائمة + إضافة/تعديل) بدل الدايلوج · وارجعلنا بحث عن العميل جوه الحقل.

| قبل | بعد |
|---|---|
| الإنشاء/التعديل مودال جوّه `Invoices.razor` (`_modalOpen` — ~400 سطر) | **صفحة جديدة `InvoiceForm.razor`** — routes `/invoices/new` + `/invoices/new/{Id:long}` (نفس عيلة الـ15 `*Form`، الأشبه `SupplierInvoiceForm.razor`) |
| FAB «فاتورة جديدة» → `New()` يفتح المودال | FAB → `Nav.NavigateTo("/invoices/new")` |
| زر الصف «تعديل» → `Edit(i)` | → `Nav.NavigateTo($"/invoices/new/{i.InvoiceId}")` |
| `<select>` العميل بدون بحث | **حقل بحث `field-input` + `.field-dropdown`** (منسوخ من `BookingForm.razor:277-314` + `NormalizeArabic` — بحث بالاسم أو الكود) |
| هيرو المودال | `fc-hero › fc-page-head` (رجوع/حفظ) › `.fc-page › .fc-overlap › .fc-card` — قلب `SupplierInvoiceForm` بالحرف |

**اللي اتشال من `Invoices.razor`** (22,491 حرف ماركب + 4,652 حرف `@code`): بلوك المودال بالكامل · `New`/`Edit`/`Close`/`OnCustomerChanged`/`OnPickOp`/`OnManualService`/`AddManual`/`OnLineTax`/`SaveAsync` · `_modalOpen`/`_editId`/`_f`/`_manual`/`_pickOp`/`_ops`/`_services` · كلاسات `Form`/`Svc`/`OpRef` + `ToForm()` + `FormTotal` + `ParseInt`.

**اللي فضل زي ما هو:** مودال التفاصيل (`_detailOpen`) · الإشعار دائن (`_creditOpen`) · التسليم · الحذف · القائمة/الفلاتر/`inv-card` · معيار `.field-*` · `LoadAsync`/`Reload`/`Delete`/`SetStatus` — ومودال الإشعار دائن لسه بيستخدم `ParseDec` (علشان كده اتحتفظ بيه، و`CloseCredit`/`CloseSend`/`CloseDetail` سليمة).

**`InvoiceForm.razor` (~800 سطر):**
- صلاحيات: `CanSave = _id is null ? INVOICE.CREATE : INVOICE.EDIT` · `Locked = _status is not ("Draft" or "Approved")` (نفس شرط زر الصف) — وقت `Locked`: تنبيه + حقول `disabled` + زر «حفظ» مختفي.
- endpoints: `api/invoices/{customers|services|tax-rates|billable-operations|operations/{id}/lines}` + GET/POST/PUT `api/invoices`.
- CSS محلي `<style>`: نواة `.field-*` منسوخة من `BookingForm` (label/input/select/prefix/dropdown/hint/err) + `.invf-*` صغيرون — باقي الصفحة كله كلاسات **عام** في `app.css` (`fc-hero`/`fc-page`/`fc-overlap`/`fc-card`/`fc-form-grid`/`fc-stats`/`fc-table`).

**التحقق:** `InvoiceForm` div 58/58 · `Invoices` div 155/155 · صفر بواقي (`_modalOpen`/`SaveAsync`/`Edit(i)`/`@onclick="New"`/`FormTotal`/`OpRef`/`ToForm` كلهم 0) · **Build succeeded · 0 Error(s)** (التحذيرات الأربعة كلها قديمة في ملفات تانية).

**⚠️ الدرس:** الشاشة اتكتبت على أجزاء بعلامة `@*__APPEND__*@` — والجزء المتبقي من أول chunk (التواريخ + الملاحظات + إغلاق `.fc-form-grid`) **اتسرّب**، والبناء اكتشفه بـ`RZ9980: Unclosed tag 'div'`. **بعد أي تقسيم/إضافة جزئية: فعّل فحص الموازنة `<div`/`</div>` + بناء كامل فورًا.**

