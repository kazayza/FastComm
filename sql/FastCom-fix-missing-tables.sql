/* =====================================================================================
   FastCom - Recovery Script
   ------------------------------------------------------------------------------------
   بيعمل 3 جداول ناقصة في قاعدة البيانات:
     1. BookingContainerLines
     2. BookingContainerDetails
     3. OperationContainers
   + الـ Indexes + الـ Trigger بتاعهم

   ⚠️  السكربت IDEMPOTENT - يعني تشغّله كام مرة براحتك،
       مش هيعمل حاجة لو الجدول موجود.

   ⚠️  شغّله في SSMS (مش في لوحة التحكم)
   ===================================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT N'=== FastCom Recovery Script ===';
PRINT N'';

/* =====================================================================================
   1) BookingContainerLines
   ===================================================================================== */
IF OBJECT_ID(N'BookingContainerLines', N'U') IS NULL
BEGIN
    PRINT N'  [CREATE] BookingContainerLines';

CREATE TABLE BookingContainerLines (
    BookingContainerLineId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingContainerLines PRIMARY KEY,
    BookingId       bigint NOT NULL,
    LineNo          int NOT NULL,
    ContainerTypeId int NOT NULL,
    RequestedQty    int NOT NULL CONSTRAINT CK_BCL_Qty CHECK (RequestedQty > 0),
    AssignedQty     int NOT NULL CONSTRAINT DF_BCL_Assigned DEFAULT 0 CONSTRAINT CK_BCL_Assigned CHECK (AssignedQty >= 0),
    WeightKg        decimal(18,3) NULL CONSTRAINT CK_BCL_Weight CHECK (WeightKg IS NULL OR WeightKg >= 0),
    Status          nvarchar(30) NOT NULL CONSTRAINT DF_BCL_Status DEFAULT N'Pending',
    Notes           nvarchar(500) NULL,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_BCL_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT UQ_BookingContainerLines UNIQUE (BookingId, LineNo),
    CONSTRAINT CK_BCL_Status CHECK (Status IN (N'Pending',N'PartiallyAssigned',N'Assigned',N'Cancelled')),
    CONSTRAINT FK_BCL_Bookings       FOREIGN KEY (BookingId)       REFERENCES Bookings(BookingId),
    CONSTRAINT FK_BCL_ContainerTypes FOREIGN KEY (ContainerTypeId) REFERENCES ContainerTypes(ContainerTypeId)
);
END
ELSE
    PRINT N'  [SKIP] BookingContainerLines - موجود بالفعل';

/* =====================================================================================
   2) BookingContainerDetails
   ===================================================================================== */
IF OBJECT_ID(N'BookingContainerDetails', N'U') IS NULL
BEGIN
    PRINT N'  [CREATE] BookingContainerDetails';

CREATE TABLE BookingContainerDetails (
    BookingContainerDetailId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingContainerDetails PRIMARY KEY,
    BookingContainerLineId bigint NOT NULL,
    ContainerId     bigint NULL,              -- NULL لحد ما الرقم يتأكد
    ContainerNumberText nvarchar(20) NULL,    -- نص مؤقت
    SealNumber      nvarchar(50)  NULL,
    ShippingLine    nvarchar(150) NULL,
    BLNumber        nvarchar(100) NULL,
    BookingReference nvarchar(100) NULL,
    WeightKg        decimal(18,3) NULL CONSTRAINT CK_BCD_Weight CHECK (WeightKg IS NULL OR WeightKg >= 0),
    Status          nvarchar(30) NOT NULL CONSTRAINT DF_BCD_Status DEFAULT N'Pending',
    Notes           nvarchar(500) NULL,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_BCD_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    CONSTRAINT CK_BCD_Status CHECK (Status IN (N'Pending',N'Confirmed',N'PickedUp',N'Delivered',N'Cancelled')),
    CONSTRAINT FK_BCD_Lines      FOREIGN KEY (BookingContainerLineId) REFERENCES BookingContainerLines(BookingContainerLineId),
    CONSTRAINT FK_BCD_Containers FOREIGN KEY (ContainerId)            REFERENCES Containers(ContainerId)
);
END
ELSE
    PRINT N'  [SKIP] BookingContainerDetails - موجود بالفعل';

/* =====================================================================================
   3) OperationContainers
   ===================================================================================== */
IF OBJECT_ID(N'OperationContainers', N'U') IS NULL
BEGIN
    PRINT N'  [CREATE] OperationContainers';

CREATE TABLE OperationContainers (
    OperationContainerId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_OperationContainers PRIMARY KEY,
    OperationId     bigint NOT NULL,
    ContainerId     bigint NULL,
    BookingContainerDetailId bigint NULL,       -- رابط صريح بسطر الحجز
    MovementSequence int NOT NULL CONSTRAINT DF_OperationContainers_Seq DEFAULT 1,
    BLNumber        nvarchar(100) NULL,
    PickupAt        datetime2(0) NULL,
    DeliveryAt      datetime2(0) NULL,
    Status          nvarchar(30) NOT NULL CONSTRAINT DF_OperationContainers_Status DEFAULT N'Assigned',
    Notes           nvarchar(500) NULL,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_OperationContainers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    -- ⚠️ مافيش UNIQUE على (OperationId, ContainerId) هنا لأن ContainerId nullable،
    --    و SQL Server بيسمح بـ NULL واحدة بس في الـ UNIQUE Constraint.
    --    الـ Uniqueness متعمولة بـ Filtered Index تحت في SECTION 19.
    CONSTRAINT CK_OperationContainers_Status CHECK (Status IN (N'Assigned',N'PickedUp',N'Delivered',N'Cancelled')),
    CONSTRAINT FK_OperationContainers_Operations FOREIGN KEY (OperationId)            REFERENCES Operations(OperationId),
    CONSTRAINT FK_OperationContainers_Containers FOREIGN KEY (ContainerId)            REFERENCES Containers(ContainerId),
    CONSTRAINT FK_OperationContainers_Details    FOREIGN KEY (BookingContainerDetailId) REFERENCES BookingContainerDetails(BookingContainerDetailId)
);
END
ELSE
    PRINT N'  [SKIP] OperationContainers - موجود بالفعل';

