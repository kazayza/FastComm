/* =====================================================================================
   FastCom — Verify Schema v2
   شغّل السكريبت ده في SSMS بعد ما تكون متصل على db65922
   ===================================================================================== */

SET NOCOUNT ON;

PRINT N'';
PRINT N'========================================================';
PRINT N'   FastCom Schema v2 — تقرير التحقق';
PRINT N'========================================================';

/* ---------- 1) قاعدة البيانات ---------- */
SELECT
    DB_NAME()                                       AS [قاعدة البيانات الحالية],
    SUSER_SNAME()                                   AS [المستخدم],
    IS_ROLEMEMBER('db_owner')                       AS [db_owner؟ 1=أيوه],
    IS_ROLEMEMBER('db_ddladmin')                    AS [db_ddladmin؟ 1=أيوه],
    (SELECT COUNT(*) FROM sys.tables)               AS [عدد الجداول],
    (SELECT COUNT(*) FROM sys.views WHERE is_ms_shipped = 0)  AS [عدد الـ Views],
    (SELECT COUNT(*) FROM sys.triggers WHERE parent_id > 0)   AS [عدد الـ Triggers],
    (SELECT COUNT(*) FROM sys.procedures)           AS [عدد الـ Procedures],
    (SELECT COUNT(*) FROM sys.indexes WHERE name IS NOT NULL AND is_primary_key = 0 AND is_unique_constraint = 0) AS [عدد الـ Indexes],
    (SELECT COUNT(*) FROM sys.foreign_keys)         AS [عدد الـ Foreign Keys];

/* ---------- 2) الـ Seed Data ---------- */
PRINT N'';
PRINT N'--- الـ Seed Data ---';
SELECT
    (SELECT COUNT(*) FROM Branches)          AS [الفروع],
    (SELECT COUNT(*) FROM Departments)       AS [الأقسام],
    (SELECT COUNT(*) FROM JobTitles)         AS [المسميات الوظيفية],
    (SELECT COUNT(*) FROM Ports)             AS [الموانئ],
    (SELECT COUNT(*) FROM Destinations)      AS [الوجهات],
    (SELECT COUNT(*) FROM TripTypes)         AS [أنواع الرحلات],
    (SELECT COUNT(*) FROM Services)          AS [الخدمات],
    (SELECT COUNT(*) FROM ContainerTypes)    AS [أنواع الحاويات],
    (SELECT COUNT(*) FROM ExpenseTypes)      AS [أنواع المصروفات],
    (SELECT COUNT(*) FROM DocumentTypes)     AS [أنواع المستندات],
    (SELECT COUNT(*) FROM TaxRates)          AS [الضرائب],
    (SELECT COUNT(*) FROM PaymentTerms)      AS [شروط الدفع],
    (SELECT COUNT(*) FROM PaymentMethods)    AS [طرق الدفع],
    (SELECT COUNT(*) FROM OperationStatuses) AS [حالات العملية],
    (SELECT COUNT(*) FROM CashBoxes)         AS [الخزائن],
    (SELECT COUNT(*) FROM NumberSequences)   AS [سلاسل الترقيم],
    (SELECT COUNT(*) FROM CompanyProfile)    AS [بيانات الشركة];

/* ---------- 3) الصلاحيات والأدوار ---------- */
PRINT N'';
PRINT N'--- الصلاحيات والأدوار ---';
SELECT
    (SELECT COUNT(*) FROM AppPermissions)    AS [عدد الصلاحيات],
    (SELECT COUNT(*) FROM AspNetRoles)       AS [عدد الأدوار],
    (SELECT COUNT(*) FROM AppRolePermissions) AS [توزيعات الصلاحيات],
    (SELECT COUNT(*) FROM AspNetUsers)       AS [عدد المستخدمين];

PRINT N'';
PRINT N'--- توزيع الصلاحيات على كل دور ---';
SELECT
    r.RoleCode                AS [كود الدور],
    r.NameAr                  AS [الدور],
    COUNT(rp.PermissionId)    AS [عدد الصلاحيات]
FROM AspNetRoles r
LEFT JOIN AppRolePermissions rp ON rp.RoleId = r.Id
GROUP BY r.RoleCode, r.NameAr, r.Id
ORDER BY COUNT(rp.PermissionId) DESC;

/* ---------- 4) أنواع الضرائب ---------- */
PRINT N'';
PRINT N'--- أنواع الضرائب (⚠️ محاسبك لازم يؤكد) ---';
SELECT Code AS [الكود], NameAr AS [الاسم], Rate AS [النسبة],
       TaxKind AS [النوع], IsDeductible AS [قابلة للخصم؟]
FROM TaxRates ORDER BY TaxRateId;

/* ---------- 5) سلاسل الترقيم ---------- */
PRINT N'';
PRINT N'--- سلاسل الترقيم ---';
SELECT DocumentType AS [المستند], Prefix AS [البادئة], Year AS [السنة],
       NumberLength AS [الطول], ResetPeriod AS [إعادة التصفير]
FROM NumberSequences ORDER BY DocumentType;

