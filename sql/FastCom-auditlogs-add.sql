-- ============================================================================
-- 🔐 FastCom — إضافة جدول سجل التدقيق (AuditLogs) + صلاحية AUDIT.VIEW
-- ----------------------------------------------------------------------------
-- ❓ متى تشغيله؟   لو جدول AuditLogs مش موجود في الداتابيز (افحص بالجزء 0)
-- 🛡️ آمن للتكرار:  كله IF NOT EXISTS / OBJECT_ID — شغّله أي عدد مرات من غير ضرر.
-- 📌 التشغيل:      SSMS على قاعدة db65922 (Encrypt + Trust server certificate)
--
-- ⚠️ التنويه: الجدول ده موجود أصلًا في sql/FastCom-schema.sql (سطر 1587)
--    بنفس التعريف بالظبط. ده للداتابيز اللي اتعملت قبل إضافة الجدول/الصلاحية.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 0) الفحص السريع — شغّله في SSMS قبل وبعد عشان تتأكد من الحالة
-- ----------------------------------------------------------------------------
SELECT CASE WHEN OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
            THEN N'✅ الجدول موجود'
            ELSE N'❌ مش موجود — الجزء 1 هيعمله' END AS [حالة جدول AuditLogs];

-- ----------------------------------------------------------------------------
-- 1) إنشاء الجدول لو مش موجود (نفس التعريف في FastCom-schema.sql)
-- ----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE AuditLogs (
        AuditLogId      bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
        UserId          int NULL,
        Action          nvarchar(30)  NOT NULL,
        EntityType      nvarchar(100) NOT NULL,
        EntityId        nvarchar(100) NULL,
        OldValues       nvarchar(max) NULL,
        NewValues       nvarchar(max) NULL,
        Description     nvarchar(1000) NULL,
        IpAddress       nvarchar(64)  NULL,
        UserAgent       nvarchar(500) NULL,
        CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_AuditLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_AuditLogs_Action CHECK (Action IN (
            N'Create',N'Update',N'Delete',N'Restore',N'Approve',N'Reject',N'Cancel',
            N'Close',N'Reopen',N'Issue',N'Send',N'Settle',N'Login',N'Logout',
            N'LoginFailed',N'PermissionChange',N'Export',N'Print',N'Download',N'Upload'))
    );
END;
GO

-- ----------------------------------------------------------------------------
-- 2) ضمان الـ FK للمستخدمين (لو الجدول اتعمل من غيرها)
-- ----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys
                   WHERE name = N'FK_AuditLogs_Users' AND parent_object_id = OBJECT_ID(N'dbo.AuditLogs'))
BEGIN
    ALTER TABLE AuditLogs
        ADD CONSTRAINT FK_AuditLogs_Users FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id);
END;
GO

-- ----------------------------------------------------------------------------
-- 3) ضمان الـ Indexes (عشان الفلترة على Entity/UserId تبقى سريعة)
-- ----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_AuditLogs_Entity' AND object_id = OBJECT_ID(N'dbo.AuditLogs'))
BEGIN
    CREATE INDEX IX_AuditLogs_Entity ON AuditLogs (EntityType, EntityId, CreatedAt DESC);
END;
GO

IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_AuditLogs_User' AND object_id = OBJECT_ID(N'dbo.AuditLogs'))
BEGIN
    CREATE INDEX IX_AuditLogs_User ON AuditLogs (UserId, CreatedAt DESC);
END;
GO

-- ----------------------------------------------------------------------------
-- 4) ضمان صلاحية AUDIT.VIEW (لو الداتابيز قديمة قبل الـ 106)
--    ⚠️ من غير PermissionId صريح — الـ IDENTITY بيختار الرقم الجديد تلقائيًا
--       عشان منعملش تصادم لو فيه صراعات في الأرقام المحجوزة.
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM AppPermissions WHERE PermissionCode = N'AUDIT.VIEW')
BEGIN
    INSERT INTO AppPermissions (PermissionCode, NameAr, NameEn, ModuleCode, ActionCode, SortOrder)
    VALUES (N'AUDIT.VIEW', N'عرض سجل التدقيق', N'View Audit Log', N'AUDIT', N'VIEW', 2400);
END;
GO

-- ----------------------------------------------------------------------------
-- 5) الفحص النهائي — شغّل الجزء ده بعد ما السكريبت يخلص
-- ----------------------------------------------------------------------------
SELECT '✅ تم التأكد — شوف النتائج تحت' AS [النتيجة];
SELECT CASE WHEN OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
            THEN (SELECT COUNT(*) FROM AuditLogs) END AS [عدد السجلات],
       CASE WHEN OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
            THEN N'❌ الجدول لسه مش موجود — راجع الأخطاء فوق' END AS [ملاحظة];
SELECT PermissionId, PermissionCode, NameAr
  FROM AppPermissions
 WHERE PermissionCode = N'AUDIT.VIEW';