/* =====================================================================================
   4) Indexes (بشرط لو مش موجودة)
   ===================================================================================== */
PRINT N'';
PRINT N'--- Indexes ---';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BookingContainerLines_Booking')
BEGIN
    PRINT N'  [CREATE] IX_BookingContainerLines_Booking';
    CREATE INDEX IX_BookingContainerLines_Booking ON BookingContainerLines (BookingId);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BookingContainerDetails_Line')
BEGIN
    PRINT N'  [CREATE] IX_BookingContainerDetails_Line';
    CREATE INDEX IX_BookingContainerDetails_Line ON BookingContainerDetails (BookingContainerLineId);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BookingContainerDetails_Container')
BEGIN
    PRINT N'  [CREATE] IX_BookingContainerDetails_Container';
    CREATE INDEX IX_BookingContainerDetails_Container ON BookingContainerDetails (ContainerId);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OperationContainers_Container')
BEGIN
    PRINT N'  [CREATE] IX_OperationContainers_Container';
    CREATE INDEX IX_OperationContainers_Container ON OperationContainers (ContainerId, OperationId);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_OperationContainers_OpContainer')
BEGIN
    PRINT N'  [CREATE] UX_OperationContainers_OpContainer (Filtered Unique)';
    CREATE UNIQUE INDEX UX_OperationContainers_OpContainer
        ON OperationContainers (OperationId, ContainerId)
        WHERE ContainerId IS NOT NULL;
END

/* =====================================================================================
   5) Trigger - مزامنة AssignedQty
   ===================================================================================== */
PRINT N'';
PRINT N'--- Trigger ---';
PRINT N'  [CREATE OR ALTER] trg_BookingContainerDetails_QtySync';

CREATE OR ALTER TRIGGER trg_BookingContainerDetails_QtySync ON BookingContainerDetails
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH AffectedLines AS (
        SELECT BookingContainerLineId FROM inserted
        UNION
        SELECT BookingContainerLineId FROM deleted
    )
    UPDATE l SET
        AssignedQty = ISNULL(d.Cnt, 0),
        Status = CASE
            WHEN ISNULL(d.Cnt,0) = 0 THEN N'Pending'
            WHEN ISNULL(d.Cnt,0) >= l.RequestedQty THEN N'Assigned'
            ELSE N'PartiallyAssigned' END
    FROM BookingContainerLines l
    INNER JOIN AffectedLines a ON a.BookingContainerLineId = l.BookingContainerLineId
    OUTER APPLY (
        SELECT COUNT(*) AS Cnt
        FROM BookingContainerDetails
        WHERE BookingContainerLineId = l.BookingContainerLineId AND Status <> N'Cancelled'
    ) d;
END

/* =====================================================================================
   6) التحقق النهائي - جدول واحد فيه كل حاجة
   ===================================================================================== */
WITH ExpectedObjects AS (
    SELECT * FROM (VALUES
        (N'BookingContainerLines',               N'Table'),
        (N'BookingContainerDetails',             N'Table'),
        (N'OperationContainers',                 N'Table'),
        (N'IX_BookingContainerLines_Booking',    N'Index'),
        (N'IX_BookingContainerDetails_Line',     N'Index'),
        (N'IX_BookingContainerDetails_Container',N'Index'),
        (N'IX_OperationContainers_Container',    N'Index'),
        (N'UX_OperationContainers_OpContainer',  N'Index'),
        (N'trg_BookingContainerDetails_QtySync', N'Trigger')
    ) AS v(ObjectName, ObjectType)
),
FoundObjects AS (
    SELECT name AS ObjectName FROM sys.objects
    UNION
    SELECT name              FROM sys.indexes  WHERE name IS NOT NULL
)
SELECT
    e.ObjectName,
    e.ObjectType,
    CASE WHEN f.ObjectName IS NULL THEN N'-- MISSING --' ELSE N'OK' END AS [Status]
FROM ExpectedObjects e
LEFT JOIN FoundObjects f ON f.ObjectName = e.ObjectName
ORDER BY e.ObjectType, e.ObjectName;

/* ── ملخص عام ── */
SELECT
    (SELECT COUNT(*) FROM sys.tables)                             AS [Tables],
    (SELECT COUNT(*) FROM sys.views)                              AS [Views],
    (SELECT COUNT(*) FROM sys.triggers WHERE is_ms_shipped = 0)   AS [Triggers],
    (SELECT COUNT(*) FROM sys.procedures WHERE is_ms_shipped = 0) AS [Procedures],
    (SELECT COUNT(*) FROM AppPermissions)                         AS [Permissions],
    (SELECT COUNT(*) FROM AspNetRoles)                            AS [Roles];