/* ---------- 6) اختبار الترقيم ---------- */
PRINT N'';
PRINT N'--- اختبار usp_GetNextNumber (⚠️ هيزوّد العداد بـ 1) ---';
BEGIN TRY
    DECLARE @n1 nvarchar(40), @n2 nvarchar(40);
    EXEC usp_GetNextNumber 'BOOKING', NULL, @n1 OUTPUT;
    EXEC usp_GetNextNumber 'INVOICE', NULL, @n2 OUTPUT;
    PRINT N'  ✅ رقم حجز تجريبي  : ' + @n1;
    PRINT N'  ✅ رقم فاتورة تجريبي: ' + @n2;
END TRY
BEGIN CATCH
    PRINT N'  ❌ خطأ في الـ Procedure: ' + ERROR_MESSAGE();
END CATCH

/* ---------- 7) اختبار الـ Triggers ---------- */
PRINT N'';
PRINT N'--- اختبار الـ Triggers ---';
IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'trg_CustodyTransactions_Sync' AND is_disabled = 0)
    PRINT N'  ✅ trg_CustodyTransactions_Sync';
ELSE PRINT N'  ❌ trg_CustodyTransactions_Sync مش شغال';

IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'trg_OperationRevenueItems_Sync' AND is_disabled = 0)
    PRINT N'  ✅ trg_OperationRevenueItems_Sync';
ELSE PRINT N'  ❌ trg_OperationRevenueItems_Sync مش شغال';

IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'trg_PaymentAllocations_InvoiceSync' AND is_disabled = 0)
    PRINT N'  ✅ trg_PaymentAllocations_InvoiceSync';
ELSE PRINT N'  ❌ trg_PaymentAllocations_InvoiceSync مش شغال';

IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'trg_CashTransactions_BalanceSync' AND is_disabled = 0)
    PRINT N'  ✅ trg_CashTransactions_BalanceSync';
ELSE PRINT N'  ❌ trg_CashTransactions_BalanceSync مش شغال';

IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'trg_BookingContainerDetails_QtySync' AND is_disabled = 0)
    PRINT N'  ✅ trg_BookingContainerDetails_QtySync';
ELSE PRINT N'  ❌ trg_BookingContainerDetails_QtySync مش شغال';

/* ---------- 8) اختبار الـ Computed Columns ---------- */
PRINT N'';
PRINT N'--- الـ Computed Columns (PERSISTED) ---';
SELECT
    OBJECT_NAME(object_id) AS [الجدول],
    name                   AS [العمود],
    is_persisted           AS [PERSISTED؟]
FROM sys.computed_columns
WHERE OBJECT_NAME(object_id) IN ('InvoiceItems','OperationRevenueItems','SupplierInvoiceItems')
ORDER BY OBJECT_NAME(object_id), name;

/* ---------- 9) اختبار الـ Views ---------- */
PRINT N'';
PRINT N'--- اختبار الـ Views (SELECT من غير بيانات) ---';
BEGIN TRY SELECT TOP 0 * FROM vw_OperationProfitability;      PRINT N'  ✅ vw_OperationProfitability';      END TRY BEGIN CATCH PRINT N'  ❌ vw_OperationProfitability: ' + ERROR_MESSAGE(); END CATCH
BEGIN TRY SELECT TOP 0 * FROM vw_CustomerInvoiceBalance;      PRINT N'  ✅ vw_CustomerInvoiceBalance';      END TRY BEGIN CATCH PRINT N'  ❌ vw_CustomerInvoiceBalance: ' + ERROR_MESSAGE(); END CATCH
BEGIN TRY SELECT TOP 0 * FROM vw_CustomerStatement;           PRINT N'  ✅ vw_CustomerStatement';           END TRY BEGIN CATCH PRINT N'  ❌ vw_CustomerStatement: ' + ERROR_MESSAGE(); END CATCH
BEGIN TRY SELECT TOP 0 * FROM vw_CustomerBalanceSummary;      PRINT N'  ✅ vw_CustomerBalanceSummary';      END TRY BEGIN CATCH PRINT N'  ❌ vw_CustomerBalanceSummary: ' + ERROR_MESSAGE(); END CATCH
BEGIN TRY SELECT TOP 0 * FROM vw_TripSummary;                 PRINT N'  ✅ vw_TripSummary';                 END TRY BEGIN CATCH PRINT N'  ❌ vw_TripSummary: ' + ERROR_MESSAGE(); END CATCH
BEGIN TRY SELECT TOP 0 * FROM vw_DashboardOperations;         PRINT N'  ✅ vw_DashboardOperations';         END TRY BEGIN CATCH PRINT N'  ❌ vw_DashboardOperations: ' + ERROR_MESSAGE(); END CATCH
BEGIN TRY SELECT TOP 0 * FROM vw_DashboardFleet;              PRINT N'  ✅ vw_DashboardFleet';              END TRY BEGIN CATCH PRINT N'  ❌ vw_DashboardFleet: ' + ERROR_MESSAGE(); END CATCH

/* ---------- 10) القيم اللي لازم تتعبّى ---------- */
PRINT N'';
PRINT N'========================================================';
PRINT N'   ⚠️  قيم لازم تعدّلها قبل الإنتاج';
PRINT N'========================================================';
SELECT
    TaxNumber        AS [الرقم الضريبي — لازم الحقيقي],
    AddressAr        AS [العنوان — لازم مفصّل],
    LegalNameAr      AS [اسم الشركة]
FROM CompanyProfile;

PRINT N'';
PRINT N'✅ انتهى التقرير';
