🚛 فاست كوم — FastCom
نظام إدارة مكتب نقل الحاويات والموانئ
حجوزات · عمليات · رحلات · عهد · مصروفات · فواتير · تحصيل · خزينة · تقارير ربحية

.NET
Blazor
SQL Server
License

</div> 📖 نظرة عامة فاست كوم نظام ERP/تشغيلي متكامل لمكاتب نقل الحاويات اللي بتشتغل على الموانئ المصرية. النظام بيدير الدورة الكاملة من طلب العميل لحد تحصيل الفاتورة، مع تتبع دقيق للتكلفة والإيراد وصافي الربح لكل عملية ولكل عميل.
🔄 دورة العمل الأساسية
text

طلب العميل → حجز → حاويات → تسعير → عملية نقل → سائق/سيارة
→ عهد السائق → تنفيذ الرحلة → مصروفات → تسليم
→ إغلاق العملية → فاتورة العميل → إرسال الفاتورة → التحصيل
📊 التقارير المطلوبة
التقرير
تكلفة / إيراد / صافي ربح لكل عملية vw_OperationProfitability
ربحية كل عميل vw_CustomerBalanceSummary
أداء السائقين والسيارات vw_TripSummary
العمليات المفتوحة والمتأخرة vw_DashboardOperations
العهد المفتوحة CustodyTransactions
الفواتير غير المحصّلة vw_CustomerInvoiceBalance
حركة النقدية CashTransactions
🛠️ التقنيات
الطبقة التقنية
الواجهة Blazor WebAssembly (.NET 8) + MudBlazor 7.15
الـ API ASP.NET Core 8 Web API
قاعدة البيانات SQL Server 2025 (monsterasp.net)
ORM EF Core 8.0.11 + Scaffolding (Database-First)
الهوية ASP.NET Core Identity (int keys) + JWT Bearer
اللغة العربية RTL من أول يوم (خط Cairo)
الاستضافة IIS 10 · نفس الموقع: / = WASM · /api = API
🎨 الهوية البصرية
Primary #0B56DF أزرق
Secondary #FF8015 برتقالي
الخط Cairo (Google Fonts) 300–800
الاتجاه RTL — dir="rtl" + MudRTLProvider
🏗️ البنية
text

FastCom/
├── src/
│ ├── FastCom.Client/ # Blazor WASM — الواجهة
│ │ ├── Auth/ # TokenStore · AuthService · JwtAuthStateProvider · AuthMessageHandler
│ │ ├── Pages/ # Login · Home · Health · Search
│ │ ├── Shared/Layout/ # MainLayout · NavMenu · BlankLayout
│ │ ├── Shared/componant/ # GlobalSearch · RedirectToLogin
│ │ ├── Services/ # CompanyService · HealthService
│ │ └── wwwroot/ # index.html · css/app.css · js/fastcom-storage.js
│ │
│ ├── FastCom.Server/ # ASP.NET Core Web API
│ │ ├── Auth/ # TokenService · PermissionService
│ │ │ # PermissionRequirement/Handler/PolicyProvider
│ │ └── Controllers/
│ │ ├── Auth/ # AuthController · AuthDtos
│ │ ├── BrandingController.cs
│ │ ├── HealthController.cs
│ │ ├── SearchController.cs
│ │ └── DiagController.cs (Development فقط)
│ │
│ ├── FastCom.Infrastructure/ # EF Core · DbContext · Identity
│ │ └── Persistence/
│ │ ├── FastComDbContext.cs (partial — Audit + Soft Delete)
│ │ ├── ScaffoldedDbContext.cs (61 DbSet + Fluent API)
│ │ └── Generated/ (61 Entity مولّدة)
│ │
│ ├── FastCom.Application/ # Use Cases (⏳ Step 4+)
│ ├── FastCom.Domain/ # الكيانات الأساسية
│ └── FastCom.Shared/ # DTOs مشتركة
│
├── sql/ # FastCom-schema.sql (المرجع الأساسي)
├── tools/ # merge-dbcontext.ps1
├── docs/ # التوثيق
├── FastCom.sln # 6 مشاريع
├── global.json # SDK 8.0.420
└── MEMORY.md # 🔴 ملف الذاكرة الرئيسي
🚀 التشغيل
المتطلبات
.NET SDK 8.0.420 أو أحدث (مثبّت في
global.json
)
SQL Server وصول لـ db65922.public.databaseasp.net
المحرر VS Code (مش Visual Studio)

استنساخ
Bash
git clone https://github.com/kazayza/FastComm.git
cd FastComm
2) 🔐 الأسرار — user-secrets
🔴 مافيش أسرار في الريبو. appsettings.json فيه placeholders بس.

PowerShell

cd src\FastCom.Server

dotnet user-secrets init

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=db65922.public.databaseasp.net,1433;Database=db65922;User Id=db65922;Password=<كلمة المرور>;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True"

dotnet user-secrets set "Jwt:Key" "<مفتاح 80 حرف>"
توليد مفتاح JWT:

PowerShell

-join ((65..90) + (97..122) + (48..57) | Get-Random -Count 80 | ForEach-Object {[char]$_})
⚠️ ,1433 إلزامي في اسم السيرفر — بدونه SqlClient بيحاول UDP 1434 (SQL Browser)
ولو محجوب بيطلع error 53 / Named Pipes Provider error 40.

بناء وتشغيل
PowerShell
dotnet restore
dotnet build

🔴 شغّل من غير --launch-profile (أي اسم غلط = يقع صامت على بورت 5000)
dotnet run --project src\FastCom.Server
4) افتح
التطبيق https://localhost:7001
Swagger https://localhost:7001/swagger
حالة النظام https://localhost:7001/api/health
🔑 أول مستخدم
text

