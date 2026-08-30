/* =====================================================================================
   FastCom — تشخيص: إيه اللي اتعمل وإيه اللي ناقص
   =====================================================================================
   شغّل السكريبت ده في SSMS بعد ما تختار db65922
   وهيطلعلك 6 نتائج (6 tabs)
   ===================================================================================== */

SET NOCOUNT ON;

/* ---------- 1) إيه اللي موجود أصلًا؟ ---------- */
SELECT
    DB_NAME()                            AS [قاعدة البيانات],
    SUSER_SNAME()                        AS [المستخدم],
    IS_ROLEMEMBER('db_owner')            AS [db_owner],
    (SELECT COUNT(*) FROM sys.tables)    AS [جداول],
    (SELECT COUNT(*) FROM sys.views WHERE is_ms_shipped = 0) AS [Views],
    (SELECT COUNT(*) FROM sys.triggers WHERE parent_id > 0)  AS [Triggers],
    (SELECT COUNT(*) FROM sys.procedures) AS [Procedures],
    (SELECT COUNT(*) FROM sys.foreign_keys) AS [Foreign Keys];

/* ---------- 2) كل الجداول الموجودة بالاسم ---------- */
SELECT
    ROW_NUMBER() OVER (ORDER BY name) AS [#],
    name AS [الجدول],
    create_date AS [تاريخ الإنشاء]
FROM sys.tables
ORDER BY name;

/* ---------- 3) الـ Views الموجودة ---------- */
SELECT name AS [View الموجود] FROM sys.views WHERE is_ms_shipped = 0 ORDER BY name;

/* ---------- 4) إيه اللي ناقص؟ ---------- */
;WITH Expected(ObjName, ObjType) AS (
    SELECT * FROM (VALUES
    -- Views (7)
    ('vw_OperationProfitability','VIEW'),
    ('vw_CustomerInvoiceBalance','VIEW'),
    ('vw_CustomerStatement','VIEW'),
    ('vw_CustomerBalanceSummary','VIEW'),
    ('vw_TripSummary','VIEW'),
    ('vw_DashboardOperations','VIEW'),
    ('vw_DashboardFleet','VIEW'),
    -- Triggers (8)
    ('trg_CustodyTransactions_Sync','TRIGGER'),
    ('trg_OperationRevenueItems_Sync','TRIGGER'),
    ('trg_Operations_CostSync','TRIGGER'),
    ('trg_Expenses_OpCostSync','TRIGGER'),
    ('trg_PaymentAllocations_InvoiceSync','TRIGGER'),
    ('trg_SupplierInvoiceAllocations_Sync','TRIGGER'),
    ('trg_CashTransactions_BalanceSync','TRIGGER'),
    ('trg_BookingContainerDetails_QtySync','TRIGGER'),
    -- Procedure (1)
    ('usp_GetNextNumber','PROCEDURE')
    ) AS x(a,b)
)
SELECT
    e.ObjType AS [النوع],
    e.ObjName AS [الاسم],
    CASE WHEN o.name IS NULL THEN N'❌ ناقص' ELSE N'✅ موجود' END AS [الحالة]
FROM Expected e
LEFT JOIN sys.objects o ON o.name = e.ObjName
ORDER BY
    CASE WHEN o.name IS NULL THEN 0 ELSE 1 END,
    e.ObjType, e.ObjName;

/* ---------- 5) Seed Data ---------- */
IF OBJECT_ID('Branches') IS NOT NULL
SELECT
    (SELECT COUNT(*) FROM Branches)          AS [فروع],
    (SELECT COUNT(*) FROM Departments)       AS [أقسام],
    (SELECT COUNT(*) FROM Ports)             AS [موانئ],
    (SELECT COUNT(*) FROM Destinations)      AS [وجهات],
    (SELECT COUNT(*) FROM Services)          AS [خدمات],
    (SELECT COUNT(*) FROM TaxRates)          AS [ضرائب],
    (SELECT COUNT(*) FROM ContainerTypes)    AS [حاويات],
    (SELECT COUNT(*) FROM ExpenseTypes)      AS [مصروفات],
    (SELECT COUNT(*) FROM DocumentTypes)     AS [مستندات],
    (SELECT COUNT(*) FROM PaymentMethods)    AS [طرق دفع],
    (SELECT COUNT(*) FROM OperationStatuses) AS [حالات],
    (SELECT COUNT(*) FROM NumberSequences)   AS [ترقيم],
    (SELECT COUNT(*) FROM CompanyProfile)    AS [بيانات شركة];

/* ---------- 6) الصلاحيات ---------- */
IF OBJECT_ID('AppPermissions') IS NOT NULL
SELECT
    (SELECT COUNT(*) FROM AppPermissions)     AS [صلاحيات],
    (SELECT COUNT(*) FROM AspNetRoles)        AS [أدوار],
    (SELECT COUNT(*) FROM AppRolePermissions) AS [توزيعات];
