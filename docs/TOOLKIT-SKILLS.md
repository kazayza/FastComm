# 🛠️ FastCom — دليل المهارات والتقنيات المقترحة (Toolkit & Skills)

> استراتيجية اختيار المهارات/الأدوات التي تخدم المشروع الآن (2026-09-08) حسب الأولوية.
> الهدف: تحويل المشروع من Beta إلى إنتاج موثوق — كل مهارة هنا مرتبطة بعائق حقيقي في `docs/REMEDIATION-PLAN.md`.

---

## 1) جدول الأولويات

| الأولوية | المهارة | كيف تخدم المشروع | الأدوات المختارة |
|---|---|---|---|
| 🔴 1 | **اختبارات التكامل (.NET)** | المشروع بدون أي اختبار — والمالية هي الخطر الأكبر | xUnit + `WebApplicationFactory` (يحاكي الـ Server كاملًا) |
| 🔴 2 | **Git + GitHub Actions** | حفظ التاريخ بعد إصلاح الـ Git + CI يمنع أي كود يكسر الـ build/test | `git` CLI + سكربت YAML في `.github/workflows` |
| 🔴 3 | **SQL Server / SSMS / T-SQL** | تحقق مطابقة الـ Schema + كتابة SP/Views للتجميع + مراجعة الـ Triggers | SSMS + سكربتات `sql/` |
| 🔴 4 | **Transactions (EF/ADO)** | أكبر Blockerr: صفر معاملات متعددة الخطوات حاليًا | `BeginTransactionAsync` على كل مسار CRUD متعدد |
| 🟠 5 | **جودة الكود (.editorconfig/Roslyn)** | منع الأخطاء من أول مرة وتوحيد الإسلوب | `Directory.Build.props` + analyzers |
| 🟠 6 | **أداء التجميعات (Dapper/Views)** | التقارير (كاب 2000 صف) بتتجمع في الذاكرة — تحتاج تحويل | View محفوظة + `[Keyless]` + Dapper |
| 🟠 7 | **تثبيت Identity/JWT** | JWT في localStorage بلا Security Stamp/Refresh/Revoke | Verification Stamp + Rate limit + Refresh token |
| 🟢 8 | **Ops: Serilog + Backup** | ليدف في opera و logs مفيدة | Serilog roll + خطة DR (شهري) |
| 🟢 9 | **PowerShell أتمتة** | Scaffold/جرد قاعدة البيانات/سكربتات فحص | PS scripts في `tools/` |

## 2) اختيارات محددة (بأسماء الحزم)

| الحاجة | المُختار | بديل مقبول |
|---|---|---|
| اختبارات | xUnit + WebApplicationFactory | NUnit |
| DB للـ Test (integration) | LocalDB أو Testcontainers SQL | **ليس** الاتصال بـ `db65922` في CI |
| Excel | ClosedXML 0.104.1 (مركّب بالفعل) | — |
| PDF | QuestPDF 2024.10.3 (مركّب) — راجع license للاستخدام التجاري | — |
| Email | MailKit 4.17 (مركّب) — ربط SMTP في config | — |
| CI | GitHub Actions (الـ repo عليه) | — |
| Schema-migration | EF Core Migrations بقرار معاك (بعد ضبط الـ schema) | scripts + diff |

## 3) كيف نستثمر كل مهارة فورًا

- **اختبارات**: نبدأ بأهم 4 مواقف مالية (دفعة→توزيع→فاتورة · إلغاء دفعة · نقل خزينة · الترقيم المتزامن) — تغطي كل الـ Blockers في الشرط.
- **CI**: أول push يلي حفظ البلسايد يضيف workflow `dotnet build + dotnet test`.
- **Transactions**: لف `Trips(14)` · `Invoices(9)` · `Operations(9)` · `Custodies(8)` · `Payments(4)` تحت «معاملة واحدة»، ويمكن review كل Path يدويًا.
- **A2 Audit**: تسجيل `IHttpContextAccessor` + ملء `CreatedBy/UpdatedBy` + كتابة `AuditLogs`.

## 3) مصاد البحث السريعة (عند الحاجة)

- EF Core 8 Transactions: `Microsoft.EntityFrameworkCore.DbContext.BeginTransactionAsync`
- Testcontainers SQL Server — أعد تركيب محلي أثناء الاختبار
- OWASP ASP.NET Core Cheat Sheet — للمراجعة الأمنية في Phase 2.5

---

> آخر تحديث: 2026-09-08 — هذه هي نسخة v1، تُحدَّث مع تنفيذ البـPlan.