اسم المستخدم : admin
كلمة المرور : (بتتعيّن من أدوات المطور في شاشة الدخول — Development فقط)
الدور : مدير النظام (107 صلاحية)
في شاشة الدخول → "نسيت كلمة المرور" → أدوات المطور:

POST /api/auth/seed-admin — إنشاء أول مدير
POST /api/auth/reset-password — إعادة تعيين باسورد + فتح الحساب
🔴 الاتنين Development بس — في الإنتاج بيرجعوا 404.

🗄️ قاعدة البيانات
العنصر العدد
جداول 68
Views 7
Triggers 8
Stored Procedures 1 (usp_GetNextNumber)
صلاحيات 107
أدوار 11
الـ Views
text

vw_OperationProfitability vw_CustomerInvoiceBalance
vw_CustomerStatement vw_TripSummary
vw_CustomerBalanceSummary vw_DashboardOperations
vw_DashboardFleet
الأدوار الـ 11
text

ADMIN · OPSMGR · OPSEMP · FINMGR · ACCT · HRMGR
FLTMGR · FLEET · DATAENT · VIEWER · PORTAL
الصلاحيات (107) حسب الوحدة
الوحدة الوحدة الوحدة
INVOICE 10 OPERATION 8 BOOKING 7
TRIP 6 CUSTOMER 6 CUSTODY 6
SUPPLIERINVOICE 5 PRICING 5 EXPENSE 5
TREASURY 4 SUPPLIER 4 REPORT 4
PAYMENT 4 MAINTENANCE 4 FLEET 4
EMPLOYEE 4 DRIVER 4 SUPPLIERPAYMENT 3
DOCUMENT 3 USER 2 SETTINGS 2
MASTERDATA 2 ROLE · PORTAL · NOTIFICATION · DASHBOARD · AUDIT 1
🔧 أدوات قاعدة البيانات
تنفيذ SQL SSMS فقط — لوحة تحكم الاستضافة بتبلع الأخطاء
الاتصال Options >> → Connection Properties → ✅ Encrypt + ✅ Trust server certificate
text

Server = db65922.public.databaseasp.net,1433
Database = db65922
🔌 الـ API
api/auth
Method Endpoint الوصف
POST /api/auth/login تسجيل الدخول → JWT + الأدوار + الصلاحيات
GET /api/auth/me بيانات المستخدم الحالي + الصلاحيات
POST /api/auth/logout تسجيل الخروج (Audit)
POST /api/auth/seed-admin 🔧 إنشاء أول مدير — Development
POST /api/auth/reset-password 🔧 إعادة تعيين باسورد — Development
api/branding
Method Endpoint الوصف
GET /api/branding/company بيانات الشركة من CompanyProfile
api/search
Method Endpoint الوصف
GET /api/search?q=&take= بحث عام في 7 وحدات
الوحدات: عميل · حجز · عملية · فاتورة · رحلة · حاوية · سيارة

api/health · api/diag
Method Endpoint الوصف
GET /api/health حالة النظام + عدّادات قاعدة البيانات
GET /api/diag/connection 🔧 تشخيص الاتصال — Development
GET /api/diag/try-servers 🔧 تجربة 7 صيغ للسيرفر — Development
🔐 الأمان

| | |
|---|
| التحقق | JWT Bearer — HMAC-SHA256 · 8 ساعات · ClockSkew دقيقة |
| الصلاحيات | 107 كود في AppPermissions — بتتجلب من /api/auth/me مش من الـ JWT (عشان حجم التوكن) |
| Server-side إلزامي | الـ UI مش نقطة الحماية الوحيدة |
| الأسرار | user-secrets (Development) + appsettings.Production.json (gitignored) |
| SQL | SqlParameter دائمًا — مافيش string concatenation |
| Lockout | 5 محاولات فاشلة → قفل 15 دقيقة |

🔴 ملاحظة مهمة على الـ JWT
الـ 107 صلاحية مش بتتحط في التوكن — ده كان هيخليه 4–5 KB (كبير على HTTP Header).
بدل كده: الأدوار بس في التوكن، والصلاحيات بتتجلب من /api/auth/me والـ Client بيخزّنها.

📋 خطة البناء

الخطوة الحالة
0 مراجعة + اختيار التقنية ✅
1 قاعدة البيانات ✅
2 Solution + Scaffold + Build ✅
3 الواجهة + Shell + Login ✅ خلصت
4 المستخدمين والأدوار والصلاحيات ⬅ دلوقتي
5 البيانات الأساسية (عملاء · موردين · خدمات) ⏳
6 الحجوزات ⏳
7 العمليات + الرحلات + الحاويات ⏳
8 العهد + المصروفات ⏳
9 الفواتير + التحصيل ⏳
10 الخزينة + كشف حساب العميل ⏳
11 المستندات + الإشعارات ⏳
12 التقارير + الـ Dashboards ⏳
📚 التوثيق
الملف المحتوى

MEMORY.md
🔴 ملف الذاكرة الرئيسي — الحالة + سجل الأخطاء + القرارات

ARCHITECTURE.md
البنية المعمارية + الـ Patterns

DATABASE.md
خريطة قاعدة البيانات الكاملة

API.md
توثيق الـ Endpoints

DECISIONS.md
سجل القرارات D1–D14 + X1–X12
📄 الترخيص
مشروع خاص — كل الحقوق محفوظة.

<div align="center"> فاست كوم · نظام إدارة مكتب نقل الحاويات والموانئ</div>