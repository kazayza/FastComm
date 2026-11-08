/* ═══════════════════════════════════════════════════════════════════
   ترقية «عهدة تشغيل الموظف العامة» — FastCom
   1) عمود IsFreightAdvance على أنواع المصروفات (سلفة نولون).
   2) نوع مصروف جاهز «سلفة نولون» (FREIGHT-ADV) لو مش موجود.
   آمن للتشغيل المتكرر (Idempotent).
   ═══════════════════════════════════════════════════════════════════ */

IF COL_LENGTH('dbo.ExpenseTypes', 'IsFreightAdvance') IS NULL
BEGIN
    ALTER TABLE dbo.ExpenseTypes
        ADD IsFreightAdvance bit NOT NULL CONSTRAINT DF_ExpenseTypes_IsFreightAdvance DEFAULT (0);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ExpenseTypes WHERE Code = 'FREIGHT-ADV' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.ExpenseTypes
        (Code, NameAr, NameEn, IsCustodyAllowed, IsFreightAdvance, IsOperationCost, IsTaxDeductible, IsActive, IsDeleted)
    VALUES
        ('FREIGHT-ADV', N'سلفة نولون', 'Freight Advance', 1, 1, 0, 0, 1, 0);
END
GO

/* ملحوظة تشغيل (يدوي، مرة واحدة لكل موظف تشغيل):
   - اعمل CashBox لكل موظف تشغيل وحط ResponsibleEmployeeId = رقم الموظف.
   - موّل الصندوق بتحويل من الخزينة الرئيسية (شاشة الخزينة → تحويل).
   - بعدها صرف عهدة التشغيل العامة للموظف هيتصرف من صندوقه هو مباشرة. */
