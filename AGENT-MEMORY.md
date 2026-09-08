# 🧠 FastCom — بنك ذاكرة المهندس (Agent Memory Bank)

> **اقرأه قبل أي تعديل.** مرجع سريع للمهندس + الـ AI. التفاصيل اليومية والتشغيلية في `MEMORY.md` (مملوك للمشروع).
> آخر تحديث: 2026-09-08

---

## 1) بطاقة المشروع
- نظام ERP/تشغيلي لمكاتب نقل **الحاويات والموانئ المصرية**: حجوزات → عمليات → رحلات → عهد → مصروفات → فواتير → تحصيل → خزينة → تقارير ربحية + بوابة عميل.
- Repo: `https://github.com/kazayza/FastComm` — الفرع `main`.
- **ليس جاهزًا للإنتاج بعد** — مرحلة Beta/تدقيق. خطة الإصلاح في `docs/REMEDIATION-PLAN.md`.

## 2) Stack
| | |
|---|---|
| الواجهة | Blazor WebAssembly (.NET 8) + MudBlazor 7.15 — عربي RTL + خط Cairo |
| الـ API | ASP.NET Core 8 Web API + Serilog + QuestPDF (Community) + ClosedXML |
| قاعدة البيانات | SQL Server على monsterasp.net (`db65922`) — Database-First (Scaffold يدوي 61 Entity) |
| ORM | EF Core 8.0.11 (قراءة) + ADO.NET/`SqlClient` للتجميعات والـ SP |
| الحماية | ASP.NET Identity (**int keys**) + JWT (HMAC-SHA256) + صلاحيات 107 كود من DB بكاش 60 ثانية |
| الاستضافة | IIS 10 — `/` = WASM · `/api` = API |

## 3) البنية
```
src/FastCom.Domain      → IAuditableEntity (خفيفة جدًا)
src/FastCom.Application → ⚠️ تقريبًا فاضية (المنطق كله في الـ Controllers حاليًا)
src/FastCom.Infrastructure → ScaffoldedDbContext (partial) + Generated/ (61 Entity مولّدة) + Identity
src/FastCom.Shared      → DTOs مشتركة
src/FastCom.Server      → Controllers (29) + Auth/ + Services/
src/FastCom.Client      → Pages (28) + Shared/ + Auth/ + Services/
sql/FastCom-schema.sql → ⭐ المرجع الوحيد للـ Schema (155KB)
```

## 4) أوامر التشغيل من هنا
```powershell
# البيلد (SDK 8.0.420 مثبت)
dotnet build FastCom.sln -c Debug

# التشغيل مع الأسرار المحلية
dotnet user-secrets init --project src/FastCom.Server
dotnet user-secrets list --project src/FastCom.Server
```

## 5) 🔴 قواعد لا تتكسر
1. **أي كتابة متعددة الخطوات (أكتر من `SaveChangesAsync` أو أكتر من جدول) = لازم `BeginTransactionAsync` واحد.** — الوضع الحالي: صفر Transactions = أكبر Blockerr (Phase 1).
2. **كل Controller جديد = `[Authorize(Policy = "PERM:CODE")]`**؛ والفائضة `[AllowAnonymous]` صراحة. الـ FallbackPolicy مقفولة — لا تعتمد على أنها هتحمي لو سبتها.
3. **مفيش أسرار في أي ملف مcommitted**: user-secrets (Dev) + `appsettings.Production.json` (gitignored، ما كتبتش بعد!). مفتاح JWT ≥ 64 حرف.
4. **كل SQL بـ SqlParameter** — ممنوع concatenation (النظام ملتزم — حافظ عليه).
5. **ما تعدّل entity مولّدة ولا `ScaffoldedDbContext` يدويًا** — الجديد عبر scaffold.
6. **الأرقام والمجاميع والأرصدة بيحسبها DB Triggers** (`trg_*` + `usp_GetNextNumber` بـ sp_getapplock) — لا تعيد حسابها في الكود ولا تنسى أنها مش في الذاكرة.
7. **انسخ التحقق: اعمل Build بنفسك** (لو Mathمتش SDK قول بصراحة) — لا تسلّم كود مش مترجم.
8. **Audit**: `CreatedBy/UpdatedBy` لسه `null` (فيه TODO في `FastComDbContext.ApplyAuditFields`) — لا تدّعي إن المحاسبة مسجلة.

## 6) الوضع الحالي (الآخر المؤكد: 2026-09-06)
- Steps 4→11 + لوحة المؤشرات: **مرقونة** — الـ Build عدّى بعد 4 دورات إصلاح.
- الجاي في المشروع: التقارير (موجودة بالفعل `ReportsController` + `Reports.razor`) + إصلاح GlobalSearch + الإعدادات.
- **Blockers للمشروع**: بدون اختبارات، بدون Git محفوظ (بيتحل النهارده)، بدون Transactions، بدون AuditLogs كتابة، التوثيق المرجعى (ARCHITECTURE/DATABASE/API/DECISIONS) مش موجود فِعليًا، `appsettings.Production.json` غير مكتوب، CI صفر.

## 7) مرجع — أين الحقيقة
- `sql/FastCom-schema.sql` (155 KB) = المرجع.
- MEMORY.md بيذكر مسارات غي مقة هنا: `uploads/...` · `reviews/...` · `design/...` — **استفسر من المستخدم** لو نحتاجها (لا تخمّن).
- في `sql/` 5 ملفات schema (شق في شوك) — القرار في خطة الإصلاح: توحيد canonical واحد + Migrations.

## 8) مفاتيح قرارات سليمة (لا تعكسها)
- صلاحيات 107 كود في JWT بدون؟ لا — الأدوار فقط في التوكن، الصلاحيات من `/api/auth/me`.
- `seed-admin` / `reset-password` → 404 في Production.
- توكن بوابة العميل: SHA-256 مُخزّن فقط + 6 فحوص + سجل وصول.
- Portal `EmptyLayout` مستقل لأي Link بال Token.

## 9) مرجع السرfacts
- بصفتي مهندس: التقييم الكامل في مناقشة 2026-09-08 (نقاط قوة / Blockers B1-B6 / Roadmap) — ملخصه في `docs/REMEDIATION-PLAN.md`.
- المهارات المقترحة + كيفية توظيفها في المشروع: `docs/TOOLKIT-SKILLS.md`.