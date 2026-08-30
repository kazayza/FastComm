/* =====================================================================================
   FastCom — Port Transport Management System
   Schema v2  |  SQL Server 2019+  |  .NET 8 / EF Core 8
   =====================================================================================

   مبني على مراجعة reviews/01-مراجعة-الملفين.md
   بيحل المشاكل المانعة: A1 A2 A3 A4 A5 A6 A7 A8 A9 A10 A11

   التغييرات الجوهرية عن v1:
     1. TripOperations (many-to-many)          → A2  التجميع
     2. BookingContainerLines / Details        → A3  الحجز بالعدد والنوع
     3. Payments / PaymentAllocations          → A1  المدفوعات
     4. SupplierInvoices / SupplierPayments    → A11 الذمم الدائنة
     5. TaxRates                               → A4  ضريبة جدول / معفى / ترانزيت
     6. ASP.NET Core Identity                  → A5  بدل AppUsers يدوي
     7. ~75 صلاحية + توزيعها على الأدوار       → A6
     8. NumberSequences (Year + BranchId=0)    → A7
     9. CustodyHolders اتلغى                    → A8
    10. Triggers لمزامنة العهد                  → A8  مصدر واحد للحقيقة
    11. RevenueNet / RevenueTax محسوبة          → A9
    12. TripCostAllocations يدوي                → A10 (القرار X1: manual)
    13. CustomerPortalTokens (Magic Link)       → D12 (القرار X3)
    14. CompanyProfile                          → D11

   ⚠️  ملاحظة مهمة:
       SQL Server مابيسمحش بـ Subquery في Computed Column.
       لذلك المجاميع (AmountSpent / RevenueNet) بتتحسب بـ TRIGGERS،
       واللي على مستوى الصف (LineSubtotal / LineTax) بـ PERSISTED Computed Columns.

   Phase 2 (مش في الملف ده): HR (Employees الموسّع، الإجازات، الحضور، السلف)،
                              EInvoiceSubmissions، Chart of Accounts
   ===================================================================================== */

/* =====================================================================================
   ⚠️  اسم قاعدة البيانات: db65922
   =====================================================================================
   لو الاسم اللي جالك من الـ Control Panel مختلف، اعمل Find & Replace:
       db65922  →  اسم قاعدة البيانات بتاعتك

   📌 ملاحظة: CREATE VIEW / PROCEDURE / TRIGGER مالهاش prefix —
      ده قيد من SQL Server نفسه ("does not allow specifying the database name
      as a prefix to the object name").
      ⚠️ يعني لازم تكون متصل بـ db65922 وقت ما تشغّل السكريبت.
   ===================================================================================== */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET XACT_ABORT ON;
GO

/* =====================================================================================
   SECTION 01 — ORGANIZATION
   ===================================================================================== */

CREATE TABLE db65922.dbo.Branches (
    BranchId        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY,
    BranchCode      nvarchar(20)  NOT NULL,
    NameAr          nvarchar(150) NOT NULL,
    NameEn          nvarchar(150) NULL,
    AddressAr       nvarchar(500) NULL,
    AddressEn       nvarchar(500) NULL,
    Phone           nvarchar(50)  NULL,
    Email           nvarchar(254) NULL,
    TaxNumber       nvarchar(50)  NULL,
    IsActive        bit NOT NULL CONSTRAINT DF_Branches_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Branches_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Branches_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Branches_Code UNIQUE (BranchCode)
);
GO

-- D11: معلومات الشركة — Singleton (صف واحد بس) — للـ Header والـ Footer والفواتير
CREATE TABLE db65922.dbo.CompanyProfile (
    CompanyProfileId    int NOT NULL CONSTRAINT PK_CompanyProfile PRIMARY KEY,
    LegalNameAr         nvarchar(300) NOT NULL,
    LegalNameEn         nvarchar(300) NULL,
    TradeNameAr         nvarchar(200) NULL,
    TradeNameEn         nvarchar(200) NULL,
    TaxNumber           nvarchar(50)  NOT NULL,          -- إلزامي في الفاتورة
    VatRegistrationNumber nvarchar(50) NULL,
    CommercialRegister  nvarchar(50)  NULL,
    AddressAr           nvarchar(500) NOT NULL,          -- إلزامي ومفصّل
    AddressEn           nvarchar(500) NULL,
    CityAr              nvarchar(100) NULL,
    CityEn              nvarchar(100) NULL,
    Phone               nvarchar(50)  NULL,
    Mobile              nvarchar(50)  NULL,
    Email               nvarchar(254) NULL,
    Website             nvarchar(254) NULL,
    LogoPath            nvarchar(500) NULL,
    LogoDarkPath        nvarchar(500) NULL,
    StampPath           nvarchar(500) NULL,
    InvoiceFooterNoteAr nvarchar(1000) NULL,
    InvoiceFooterNoteEn nvarchar(1000) NULL,
    DefaultCurrencyCode char(3) NOT NULL CONSTRAINT DF_CompanyProfile_Currency DEFAULT 'EGP',
    DefaultTaxRateId    int NULL,
    FiscalYearStartMonth tinyint NOT NULL CONSTRAINT DF_CompanyProfile_FYStart DEFAULT 1,
    UpdatedAt           datetime2(0) NULL,
    UpdatedBy           int NULL,
    CONSTRAINT CK_CompanyProfile_Singleton CHECK (CompanyProfileId = 1),
    CONSTRAINT CK_CompanyProfile_FYMonth  CHECK (FiscalYearStartMonth BETWEEN 1 AND 12)
);
GO

CREATE TABLE db65922.dbo.Departments (
    DepartmentId    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
    Code            nvarchar(20)  NOT NULL,
    NameAr          nvarchar(150) NOT NULL,
    NameEn          nvarchar(150) NULL,
    IsActive        bit NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Departments_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Departments_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    CONSTRAINT UQ_Departments_Code UNIQUE (Code)
);
GO

-- B2: بدل النص الحر في Employees.JobTitle
CREATE TABLE db65922.dbo.JobTitles (
    JobTitleId  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobTitles PRIMARY KEY,
    Code        nvarchar(30)  NOT NULL,
    NameAr      nvarchar(100) NOT NULL,
    NameEn      nvarchar(100) NULL,
    IsActive    bit NOT NULL CONSTRAINT DF_JobTitles_IsActive DEFAULT 1,
    IsDeleted   bit NOT NULL CONSTRAINT DF_JobTitles_IsDeleted DEFAULT 0,
    CreatedAt   datetime2(0) NOT NULL CONSTRAINT DF_JobTitles_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_JobTitles_Code UNIQUE (Code)
);
GO

CREATE TABLE db65922.dbo.SystemSettings (
    SettingId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemSettings PRIMARY KEY,
    SettingKey      nvarchar(100) NOT NULL,
    SettingValue    nvarchar(max) NULL,
    ValueType       nvarchar(20)  NOT NULL CONSTRAINT DF_SystemSettings_Type DEFAULT N'String',
    Description     nvarchar(500) NULL,
    IsSystem        bit NOT NULL CONSTRAINT DF_SystemSettings_IsSystem DEFAULT 0,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    CONSTRAINT UQ_SystemSettings_Key UNIQUE (SettingKey),
    CONSTRAINT CK_SystemSettings_ValueType CHECK (ValueType IN (N'String',N'Int',N'Bool',N'Decimal',N'Json',N'Date'))
);
GO

/* =====================================================================================
   SECTION 02 — MASTER DATA
   ===================================================================================== */

CREATE TABLE db65922.dbo.DocumentTypes (
    DocumentTypeId  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentTypes PRIMARY KEY,
    Code            nvarchar(30)  NOT NULL,
    NameAr          nvarchar(150) NOT NULL,
    NameEn          nvarchar(150) NULL,
    HasExpiry       bit NOT NULL CONSTRAINT DF_DocumentTypes_HasExpiry DEFAULT 0,
    IsActive        bit NOT NULL CONSTRAINT DF_DocumentTypes_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_DocumentTypes_IsDeleted DEFAULT 0,
    CONSTRAINT UQ_DocumentTypes_Code UNIQUE (Code)
);
GO

CREATE TABLE db65922.dbo.PaymentTerms (
    PaymentTermId   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentTerms PRIMARY KEY,
    Code            nvarchar(30)  NOT NULL,
    NameAr          nvarchar(100) NOT NULL,
    NameEn          nvarchar(100) NULL,
    DueDays         int NOT NULL CONSTRAINT CK_PaymentTerms_DueDays CHECK (DueDays >= 0),
    IsActive        bit NOT NULL CONSTRAINT DF_PaymentTerms_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_PaymentTerms_IsDeleted DEFAULT 0,
    CONSTRAINT UQ_PaymentTerms_Code UNIQUE (Code)
);
GO

-- A1: نقدي / شيك / تحويل
CREATE TABLE db65922.dbo.PaymentMethods (
    PaymentMethodId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentMethods PRIMARY KEY,
    Code            nvarchar(30)  NOT NULL,
    NameAr          nvarchar(100) NOT NULL,
    NameEn          nvarchar(100) NULL,
    RequiresChequeNo bit NOT NULL CONSTRAINT DF_PaymentMethods_Cheque DEFAULT 0,
    IsCashBased     bit NOT NULL CONSTRAINT DF_PaymentMethods_Cash DEFAULT 1,
    SortOrder       int NOT NULL CONSTRAINT DF_PaymentMethods_Sort DEFAULT 100,
    IsActive        bit NOT NULL CONSTRAINT DF_PaymentMethods_IsActive DEFAULT 1,
    CONSTRAINT UQ_PaymentMethods_Code UNIQUE (Code)
);
GO

-- A4 + D3: ده الجدول اللي بيحل مشكلة الضريبة كلها
CREATE TABLE db65922.dbo.TaxRates (
    TaxRateId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TaxRates PRIMARY KEY,
    Code            nvarchar(30)  NOT NULL,
    NameAr          nvarchar(100) NOT NULL,
    NameEn          nvarchar(100) NULL,
    Rate            decimal(9,4) NOT NULL CONSTRAINT CK_TaxRates_Rate CHECK (Rate >= 0 AND Rate <= 100),
    TaxKind         nvarchar(20)  NOT NULL,
    IsDeductible    bit NOT NULL CONSTRAINT DF_TaxRates_Deductible DEFAULT 1,
    RequiresExemptionReason bit NOT NULL CONSTRAINT DF_TaxRates_ReqReason DEFAULT 0,
    ValidFrom       date NOT NULL CONSTRAINT DF_TaxRates_ValidFrom DEFAULT '2016-09-08',
    ValidTo         date NULL,
    IsActive        bit NOT NULL CONSTRAINT DF_TaxRates_IsActive DEFAULT 1,
    CONSTRAINT UQ_TaxRates_Code UNIQUE (Code),
    CONSTRAINT CK_TaxRates_Kind CHECK (TaxKind IN (N'Standard',N'Schedule',N'Zero',N'Exempt')),
    CONSTRAINT CK_TaxRates_Dates CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
GO

CREATE TABLE db65922.dbo.Ports (
    PortId      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Ports PRIMARY KEY,
    PortCode    nvarchar(20)  NOT NULL,
    NameAr      nvarchar(150) NOT NULL,
    NameEn      nvarchar(150) NULL,
    CityAr      nvarchar(100) NULL,
    CityEn      nvarchar(100) NULL,
    IsActive    bit NOT NULL CONSTRAINT DF_Ports_IsActive DEFAULT 1,
    IsDeleted   bit NOT NULL CONSTRAINT DF_Ports_IsDeleted DEFAULT 0,
    CreatedAt   datetime2(0) NOT NULL CONSTRAINT DF_Ports_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt   datetime2(0) NULL,
    CONSTRAINT UQ_Ports_Code UNIQUE (PortCode)
);
GO

-- B12: بدل النص الحر في Bookings/Operations/CustomerPriceRules
CREATE TABLE db65922.dbo.Destinations (
    DestinationId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Destinations PRIMARY KEY,
    Code          nvarchar(30)  NOT NULL,
    NameAr        nvarchar(150) NOT NULL,
    NameEn        nvarchar(150) NULL,
    CityAr        nvarchar(100) NULL,
    CityEn        nvarchar(100) NULL,
    DistanceKm    decimal(8,1) NULL CONSTRAINT CK_Destinations_Distance CHECK (DistanceKm IS NULL OR DistanceKm >= 0),
    IsActive      bit NOT NULL CONSTRAINT DF_Destinations_IsActive DEFAULT 1,
    IsDeleted     bit NOT NULL CONSTRAINT DF_Destinations_IsDeleted DEFAULT 0,
    CONSTRAINT UQ_Destinations_Code UNIQUE (Code)
);
GO

-- B12: TripType كان عمود ميت — دلوقتي ليه master
CREATE TABLE db65922.dbo.TripTypes (
    TripTypeId  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TripTypes PRIMARY KEY,
    Code        nvarchar(30)  NOT NULL,
    NameAr      nvarchar(100) NOT NULL,
    NameEn      nvarchar(100) NULL,
    DefaultTaxRateId int NULL,
    IsActive    bit NOT NULL CONSTRAINT DF_TripTypes_IsActive DEFAULT 1,
    IsDeleted   bit NOT NULL CONSTRAINT DF_TripTypes_IsDeleted DEFAULT 0,
    CONSTRAINT UQ_TripTypes_Code UNIQUE (Code)
);
GO
ALTER TABLE db65922.dbo.TripTypes
    ADD CONSTRAINT FK_TripTypes_TaxRates FOREIGN KEY (DefaultTaxRateId) REFERENCES db65922.dbo.TaxRates(TaxRateId);
GO

CREATE TABLE db65922.dbo.Services (
    ServiceId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Services PRIMARY KEY,
    ServiceCode     nvarchar(30)  NOT NULL,
    NameAr          nvarchar(150) NOT NULL,
    NameEn          nvarchar(150) NULL,
    Unit            nvarchar(30)  NOT NULL CONSTRAINT DF_Services_Unit DEFAULT N'Trip',
    -- B12: كانوا ناقصين — الـ Prompt قسم 13 بيطلبهم
    DefaultSellingPrice decimal(19,4) NOT NULL CONSTRAINT DF_Services_Price DEFAULT 0 CONSTRAINT CK_Services_Price CHECK (DefaultSellingPrice >= 0),
    DefaultCost     decimal(19,4) NOT NULL CONSTRAINT DF_Services_Cost DEFAULT 0 CONSTRAINT CK_Services_Cost CHECK (DefaultCost >= 0),
    TaxRateId       int NULL,
    IsTaxable       bit NOT NULL CONSTRAINT DF_Services_IsTaxable DEFAULT 1,
    -- A4: إلزامي للفاتورة الإلكترونية المصرية
    Gs1Code         nvarchar(50) NULL,
    EgsCode         nvarchar(50) NULL,
    IsActive        bit NOT NULL CONSTRAINT DF_Services_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Services_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Services_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt       datetime2(0) NULL,
    CONSTRAINT UQ_Services_Code UNIQUE (ServiceCode),
    CONSTRAINT FK_Services_TaxRates FOREIGN KEY (TaxRateId) REFERENCES db65922.dbo.TaxRates(TaxRateId)
);
GO

CREATE TABLE db65922.dbo.ContainerTypes (
    ContainerTypeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContainerTypes PRIMARY KEY,
    Code            nvarchar(30)  NOT NULL,
    NameAr          nvarchar(100) NOT NULL,
    NameEn          nvarchar(100) NULL,
    SizeFeet        tinyint NOT NULL CONSTRAINT CK_ContainerTypes_Size CHECK (SizeFeet IN (10,20,40,45,53)),
    IsReefer        bit NOT NULL CONSTRAINT DF_ContainerTypes_Reefer DEFAULT 0,
    IsActive        bit NOT NULL CONSTRAINT DF_ContainerTypes_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_ContainerTypes_IsDeleted DEFAULT 0,
    CONSTRAINT UQ_ContainerTypes_Code UNIQUE (Code)
);
GO

CREATE TABLE db65922.dbo.ExpenseTypes (
    ExpenseTypeId   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExpenseTypes PRIMARY KEY,
    Code            nvarchar(30)  NOT NULL,
    NameAr          nvarchar(150) NOT NULL,
    NameEn          nvarchar(150) NULL,
    IsCustodyAllowed bit NOT NULL CONSTRAINT DF_ExpenseTypes_CustodyAllowed DEFAULT 1,
    IsOperationCost bit NOT NULL CONSTRAINT DF_ExpenseTypes_OpCost DEFAULT 1,   -- A10: هل تدخل في تكلفة العملية؟
    IsTaxDeductible bit NOT NULL CONSTRAINT DF_ExpenseTypes_Deductible DEFAULT 1,
    IsActive        bit NOT NULL CONSTRAINT DF_ExpenseTypes_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_ExpenseTypes_IsDeleted DEFAULT 0,
    CONSTRAINT UQ_ExpenseTypes_Code UNIQUE (Code)
);
GO

-- قسم 40: حالات قابلة للتكوين
CREATE TABLE db65922.dbo.OperationStatuses (
    StatusId        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OperationStatuses PRIMARY KEY,
    Code            nvarchar(30)  NOT NULL,
    NameAr          nvarchar(100) NOT NULL,
    NameEn          nvarchar(100) NULL,
    SortOrder       int NOT NULL CONSTRAINT DF_OpStatus_Sort DEFAULT 100,
    IsTerminal      bit NOT NULL CONSTRAINT DF_OpStatus_Terminal DEFAULT 0,
    ColorHex        nvarchar(7)   NULL,
    IsActive        bit NOT NULL CONSTRAINT DF_OpStatus_IsActive DEFAULT 1,
    CONSTRAINT UQ_OperationStatuses_Code UNIQUE (Code)
);
GO

/* =====================================================================================
   SECTION 03 — PARTIES
   ===================================================================================== */

CREATE TABLE db65922.dbo.Customers (
    CustomerId      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    CustomerCode    nvarchar(30)  NOT NULL,
    CustomerType    nvarchar(20)  NOT NULL CONSTRAINT DF_Customers_Type DEFAULT N'Company',
    NameAr          nvarchar(200) NOT NULL,
    NameEn          nvarchar(200) NULL,
    TaxNumber       nvarchar(50)  NULL,
    NationalId      nvarchar(30)  NULL,          -- للأفراد في الفاتورة الإلكترونية
    CommercialRegister nvarchar(50) NULL,
    Phone           nvarchar(50)  NULL,
    Email           nvarchar(254) NULL,
    AddressAr       nvarchar(500) NULL,
    AddressEn       nvarchar(500) NULL,
    CityAr          nvarchar(100) NULL,
    CityEn          nvarchar(100) NULL,
    PaymentTermId   int NULL,
    CreditLimit     decimal(19,4) NOT NULL CONSTRAINT DF_Customers_CreditLimit DEFAULT 0 CONSTRAINT CK_Customers_CreditLimit CHECK (CreditLimit >= 0),
    -- D12: بورتال العملاء
    PortalEnabled   bit NOT NULL CONSTRAINT DF_Customers_Portal DEFAULT 0,
    PreferredLanguage char(2) NOT NULL CONSTRAINT DF_Customers_Lang DEFAULT 'ar',
    IsActive        bit NOT NULL CONSTRAINT DF_Customers_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Customers_Code UNIQUE (CustomerCode),
    CONSTRAINT CK_Customers_Type CHECK (CustomerType IN (N'Company',N'Individual')),
    CONSTRAINT CK_Customers_Lang CHECK (PreferredLanguage IN ('ar','en')),
    -- A4: الشركة لازم يكون لها رقم ضريبي
    CONSTRAINT CK_Customers_CompanyTax CHECK (CustomerType <> N'Company' OR TaxNumber IS NOT NULL),
    -- A4: الفرد لازم يكون له رقم قومي
    CONSTRAINT CK_Customers_IndividualNid CHECK (CustomerType <> N'Individual' OR NationalId IS NOT NULL),
    CONSTRAINT FK_Customers_PaymentTerms FOREIGN KEY (PaymentTermId) REFERENCES db65922.dbo.PaymentTerms(PaymentTermId)
);
GO

CREATE TABLE db65922.dbo.CustomerContacts (
    CustomerContactId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerContacts PRIMARY KEY,
    CustomerId      int NOT NULL,
    ContactName     nvarchar(150) NOT NULL,
    JobTitle        nvarchar(100) NULL,
    Phone           nvarchar(50)  NULL,
    Mobile          nvarchar(50)  NULL,
    Email           nvarchar(254) NULL,
    IsPrimary       bit NOT NULL CONSTRAINT DF_CustomerContacts_IsPrimary DEFAULT 0,
    CanReceiveInvoices bit NOT NULL CONSTRAINT DF_CustomerContacts_Invoice DEFAULT 0,
    IsActive        bit NOT NULL CONSTRAINT DF_CustomerContacts_IsActive DEFAULT 1,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_CustomerContacts_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    CONSTRAINT FK_CustomerContacts_Customers FOREIGN KEY (CustomerId) REFERENCES db65922.dbo.Customers(CustomerId)
);
GO

CREATE TABLE db65922.dbo.Suppliers (
    SupplierId      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY,
    SupplierCode    nvarchar(30)  NOT NULL,
    SupplierType    nvarchar(30)  NOT NULL CONSTRAINT DF_Suppliers_Type DEFAULT N'Other',
    NameAr          nvarchar(200) NOT NULL,
    NameEn          nvarchar(200) NULL,
    TaxNumber       nvarchar(50)  NULL,
    Phone           nvarchar(50)  NULL,
    Email           nvarchar(254) NULL,
    Address         nvarchar(500) NULL,
    PaymentTermId   int NULL,
    IsActive        bit NOT NULL CONSTRAINT DF_Suppliers_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Suppliers_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Suppliers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Suppliers_Code UNIQUE (SupplierCode),
    CONSTRAINT CK_Suppliers_Type CHECK (SupplierType IN (N'TransportCompany',N'FuelStation',N'Workshop',N'PortServices',N'PartsSupplier',N'Other')),
    CONSTRAINT FK_Suppliers_PaymentTerms FOREIGN KEY (PaymentTermId) REFERENCES db65922.dbo.PaymentTerms(PaymentTermId)
);
GO

/* =====================================================================================
   SECTION 04 — EMPLOYEES (MVP: أعمدة أساسية بس — Phase 2 هتوسّعه)
   ===================================================================================== */

CREATE TABLE db65922.dbo.Employees (
    EmployeeId      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    EmployeeCode    nvarchar(30)  NOT NULL,
    FullNameAr      nvarchar(200) NOT NULL,
    FullNameEn      nvarchar(200) NULL,
    NationalId      nvarchar(30)  NULL,
    Mobile          nvarchar(50)  NULL,
    Email           nvarchar(254) NULL,
    Address         nvarchar(500) NULL,
    HireDate        date NULL,
    DepartmentId    int NULL,
    JobTitleId      int NULL,                 -- B2: بدل النص الحر
    BranchId        int NULL,
    ManagerEmployeeId int NULL,
    EmploymentStatus nvarchar(30) NOT NULL CONSTRAINT DF_Employees_Status DEFAULT N'Active',
    IsDeleted       bit NOT NULL CONSTRAINT DF_Employees_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Employees_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Employees_Code UNIQUE (EmployeeCode),
    CONSTRAINT CK_Employees_Status CHECK (EmploymentStatus IN (N'Active',N'OnLeave',N'Suspended',N'Terminated')),
    CONSTRAINT FK_Employees_Departments FOREIGN KEY (DepartmentId) REFERENCES db65922.dbo.Departments(DepartmentId),
    CONSTRAINT FK_Employees_JobTitles   FOREIGN KEY (JobTitleId)   REFERENCES db65922.dbo.JobTitles(JobTitleId),
    CONSTRAINT FK_Employees_Branches    FOREIGN KEY (BranchId)     REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_Employees_Manager     FOREIGN KEY (ManagerEmployeeId) REFERENCES db65922.dbo.Employees(EmployeeId)
);
GO

/* =====================================================================================
   SECTION 05 — FLEET
   ===================================================================================== */

CREATE TABLE db65922.dbo.Drivers (
    DriverId        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Drivers PRIMARY KEY,
    DriverCode      nvarchar(30)  NOT NULL,
    DriverType      nvarchar(20)  NOT NULL CONSTRAINT DF_Drivers_Type DEFAULT N'Internal',
    EmployeeId      int NULL,
    SupplierId      int NULL,
    FullName        nvarchar(200) NOT NULL,
    NationalId      nvarchar(30)  NULL,
    Mobile          nvarchar(50)  NULL,
    LicenseNumber   nvarchar(50)  NULL,
    LicenseType     nvarchar(50)  NULL,
    LicenseExpiryDate date NULL,
    Status          nvarchar(20)  NOT NULL CONSTRAINT DF_Drivers_Status DEFAULT N'Active',
    Notes           nvarchar(500) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Drivers_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Drivers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Drivers_Code UNIQUE (DriverCode),
    CONSTRAINT CK_Drivers_Type   CHECK (DriverType IN (N'Internal',N'External')),
    CONSTRAINT CK_Drivers_Status CHECK (Status IN (N'Active',N'Inactive',N'Suspended')),
    -- A8: منع التناقض (كان مسموح Internal مع SupplierId معبّى)
    CONSTRAINT CK_Drivers_Internal CHECK (DriverType <> N'Internal' OR (EmployeeId IS NOT NULL AND SupplierId IS NULL)),
    CONSTRAINT CK_Drivers_External CHECK (DriverType <> N'External' OR (SupplierId IS NOT NULL AND EmployeeId IS NULL)),
    CONSTRAINT FK_Drivers_Employees FOREIGN KEY (EmployeeId) REFERENCES db65922.dbo.Employees(EmployeeId),
    CONSTRAINT FK_Drivers_Suppliers FOREIGN KEY (SupplierId) REFERENCES db65922.dbo.Suppliers(SupplierId)
);
GO

CREATE TABLE db65922.dbo.Vehicles (
    VehicleId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Vehicles PRIMARY KEY,
    VehicleCode     nvarchar(30)  NOT NULL,
    PlateNumber     nvarchar(30)  NOT NULL,
    VehicleType     nvarchar(50)  NOT NULL,
    Brand           nvarchar(100) NULL,
    Model           nvarchar(100) NULL,
    ModelYear       smallint NULL,
    OwnershipType   nvarchar(20)  NOT NULL CONSTRAINT DF_Vehicles_Ownership DEFAULT N'Company',
    SupplierId      int NULL,
    CapacityTon     decimal(10,2) NULL CONSTRAINT CK_Vehicles_Capacity CHECK (CapacityTon IS NULL OR CapacityTon >= 0),
    ContainerSlots20 tinyint NOT NULL CONSTRAINT DF_Vehicles_Slots DEFAULT 1,   -- كم حاوية 20 قدم بتشيل
    LicenseExpiryDate date NULL,
    InsuranceExpiryDate date NULL,
    Status          nvarchar(30)  NOT NULL CONSTRAINT DF_Vehicles_Status DEFAULT N'Available',
    Notes           nvarchar(500) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Vehicles_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Vehicles_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    -- X2: AssignedDriverId اتشال — التخصيص الفعلي في Trips بس
    CONSTRAINT UQ_Vehicles_Code  UNIQUE (VehicleCode),
    CONSTRAINT UQ_Vehicles_Plate UNIQUE (PlateNumber),
    CONSTRAINT CK_Vehicles_Ownership CHECK (OwnershipType IN (N'Company',N'External')),
    CONSTRAINT CK_Vehicles_Status    CHECK (Status IN (N'Available',N'InTrip',N'Maintenance',N'OutOfService')),
    CONSTRAINT FK_Vehicles_Suppliers FOREIGN KEY (SupplierId) REFERENCES db65922.dbo.Suppliers(SupplierId)
);
GO

CREATE TABLE db65922.dbo.Trailers (
    TrailerId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Trailers PRIMARY KEY,
    TrailerCode     nvarchar(30)  NOT NULL,
    PlateNumber     nvarchar(30)  NULL,
    TrailerType     nvarchar(50)  NOT NULL,
    SizeFeet        tinyint NULL CONSTRAINT CK_Trailers_Size CHECK (SizeFeet IS NULL OR SizeFeet IN (20,40,45,53)),
    OwnershipType   nvarchar(20)  NOT NULL CONSTRAINT DF_Trailers_Ownership DEFAULT N'Company',
    SupplierId      int NULL,
    Status          nvarchar(30)  NOT NULL CONSTRAINT DF_Trailers_Status DEFAULT N'Available',
    LicenseExpiryDate date NULL,
    InsuranceExpiryDate date NULL,
    Notes           nvarchar(500) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Trailers_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Trailers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Trailers_Code  UNIQUE (TrailerCode),
    CONSTRAINT UQ_Trailers_Plate UNIQUE (PlateNumber),
    CONSTRAINT CK_Trailers_Ownership CHECK (OwnershipType IN (N'Company',N'External')),
    CONSTRAINT CK_Trailers_Status    CHECK (Status IN (N'Available',N'InTrip',N'Maintenance',N'OutOfService')),
    CONSTRAINT FK_Trailers_Suppliers FOREIGN KEY (SupplierId) REFERENCES db65922.dbo.Suppliers(SupplierId)
);
GO

-- B1: كان ناقص تمامًا — والـ Fleet Dashboard بيطلب "Maintenance Due"
CREATE TABLE db65922.dbo.VehicleMaintenance (
    MaintenanceId   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_VehicleMaintenance PRIMARY KEY,
    MaintenanceNumber nvarchar(40) NOT NULL,
    VehicleId       int NOT NULL,
    BranchId        int NOT NULL,
    MaintenanceDate date NOT NULL,
    MaintenanceType nvarchar(50)  NOT NULL,
    SupplierId      int NULL,
    Odometer        decimal(18,1) NULL CONSTRAINT CK_VehMaint_Odometer CHECK (Odometer IS NULL OR Odometer >= 0),
    Cost            decimal(19,4) NOT NULL CONSTRAINT DF_VehMaint_Cost DEFAULT 0 CONSTRAINT CK_VehMaint_Cost CHECK (Cost >= 0),
    NextDueDate     date NULL,
    NextDueOdometer decimal(18,1) NULL,
    Description     nvarchar(1000) NULL,
    Status          nvarchar(20) NOT NULL CONSTRAINT DF_VehMaint_Status DEFAULT N'Completed',
    IsDeleted       bit NOT NULL CONSTRAINT DF_VehMaint_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_VehMaint_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    CONSTRAINT UQ_VehicleMaintenance_Number UNIQUE (MaintenanceNumber),
    CONSTRAINT CK_VehMaint_Status CHECK (Status IN (N'Scheduled',N'InProgress',N'Completed',N'Cancelled')),
    CONSTRAINT FK_VehMaint_Vehicles  FOREIGN KEY (VehicleId)  REFERENCES db65922.dbo.Vehicles(VehicleId),
    CONSTRAINT FK_VehMaint_Branches  FOREIGN KEY (BranchId)   REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_VehMaint_Suppliers FOREIGN KEY (SupplierId) REFERENCES db65922.dbo.Suppliers(SupplierId)
);
GO

/* =====================================================================================
   SECTION 06 — PRICING
   ===================================================================================== */

CREATE TABLE db65922.dbo.PriceLists (
    PriceListId     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PriceLists PRIMARY KEY,
    PriceListCode   nvarchar(30)  NOT NULL,
    NameAr          nvarchar(150) NOT NULL,
    NameEn          nvarchar(150) NULL,
    CurrencyCode    char(3) NOT NULL CONSTRAINT DF_PriceLists_Currency DEFAULT 'EGP',
    ValidFrom       date NOT NULL,
    ValidTo         date NULL,
    IsActive        bit NOT NULL CONSTRAINT DF_PriceLists_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_PriceLists_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_PriceLists_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT UQ_PriceLists_Code UNIQUE (PriceListCode),
    CONSTRAINT CK_PriceLists_Dates CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
GO

CREATE TABLE db65922.dbo.CustomerPriceRules (
    CustomerPriceRuleId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerPriceRules PRIMARY KEY,
    CustomerId      int NOT NULL,
    PriceListId     int NULL,
    ServiceId       int NOT NULL,
    PortId          int NULL,
    DestinationId   int NULL,                 -- B12: بدل النص الحر
    ContainerTypeId int NULL,
    TripTypeId      int NULL,                 -- B12: بدل النص الحر
    UnitPrice       decimal(19,4) NOT NULL CONSTRAINT CK_CPR_Price CHECK (UnitPrice >= 0),
    CostPrice       decimal(19,4) NULL CONSTRAINT CK_CPR_Cost CHECK (CostPrice IS NULL OR CostPrice >= 0),
    TaxRateId       int NULL,
    ValidFrom       date NOT NULL,
    ValidTo         date NULL,
    Priority        int NOT NULL CONSTRAINT DF_CPR_Priority DEFAULT 100,
    IsActive        bit NOT NULL CONSTRAINT DF_CPR_IsActive DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_CPR_IsDeleted DEFAULT 0,
    Notes           nvarchar(500) NULL,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_CPR_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    CONSTRAINT CK_CPR_Dates CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom),
    CONSTRAINT FK_CPR_Customers      FOREIGN KEY (CustomerId)      REFERENCES db65922.dbo.Customers(CustomerId),
    CONSTRAINT FK_CPR_PriceLists     FOREIGN KEY (PriceListId)     REFERENCES db65922.dbo.PriceLists(PriceListId),
    CONSTRAINT FK_CPR_Services       FOREIGN KEY (ServiceId)       REFERENCES db65922.dbo.Services(ServiceId),
    CONSTRAINT FK_CPR_Ports          FOREIGN KEY (PortId)          REFERENCES db65922.dbo.Ports(PortId),
    CONSTRAINT FK_CPR_Destinations   FOREIGN KEY (DestinationId)   REFERENCES db65922.dbo.Destinations(DestinationId),
    CONSTRAINT FK_CPR_ContainerTypes FOREIGN KEY (ContainerTypeId) REFERENCES db65922.dbo.ContainerTypes(ContainerTypeId),
    CONSTRAINT FK_CPR_TripTypes      FOREIGN KEY (TripTypeId)      REFERENCES db65922.dbo.TripTypes(TripTypeId),
    CONSTRAINT FK_CPR_TaxRates       FOREIGN KEY (TaxRateId)       REFERENCES db65922.dbo.TaxRates(TaxRateId)
);
GO

-- B12: منع تداخل قاعدتين بنفس الأبعاد والفترتين (يسبب غموض في السعر)
CREATE UNIQUE INDEX UX_CustomerPriceRules_NoOverlap
    ON db65922.dbo.CustomerPriceRules (CustomerId, ServiceId, PortId, DestinationId, ContainerTypeId, TripTypeId, ValidFrom)
    WHERE IsDeleted = 0 AND IsActive = 1;
GO

/* =====================================================================================
   SECTION 07 — BOOKING / CONTAINERS
   ===================================================================================== */

CREATE TABLE db65922.dbo.Bookings (
    BookingId       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Bookings PRIMARY KEY,
    BookingNumber   nvarchar(40)  NOT NULL,
    BranchId        int NOT NULL,
    CustomerId      int NOT NULL,
    BookingDate     datetime2(0) NOT NULL CONSTRAINT DF_Bookings_Date DEFAULT SYSUTCDATETIME(),
    RequestedDate   date NULL,
    ServiceId       int NULL,
    PortId          int NULL,
    DestinationId   int NULL,                 -- B12
    TripTypeId      int NULL,                 -- بيحدد الضريبة (وارد/صادر/ترانزيت)
    CustomerReference nvarchar(100) NULL,
    ContactId       int NULL,
    Status          nvarchar(30) NOT NULL CONSTRAINT DF_Bookings_Status DEFAULT N'Draft',
    Notes           nvarchar(1000) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Bookings_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Bookings_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Bookings_Number UNIQUE (BookingNumber),
    CONSTRAINT CK_Bookings_Status CHECK (Status IN (N'Draft',N'Confirmed',N'Assigned',N'InProgress',N'Completed',N'Cancelled')),
    CONSTRAINT FK_Bookings_Branches     FOREIGN KEY (BranchId)      REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_Bookings_Customers    FOREIGN KEY (CustomerId)    REFERENCES db65922.dbo.Customers(CustomerId),
    CONSTRAINT FK_Bookings_Services     FOREIGN KEY (ServiceId)     REFERENCES db65922.dbo.Services(ServiceId),
    CONSTRAINT FK_Bookings_Ports        FOREIGN KEY (PortId)        REFERENCES db65922.dbo.Ports(PortId),
    CONSTRAINT FK_Bookings_Destinations FOREIGN KEY (DestinationId) REFERENCES db65922.dbo.Destinations(DestinationId),
    CONSTRAINT FK_Bookings_TripTypes    FOREIGN KEY (TripTypeId)    REFERENCES db65922.dbo.TripTypes(TripTypeId),
    CONSTRAINT FK_Bookings_Contacts     FOREIGN KEY (ContactId)     REFERENCES db65922.dbo.CustomerContacts(CustomerContactId)
);
GO

CREATE TABLE db65922.dbo.Containers (
    ContainerId     bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Containers PRIMARY KEY,
    ContainerNumber nvarchar(20)  NOT NULL,
    ContainerTypeId int NOT NULL,
    OwnerType       nvarchar(20)  NOT NULL CONSTRAINT DF_Containers_Owner DEFAULT N'ShippingLine',
    OwnerName       nvarchar(150) NULL,
    WeightKg        decimal(18,3) NULL CONSTRAINT CK_Containers_Weight CHECK (WeightKg IS NULL OR WeightKg >= 0),
    CurrentStatus   nvarchar(30)  NOT NULL CONSTRAINT DF_Containers_Status DEFAULT N'Available',
    Notes           nvarchar(500) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Containers_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Containers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Containers_Number UNIQUE (ContainerNumber),
    CONSTRAINT CK_Containers_Owner  CHECK (OwnerType IN (N'ShippingLine',N'Customer',N'Leased',N'Company')),
    CONSTRAINT CK_Containers_Status CHECK (CurrentStatus IN (N'Available',N'InTransit',N'AtPort',N'AtDepot',N'Delivered',N'Returned')),
    CONSTRAINT FK_Containers_Types  FOREIGN KEY (ContainerTypeId) REFERENCES db65922.dbo.ContainerTypes(ContainerTypeId)
);
GO

-- A3: نفس الحاوية بتيجي النهاردة وبترجع الشهر الجاي — ده تاريخ الحركات
CREATE TABLE db65922.dbo.ContainerMovements (
    ContainerMovementId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContainerMovements PRIMARY KEY,
    ContainerId     bigint NOT NULL,
    MovementType    nvarchar(30)  NOT NULL,
    BookingId       bigint NULL,
    OperationId     bigint NULL,
    PortId          int NULL,
    MovementDate    datetime2(0) NOT NULL CONSTRAINT DF_ContainerMovements_Date DEFAULT SYSUTCDATETIME(),
    Notes           nvarchar(500) NULL,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_ContainerMovements_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT CK_ContainerMovements_Type CHECK (MovementType IN (N'Inbound',N'Outbound',N'Pickup',N'Delivery',N'Return',N'GateIn',N'GateOut')),
    CONSTRAINT FK_ContainerMovements_Containers FOREIGN KEY (ContainerId) REFERENCES db65922.dbo.Containers(ContainerId),
    CONSTRAINT FK_ContainerMovements_Ports      FOREIGN KEY (PortId)      REFERENCES db65922.dbo.Ports(PortId)
);
GO

-- A3 + D2: الحجز بالعدد والنوع — الأرقام اختيارية
CREATE TABLE db65922.dbo.BookingContainerLines (
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
    CONSTRAINT FK_BCL_Bookings       FOREIGN KEY (BookingId)       REFERENCES db65922.dbo.Bookings(BookingId),
    CONSTRAINT FK_BCL_ContainerTypes FOREIGN KEY (ContainerTypeId) REFERENCES db65922.dbo.ContainerTypes(ContainerTypeId)
);
GO

-- A3: الحاوية الفعلية لما الرقم يتعرف
CREATE TABLE db65922.dbo.BookingContainerDetails (
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
    CONSTRAINT FK_BCD_Lines      FOREIGN KEY (BookingContainerLineId) REFERENCES db65922.dbo.BookingContainerLines(BookingContainerLineId),
    CONSTRAINT FK_BCD_Containers FOREIGN KEY (ContainerId)            REFERENCES db65922.dbo.Containers(ContainerId)
);
GO


/* =====================================================================================
   SECTION 08 — OPERATIONS / TRIPS   🔴 أهم جزء في النظام
   ===================================================================================== */

CREATE TABLE db65922.dbo.Operations (
    OperationId     bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Operations PRIMARY KEY,
    OperationNumber nvarchar(40)  NOT NULL,
    BranchId        int NOT NULL,
    BookingId       bigint NOT NULL,
    CustomerId      int NOT NULL,
    ServiceId       int NULL,
    PortId          int NULL,
    DestinationId   int NULL,
    TripTypeId      int NULL,
    PlannedDate     datetime2(0) NULL,
    ActualStartAt   datetime2(0) NULL,
    ActualDeliveryAt datetime2(0) NULL,
    Status          nvarchar(30) NOT NULL CONSTRAINT DF_Operations_Status DEFAULT N'Pending',
    -- A9: RevenueNet / RevenueTax بتتحسب بـ Trigger من OperationRevenueItems
    RevenueNet      decimal(19,4) NOT NULL CONSTRAINT DF_Operations_RevNet DEFAULT 0 CONSTRAINT CK_Operations_RevNet CHECK (RevenueNet >= 0),
    RevenueTax      decimal(19,4) NOT NULL CONSTRAINT DF_Operations_RevTax DEFAULT 0 CONSTRAINT CK_Operations_RevTax CHECK (RevenueTax >= 0),
    EstimatedCost   decimal(19,4) NOT NULL CONSTRAINT DF_Operations_EstCost DEFAULT 0 CONSTRAINT CK_Operations_EstCost CHECK (EstimatedCost >= 0),
    -- A10: التكلفة الفعلية بتتحسب بـ Trigger
    ActualCost      decimal(19,4) NOT NULL CONSTRAINT DF_Operations_ActCost DEFAULT 0 CONSTRAINT CK_Operations_ActCost CHECK (ActualCost >= 0),
    Notes           nvarchar(1000) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Operations_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Operations_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    ClosedAt        datetime2(0) NULL,
    ClosedBy        int NULL,
    CONSTRAINT UQ_Operations_Number UNIQUE (OperationNumber),
    CONSTRAINT CK_Operations_Status CHECK (Status IN (
        N'Pending',N'Assigned',N'DriverReceived',N'InTransit',N'Delivered',
        N'ExpensesPending',N'CustodyPending',N'ReadyToClose',N'Closed',N'Cancelled')),
    CONSTRAINT FK_Operations_Branches     FOREIGN KEY (BranchId)      REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_Operations_Bookings     FOREIGN KEY (BookingId)     REFERENCES db65922.dbo.Bookings(BookingId),
    CONSTRAINT FK_Operations_Customers    FOREIGN KEY (CustomerId)    REFERENCES db65922.dbo.Customers(CustomerId),
    CONSTRAINT FK_Operations_Services     FOREIGN KEY (ServiceId)     REFERENCES db65922.dbo.Services(ServiceId),
    CONSTRAINT FK_Operations_Ports        FOREIGN KEY (PortId)        REFERENCES db65922.dbo.Ports(PortId),
    CONSTRAINT FK_Operations_Destinations FOREIGN KEY (DestinationId) REFERENCES db65922.dbo.Destinations(DestinationId),
    CONSTRAINT FK_Operations_TripTypes    FOREIGN KEY (TripTypeId)    REFERENCES db65922.dbo.TripTypes(TripTypeId)
);
GO

-- 🔴 A2 + D1: الرحلة كيان مستقل — مافيش OperationId
CREATE TABLE db65922.dbo.Trips (
    TripId          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Trips PRIMARY KEY,
    TripNumber      nvarchar(40)  NOT NULL,
    BranchId        int NOT NULL,
    DriverId        int NOT NULL,               -- ⚠️ كان nullable في v1
    VehicleId       int NOT NULL,               -- ⚠️ كان nullable في v1
    TrailerId       int NULL,
    TripTypeId      int NULL,
    PlannedStartAt  datetime2(0) NULL,
    ActualStartAt   datetime2(0) NULL,
    ActualEndAt     datetime2(0) NULL,
    StartOdometer   decimal(18,1) NULL,
    EndOdometer     decimal(18,1) NULL,
    TotalDistanceKm decimal(18,1) NULL,
    Status          nvarchar(30) NOT NULL CONSTRAINT DF_Trips_Status DEFAULT N'Planned',
    Notes           nvarchar(1000) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Trips_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Trips_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Trips_Number UNIQUE (TripNumber),
    CONSTRAINT CK_Trips_Status CHECK (Status IN (N'Planned',N'Assigned',N'Started',N'Completed',N'Cancelled')),
    CONSTRAINT CK_Trips_Odometer CHECK (EndOdometer IS NULL OR StartOdometer IS NULL OR EndOdometer >= StartOdometer),
    CONSTRAINT CK_Trips_Dates CHECK (ActualEndAt IS NULL OR ActualStartAt IS NULL OR ActualEndAt >= ActualStartAt),
    CONSTRAINT FK_Trips_Branches  FOREIGN KEY (BranchId)   REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_Trips_Drivers   FOREIGN KEY (DriverId)   REFERENCES db65922.dbo.Drivers(DriverId),
    CONSTRAINT FK_Trips_Vehicles  FOREIGN KEY (VehicleId)  REFERENCES db65922.dbo.Vehicles(VehicleId),
    CONSTRAINT FK_Trips_Trailers  FOREIGN KEY (TrailerId)  REFERENCES db65922.dbo.Trailers(TrailerId),
    CONSTRAINT FK_Trips_TripTypes FOREIGN KEY (TripTypeId) REFERENCES db65922.dbo.TripTypes(TripTypeId)
);
GO

-- 🔴 A2: ده الجدول اللي بيخلّي التجميع ممكن — رحلة ← كذا عملية
CREATE TABLE db65922.dbo.TripOperations (
    TripOperationId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TripOperations PRIMARY KEY,
    TripId          bigint NOT NULL,
    OperationId     bigint NOT NULL,
    SequenceNo      int NOT NULL CONSTRAINT DF_TripOperations_Seq DEFAULT 1,
    PickupAt        datetime2(0) NULL,
    DeliveryAt      datetime2(0) NULL,
    Status          nvarchar(30) NOT NULL CONSTRAINT DF_TripOperations_Status DEFAULT N'Assigned',
    Notes           nvarchar(500) NULL,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_TripOperations_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT UQ_TripOperations UNIQUE (TripId, OperationId),
    CONSTRAINT CK_TripOperations_Status CHECK (Status IN (N'Assigned',N'PickedUp',N'Delivered',N'Cancelled')),
    CONSTRAINT FK_TripOperations_Trip      FOREIGN KEY (TripId)      REFERENCES db65922.dbo.Trips(TripId),
    CONSTRAINT FK_TripOperations_Operation FOREIGN KEY (OperationId) REFERENCES db65922.dbo.Operations(OperationId)
);
GO

-- 🔴 A10 + X1 (manual): توزيع تكلفة الرحلة على العمليات — يدوي
CREATE TABLE db65922.dbo.TripCostAllocations (
    TripCostAllocationId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TripCostAllocations PRIMARY KEY,
    TripId          bigint NOT NULL,
    OperationId     bigint NOT NULL,
    AllocationBasis nvarchar(20) NOT NULL CONSTRAINT DF_TCA_Basis DEFAULT N'Manual',
    AllocationPercent decimal(9,4) NOT NULL CONSTRAINT DF_TCA_Percent DEFAULT 0 CONSTRAINT CK_TCA_Percent CHECK (AllocationPercent >= 0 AND AllocationPercent <= 100),
    AllocatedAmount decimal(19,4) NOT NULL CONSTRAINT DF_TCA_Amount DEFAULT 0 CONSTRAINT CK_TCA_Amount CHECK (AllocatedAmount >= 0),
    Notes           nvarchar(500) NULL,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_TCA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    CONSTRAINT UQ_TripCostAllocations UNIQUE (TripId, OperationId),
    CONSTRAINT CK_TCA_Basis CHECK (AllocationBasis IN (N'Manual',N'Weight',N'ContainerCount',N'RevenueShare')),
    CONSTRAINT FK_TCA_Trip      FOREIGN KEY (TripId)      REFERENCES db65922.dbo.Trips(TripId),
    CONSTRAINT FK_TCA_Operation FOREIGN KEY (OperationId) REFERENCES db65922.dbo.Operations(OperationId)
);
GO

CREATE TABLE db65922.dbo.OperationContainers (
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
    CONSTRAINT FK_OperationContainers_Operations FOREIGN KEY (OperationId)            REFERENCES db65922.dbo.Operations(OperationId),
    CONSTRAINT FK_OperationContainers_Containers FOREIGN KEY (ContainerId)            REFERENCES db65922.dbo.Containers(ContainerId),
    CONSTRAINT FK_OperationContainers_Details    FOREIGN KEY (BookingContainerDetailId) REFERENCES db65922.dbo.BookingContainerDetails(BookingContainerDetailId)
);
GO

CREATE TABLE db65922.dbo.OperationRevenueItems (
    OperationRevenueItemId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_OperationRevenueItems PRIMARY KEY,
    OperationId     bigint NOT NULL,
    ServiceId       int NOT NULL,
    PriceRuleId     bigint NULL,                -- B12: تتبع مصدر السعر
    Description     nvarchar(300) NULL,
    Quantity        decimal(18,3) NOT NULL CONSTRAINT CK_ORI_Qty CHECK (Quantity > 0),
    UnitPrice       decimal(19,4) NOT NULL CONSTRAINT CK_ORI_Price CHECK (UnitPrice >= 0),
    Discount        decimal(19,4) NOT NULL CONSTRAINT DF_ORI_Discount DEFAULT 0 CONSTRAINT CK_ORI_Discount CHECK (Discount >= 0),
    TaxRateId       int NULL,
    -- ⚠️ TaxRate متخزنة هنا كـ Denormalized Snapshot — لأن Computed Column
    --    في SQL Server ماينفعش يشير لجدول تاني (db65922.dbo.TaxRates).
    --    التطبيق بينسخ القيمة من TaxRates عند الحفظ.
    TaxRate         decimal(9,4) NOT NULL CONSTRAINT DF_ORI_TaxRate DEFAULT 0 CONSTRAINT CK_ORI_TaxRate CHECK (TaxRate >= 0),
    -- Row-local → PERSISTED مسموح بيه
    LineNet   AS CONVERT(decimal(19,4), (Quantity * UnitPrice) - Discount) PERSISTED,
    LineTax   AS CONVERT(decimal(19,4), ((Quantity * UnitPrice) - Discount) * TaxRate / 100.0) PERSISTED,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_ORI_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT FK_ORI_Operations FOREIGN KEY (OperationId) REFERENCES db65922.dbo.Operations(OperationId),
    CONSTRAINT FK_ORI_Services   FOREIGN KEY (ServiceId)   REFERENCES db65922.dbo.Services(ServiceId),
    CONSTRAINT FK_ORI_PriceRules FOREIGN KEY (PriceRuleId) REFERENCES db65922.dbo.CustomerPriceRules(CustomerPriceRuleId),
    CONSTRAINT FK_ORI_TaxRates   FOREIGN KEY (TaxRateId)   REFERENCES db65922.dbo.TaxRates(TaxRateId)
);
GO

/* =====================================================================================
   SECTION 09 — CUSTODY   (A8: CustodyHolders اتلغى، ومصدر واحد للحقيقة)
   ===================================================================================== */

CREATE TABLE db65922.dbo.DriverCustodies (
    CustodyId       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DriverCustodies PRIMARY KEY,
    CustodyNumber   nvarchar(40)  NOT NULL,
    BranchId        int NOT NULL,
    TripId          bigint NOT NULL,            -- 🔴 A8+D1: العهدة على الرحلة (مش nullable زي v1)
    OwnerType       nvarchar(20)  NOT NULL CONSTRAINT DF_Custodies_OwnerType DEFAULT N'Driver',
    OwnerId         int NOT NULL,               -- DriverId أو EmployeeId حسب OwnerType
    CustodyDate     datetime2(0) NOT NULL CONSTRAINT DF_Custodies_Date DEFAULT SYSUTCDATETIME(),
    AmountIssued    decimal(19,4) NOT NULL CONSTRAINT CK_Custodies_Issued CHECK (AmountIssued >= 0),
    -- 🔴 A8: بتتحسب بـ Trigger من CustodyTransactions — مصدر واحد للحقيقة
    AmountSpent     decimal(19,4) NOT NULL CONSTRAINT DF_Custodies_Spent DEFAULT 0 CONSTRAINT CK_Custodies_Spent CHECK (AmountSpent >= 0),
    AmountReturned  decimal(19,4) NOT NULL CONSTRAINT DF_Custodies_Returned DEFAULT 0 CONSTRAINT CK_Custodies_Returned CHECK (AmountReturned >= 0),
    AdditionalDue   decimal(19,4) NOT NULL CONSTRAINT DF_Custodies_Additional DEFAULT 0 CONSTRAINT CK_Custodies_Additional CHECK (AdditionalDue >= 0),
    Status          nvarchar(30) NOT NULL CONSTRAINT DF_Custodies_Status DEFAULT N'Open',
    Notes           nvarchar(500) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Custodies_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Custodies_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    ClosedAt        datetime2(0) NULL,
    ClosedBy        int NULL,
    CONSTRAINT UQ_DriverCustodies_Number UNIQUE (CustodyNumber),
    CONSTRAINT CK_Custodies_OwnerType CHECK (OwnerType IN (N'Driver',N'Employee')),
    CONSTRAINT CK_Custodies_Status CHECK (Status IN (N'Open',N'PartiallySettled',N'Submitted',N'Approved',N'Closed')),
    -- 🔴 قسم 22: مانرجّعش أكتر من اللي اتصرف ناقص المصروفات
    CONSTRAINT CK_Custodies_NoOverRefund CHECK (AmountReturned <= AmountIssued - AmountSpent + AdditionalDue),
    CONSTRAINT FK_Custodies_Branches FOREIGN KEY (BranchId) REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_Custodies_Trips    FOREIGN KEY (TripId)   REFERENCES db65922.dbo.Trips(TripId)
);
GO

CREATE TABLE db65922.dbo.CustodyTransactions (
    CustodyTransactionId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustodyTransactions PRIMARY KEY,
    CustodyId       bigint NOT NULL,
    ExpenseId       bigint NULL,
    TransactionType nvarchar(20)  NOT NULL,
    Amount          decimal(19,4) NOT NULL CONSTRAINT CK_CustodyTx_Amount CHECK (Amount > 0),
    TransactionDate datetime2(0) NOT NULL CONSTRAINT DF_CustodyTx_Date DEFAULT SYSUTCDATETIME(),
    Notes           nvarchar(500) NULL,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_CustodyTx_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT CK_CustodyTx_Type CHECK (TransactionType IN (N'Issue',N'Expense',N'Refund',N'Additional')),
    CONSTRAINT FK_CustodyTx_Custody FOREIGN KEY (CustodyId) REFERENCES db65922.dbo.DriverCustodies(CustodyId)
);
GO

/* =====================================================================================
   SECTION 10 — EXPENSES
   ===================================================================================== */

CREATE TABLE db65922.dbo.Expenses (
    ExpenseId       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Expenses PRIMARY KEY,
    ExpenseNumber   nvarchar(40)  NOT NULL,
    BranchId        int NOT NULL,
    OperationId     bigint NULL,
    TripId          bigint NULL,
    CustodyId       bigint NULL,
    DriverId        int NULL,                   -- B5: كان ناقص
    SupplierId      int NULL,
    ExpenseTypeId   int NOT NULL,
    ExpenseDate     datetime2(0) NOT NULL CONSTRAINT DF_Expenses_Date DEFAULT SYSUTCDATETIME(),
    Description     nvarchar(500) NULL,
    Amount          decimal(19,4) NOT NULL CONSTRAINT CK_Expenses_Amount CHECK (Amount > 0),
    TaxRateId       int NULL,
    TaxRate         decimal(9,4) NOT NULL CONSTRAINT DF_Expenses_TaxRate DEFAULT 0 CONSTRAINT CK_Expenses_TaxRate CHECK (TaxRate >= 0),
    IsTaxDeductible bit NOT NULL CONSTRAINT DF_Expenses_Deductible DEFAULT 1,   -- A4: ضريبة الجدول = 0
    ReferenceNumber nvarchar(100) NULL,
    PaymentStatus   nvarchar(20) NOT NULL CONSTRAINT DF_Expenses_PayStatus DEFAULT N'Unpaid',
    Status          nvarchar(20) NOT NULL CONSTRAINT DF_Expenses_Status DEFAULT N'Posted',
    IsApproved      bit NOT NULL CONSTRAINT DF_Expenses_Approved DEFAULT 0,     -- A6: EXPENSE.APPROVE
    ApprovedBy      int NULL,
    ApprovedAt      datetime2(0) NULL,
    Notes           nvarchar(500) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Expenses_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Expenses_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Expenses_Number UNIQUE (ExpenseNumber),
    CONSTRAINT CK_Expenses_PayStatus CHECK (PaymentStatus IN (N'Unpaid',N'PartiallyPaid',N'Paid')),
    CONSTRAINT CK_Expenses_Status    CHECK (Status IN (N'Draft',N'Posted',N'Cancelled')),
    CONSTRAINT FK_Expenses_Branches  FOREIGN KEY (BranchId)      REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_Expenses_Operations FOREIGN KEY (OperationId)  REFERENCES db65922.dbo.Operations(OperationId),
    CONSTRAINT FK_Expenses_Trips     FOREIGN KEY (TripId)        REFERENCES db65922.dbo.Trips(TripId),
    CONSTRAINT FK_Expenses_Custodies FOREIGN KEY (CustodyId)     REFERENCES db65922.dbo.DriverCustodies(CustodyId),
    CONSTRAINT FK_Expenses_Drivers   FOREIGN KEY (DriverId)      REFERENCES db65922.dbo.Drivers(DriverId),
    CONSTRAINT FK_Expenses_Suppliers FOREIGN KEY (SupplierId)    REFERENCES db65922.dbo.Suppliers(SupplierId),
    CONSTRAINT FK_Expenses_Types     FOREIGN KEY (ExpenseTypeId) REFERENCES db65922.dbo.ExpenseTypes(ExpenseTypeId),
    CONSTRAINT FK_Expenses_TaxRates  FOREIGN KEY (TaxRateId)     REFERENCES db65922.dbo.TaxRates(TaxRateId)
);
GO

ALTER TABLE db65922.dbo.CustodyTransactions
    ADD CONSTRAINT FK_CustodyTx_Expense FOREIGN KEY (ExpenseId) REFERENCES db65922.dbo.Expenses(ExpenseId);
GO

/* =====================================================================================
   SECTION 11 — SALES / RECEIVABLES
   ===================================================================================== */

CREATE TABLE db65922.dbo.Invoices (
    InvoiceId       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Invoices PRIMARY KEY,
    InvoiceNumber   nvarchar(40)  NOT NULL,
    BranchId        int NOT NULL,
    CustomerId      int NOT NULL,
    InvoiceDate     date NOT NULL,
    DueDate         date NULL,
    InvoiceType     nvarchar(20)  NOT NULL,           -- Tax / NonTax
    DocumentTypeCode nvarchar(20) NOT NULL CONSTRAINT DF_Invoices_DocType DEFAULT N'Invoice',  -- A4
    OriginalInvoiceId bigint NULL,                    -- A4: للـ Credit Note
    ReasonCode      nvarchar(20)  NULL,               -- A4: سبب الـ Credit Note
    Status          nvarchar(30) NOT NULL CONSTRAINT DF_Invoices_Status DEFAULT N'Draft',
    PaymentStatus   nvarchar(30) NOT NULL CONSTRAINT DF_Invoices_PayStatus DEFAULT N'Unpaid',
    CurrencyCode    char(3) NOT NULL CONSTRAINT DF_Invoices_Currency DEFAULT 'EGP',
    SubTotal        decimal(19,4) NOT NULL CONSTRAINT DF_Invoices_SubTotal DEFAULT 0,
    DiscountTotal   decimal(19,4) NOT NULL CONSTRAINT DF_Invoices_Discount DEFAULT 0,
    TaxTotal        decimal(19,4) NOT NULL CONSTRAINT DF_Invoices_Tax DEFAULT 0,
    GrandTotal      decimal(19,4) NOT NULL CONSTRAINT DF_Invoices_GrandTotal DEFAULT 0,
    -- A1: بتتحسب بـ Trigger من Payments
    PaidAmount      decimal(19,4) NOT NULL CONSTRAINT DF_Invoices_Paid DEFAULT 0,
    Notes           nvarchar(1000) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Invoices_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Invoices_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    IssuedAt        datetime2(0) NULL,
    IssuedBy        int NULL,
    CONSTRAINT UQ_Invoices_Number UNIQUE (InvoiceNumber),
    CONSTRAINT CK_Invoices_Type     CHECK (InvoiceType IN (N'Tax',N'NonTax')),
    CONSTRAINT CK_Invoices_DocType  CHECK (DocumentTypeCode IN (N'Invoice',N'CreditNote',N'DebitNote')),
    CONSTRAINT CK_Invoices_Status   CHECK (Status IN (N'Draft',N'Approved',N'Issued',N'Sent',N'Cancelled',N'Returned')),
    CONSTRAINT CK_Invoices_PayStatus CHECK (PaymentStatus IN (N'Unpaid',N'PartiallyPaid',N'Paid',N'Overpaid')),
    -- 🔴 A4: الـ v1 كان بيمنع القيم السالبة تمامًا → Credit Note مستحيلة
    --       دلوقتي: الفاتورة العادية موجبة، والـ CreditNote سالبة
    CONSTRAINT CK_Invoices_Sign CHECK (
        (DocumentTypeCode = N'CreditNote' AND GrandTotal <= 0)
        OR (DocumentTypeCode <> N'CreditNote' AND GrandTotal >= 0)),
    -- Credit Note لازم تشير لفاتورة أصلية
    CONSTRAINT CK_Invoices_CreditNeedsOriginal CHECK (DocumentTypeCode <> N'CreditNote' OR OriginalInvoiceId IS NOT NULL),
    CONSTRAINT FK_Invoices_Branches  FOREIGN KEY (BranchId)         REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_Invoices_Customers FOREIGN KEY (CustomerId)       REFERENCES db65922.dbo.Customers(CustomerId),
    CONSTRAINT FK_Invoices_Original  FOREIGN KEY (OriginalInvoiceId) REFERENCES db65922.dbo.Invoices(InvoiceId)
);
GO

CREATE TABLE db65922.dbo.InvoiceItems (
    InvoiceItemId   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InvoiceItems PRIMARY KEY,
    InvoiceId       bigint NOT NULL,
    OperationId     bigint NULL,
    ServiceId       int NULL,
    PriceRuleId     bigint NULL,                -- B12: تتبع مصدر السعر
    Description     nvarchar(500) NOT NULL,
    -- 🔴 A4: Quantity <> 0 بدل > 0 — عشان الـ Credit Note
    Quantity        decimal(18,3) NOT NULL CONSTRAINT CK_InvoiceItems_Qty CHECK (Quantity <> 0),
    UnitPrice       decimal(19,4) NOT NULL CONSTRAINT CK_InvoiceItems_Price CHECK (UnitPrice >= 0),
    Discount        decimal(19,4) NOT NULL CONSTRAINT DF_InvoiceItems_Discount DEFAULT 0,
    TaxRateId       int NULL,
    TaxRate         decimal(9,4) NOT NULL CONSTRAINT DF_InvoiceItems_TaxRate DEFAULT 0 CONSTRAINT CK_InvoiceItems_TaxRate CHECK (TaxRate >= 0),
    -- Row-local → PERSISTED
    LineSubtotal AS CONVERT(decimal(19,4), (Quantity * UnitPrice) - Discount) PERSISTED,
    LineTax      AS CONVERT(decimal(19,4), (((Quantity * UnitPrice) - Discount) * TaxRate / 100.0)) PERSISTED,
    LineTotal    AS CONVERT(decimal(19,4), (((Quantity * UnitPrice) - Discount) * (1 + TaxRate / 100.0))) PERSISTED,
    CONSTRAINT FK_InvoiceItems_Invoices   FOREIGN KEY (InvoiceId)   REFERENCES db65922.dbo.Invoices(InvoiceId),
    CONSTRAINT FK_InvoiceItems_Operations FOREIGN KEY (OperationId) REFERENCES db65922.dbo.Operations(OperationId),
    CONSTRAINT FK_InvoiceItems_Services   FOREIGN KEY (ServiceId)   REFERENCES db65922.dbo.Services(ServiceId),
    CONSTRAINT FK_InvoiceItems_PriceRules FOREIGN KEY (PriceRuleId) REFERENCES db65922.dbo.CustomerPriceRules(CustomerPriceRuleId),
    CONSTRAINT FK_InvoiceItems_TaxRates   FOREIGN KEY (TaxRateId)   REFERENCES db65922.dbo.TaxRates(TaxRateId)
);
GO

CREATE TABLE db65922.dbo.InvoiceOperations (
    InvoiceOperationId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InvoiceOperations PRIMARY KEY,
    InvoiceId       bigint NOT NULL,
    OperationId     bigint NOT NULL,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_InvoiceOperations_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT UQ_InvoiceOperations UNIQUE (InvoiceId, OperationId),
    CONSTRAINT FK_InvoiceOperations_Invoices   FOREIGN KEY (InvoiceId)   REFERENCES db65922.dbo.Invoices(InvoiceId),
    CONSTRAINT FK_InvoiceOperations_Operations FOREIGN KEY (OperationId) REFERENCES db65922.dbo.Operations(OperationId)
);
GO

/* =====================================================================================
   SECTION 12 — PAYMENTS   (🔴 A1: كانت مش موجودة خالص في v1)
   ===================================================================================== */

CREATE TABLE db65922.dbo.Payments (
    PaymentId       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payments PRIMARY KEY,
    PaymentNumber   nvarchar(40)  NOT NULL,
    BranchId        int NOT NULL,
    CustomerId      int NOT NULL,
    PaymentDate     date NOT NULL,
    PaymentMethodId int NOT NULL,
    Amount          decimal(19,4) NOT NULL CONSTRAINT CK_Payments_Amount CHECK (Amount > 0),
    ChequeNumber    nvarchar(50)  NULL,
    ChequeDate      date NULL,
    BankAccount     nvarchar(100) NULL,
    Status          nvarchar(20) NOT NULL CONSTRAINT DF_Payments_Status DEFAULT N'Posted',
    ReversedByPaymentId bigint NULL,            -- A1: العكس
    CashTransactionId bigint NULL,              -- الربط بالأثر النقدي
    Notes           nvarchar(500) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Payments_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Payments_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT UQ_Payments_Number UNIQUE (PaymentNumber),
    CONSTRAINT CK_Payments_Status CHECK (Status IN (N'Posted',N'Cancelled',N'Returned')),
    -- شيك لازم يكون له رقم
    CONSTRAINT CK_Payments_ChequeNeedsNo CHECK (ChequeDate IS NULL OR ChequeNumber IS NOT NULL),
    CONSTRAINT FK_Payments_Branches  FOREIGN KEY (BranchId)        REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_Payments_Customers FOREIGN KEY (CustomerId)      REFERENCES db65922.dbo.Customers(CustomerId),
    CONSTRAINT FK_Payments_Methods   FOREIGN KEY (PaymentMethodId) REFERENCES db65922.dbo.PaymentMethods(PaymentMethodId),
    CONSTRAINT FK_Payments_Reversed  FOREIGN KEY (ReversedByPaymentId) REFERENCES db65922.dbo.Payments(PaymentId)
);
GO

-- A1: دفعة 50,000 على 3 فواتير
CREATE TABLE db65922.dbo.PaymentAllocations (
    PaymentAllocationId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentAllocations PRIMARY KEY,
    PaymentId       bigint NOT NULL,
    InvoiceId       bigint NOT NULL,
    AllocatedAmount decimal(19,4) NOT NULL CONSTRAINT CK_PaymentAlloc_Amount CHECK (AllocatedAmount <> 0),
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_PaymentAlloc_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT UQ_PaymentAllocations UNIQUE (PaymentId, InvoiceId),
    CONSTRAINT FK_PaymentAlloc_Payments FOREIGN KEY (PaymentId) REFERENCES db65922.dbo.Payments(PaymentId),
    CONSTRAINT FK_PaymentAlloc_Invoices FOREIGN KEY (InvoiceId) REFERENCES db65922.dbo.Invoices(InvoiceId)
);
GO

-- B3: الـ Prompt قسم 29 — Sent Date / Sent By / Recipient / Send Status / Error Message
CREATE TABLE db65922.dbo.InvoiceSendLogs (
    InvoiceSendLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InvoiceSendLogs PRIMARY KEY,
    InvoiceId       bigint NOT NULL,
    SentAt          datetime2(0) NOT NULL CONSTRAINT DF_InvoiceSendLogs_At DEFAULT SYSUTCDATETIME(),
    SentBy          int NULL,
    Recipient       nvarchar(254) NOT NULL,
    Channel         nvarchar(20)  NOT NULL CONSTRAINT DF_InvoiceSendLogs_Channel DEFAULT N'Email',
    Status          nvarchar(20)  NOT NULL CONSTRAINT DF_InvoiceSendLogs_Status DEFAULT N'Success',
    ErrorMessage    nvarchar(1000) NULL,
    AttachmentPath  nvarchar(500) NULL,
    CONSTRAINT CK_InvoiceSendLogs_Channel CHECK (Channel IN (N'Email',N'WhatsApp',N'Portal',N'Fax',N'Manual')),
    CONSTRAINT CK_InvoiceSendLogs_Status  CHECK (Status IN (N'Success',N'Failed',N'Pending')),
    CONSTRAINT FK_InvoiceSendLogs_Invoices FOREIGN KEY (InvoiceId) REFERENCES db65922.dbo.Invoices(InvoiceId)
);
GO

/* =====================================================================================
   SECTION 13 — SUPPLIER PAYABLES   (🔴 A11 + D4: أسطول مختلط)
   ===================================================================================== */

CREATE TABLE db65922.dbo.SupplierInvoices (
    SupplierInvoiceId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierInvoices PRIMARY KEY,
    SupplierInvoiceNumber nvarchar(40) NOT NULL,      -- داخلي
    SupplierRefNumber nvarchar(100) NULL,             -- رقم المورد
    BranchId        int NOT NULL,
    SupplierId      int NOT NULL,
    InvoiceDate     date NOT NULL,
    DueDate         date NULL,
    CurrencyCode    char(3) NOT NULL CONSTRAINT DF_SupplierInvoices_Currency DEFAULT 'EGP',
    SubTotal        decimal(19,4) NOT NULL CONSTRAINT DF_SupplierInvoices_Sub DEFAULT 0,
    TaxTotal        decimal(19,4) NOT NULL CONSTRAINT DF_SupplierInvoices_Tax DEFAULT 0,
    GrandTotal      decimal(19,4) NOT NULL CONSTRAINT DF_SupplierInvoices_Total DEFAULT 0,
    PaidAmount      decimal(19,4) NOT NULL CONSTRAINT DF_SupplierInvoices_Paid DEFAULT 0,
    Status          nvarchar(30) NOT NULL CONSTRAINT DF_SupplierInvoices_Status DEFAULT N'Draft',
    PaymentStatus   nvarchar(20) NOT NULL CONSTRAINT DF_SupplierInvoices_PayStatus DEFAULT N'Unpaid',
    Notes           nvarchar(1000) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_SupplierInvoices_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_SupplierInvoices_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    ApprovedAt      datetime2(0) NULL,
    ApprovedBy      int NULL,
    CONSTRAINT UQ_SupplierInvoices_Number UNIQUE (SupplierInvoiceNumber),
    CONSTRAINT CK_SupplierInvoices_Status CHECK (Status IN (N'Draft',N'Approved',N'PartiallyPaid',N'Paid',N'Cancelled')),
    CONSTRAINT CK_SupplierInvoices_PayStatus CHECK (PaymentStatus IN (N'Unpaid',N'PartiallyPaid',N'Paid')),
    CONSTRAINT FK_SupplierInvoices_Branches  FOREIGN KEY (BranchId)   REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_SupplierInvoices_Suppliers FOREIGN KEY (SupplierId) REFERENCES db65922.dbo.Suppliers(SupplierId)
);
GO

CREATE TABLE db65922.dbo.SupplierInvoiceItems (
    SupplierInvoiceItemId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierInvoiceItems PRIMARY KEY,
    SupplierInvoiceId bigint NOT NULL,
    TripId          bigint NULL,
    OperationId     bigint NULL,
    ExpenseTypeId   int NULL,
    Description     nvarchar(500) NOT NULL,
    Quantity        decimal(18,3) NOT NULL CONSTRAINT CK_SII_Qty CHECK (Quantity > 0),
    UnitPrice       decimal(19,4) NOT NULL CONSTRAINT CK_SII_Price CHECK (UnitPrice >= 0),
    TaxRate         decimal(9,4) NOT NULL CONSTRAINT DF_SII_TaxRate DEFAULT 0 CONSTRAINT CK_SII_TaxRate CHECK (TaxRate >= 0),
    LineSubtotal AS CONVERT(decimal(19,4), Quantity * UnitPrice) PERSISTED,
    LineTax      AS CONVERT(decimal(19,4), (Quantity * UnitPrice) * TaxRate / 100.0) PERSISTED,
    LineTotal    AS CONVERT(decimal(19,4), (Quantity * UnitPrice) * (1 + TaxRate / 100.0)) PERSISTED,
    CONSTRAINT FK_SII_Invoices   FOREIGN KEY (SupplierInvoiceId) REFERENCES db65922.dbo.SupplierInvoices(SupplierInvoiceId),
    CONSTRAINT FK_SII_Trips      FOREIGN KEY (TripId)            REFERENCES db65922.dbo.Trips(TripId),
    CONSTRAINT FK_SII_Operations FOREIGN KEY (OperationId)       REFERENCES db65922.dbo.Operations(OperationId),
    CONSTRAINT FK_SII_Types      FOREIGN KEY (ExpenseTypeId)     REFERENCES db65922.dbo.ExpenseTypes(ExpenseTypeId)
);
GO

CREATE TABLE db65922.dbo.SupplierPayments (
    SupplierPaymentId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierPayments PRIMARY KEY,
    SupplierPaymentNumber nvarchar(40) NOT NULL,
    BranchId        int NOT NULL,
    SupplierId      int NOT NULL,
    PaymentDate     date NOT NULL,
    PaymentMethodId int NOT NULL,
    Amount          decimal(19,4) NOT NULL CONSTRAINT CK_SupplierPayments_Amount CHECK (Amount > 0),
    ChequeNumber    nvarchar(50)  NULL,
    ChequeDate      date NULL,
    BankAccount     nvarchar(100) NULL,
    Status          nvarchar(20) NOT NULL CONSTRAINT DF_SupplierPayments_Status DEFAULT N'Posted',
    CashTransactionId bigint NULL,
    Notes           nvarchar(500) NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_SupplierPayments_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_SupplierPayments_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    CONSTRAINT UQ_SupplierPayments_Number UNIQUE (SupplierPaymentNumber),
    CONSTRAINT CK_SupplierPayments_Status CHECK (Status IN (N'Posted',N'Cancelled',N'Returned')),
    CONSTRAINT FK_SupplierPayments_Branches  FOREIGN KEY (BranchId)        REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_SupplierPayments_Suppliers FOREIGN KEY (SupplierId)      REFERENCES db65922.dbo.Suppliers(SupplierId),
    CONSTRAINT FK_SupplierPayments_Methods   FOREIGN KEY (PaymentMethodId) REFERENCES db65922.dbo.PaymentMethods(PaymentMethodId)
);
GO

CREATE TABLE db65922.dbo.SupplierInvoiceAllocations (
    SupplierInvoiceAllocationId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierInvoiceAllocations PRIMARY KEY,
    SupplierPaymentId bigint NOT NULL,
    SupplierInvoiceId bigint NOT NULL,
    AllocatedAmount decimal(19,4) NOT NULL CONSTRAINT CK_SIA_Amount CHECK (AllocatedAmount > 0),
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_SIA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT UQ_SupplierInvoiceAllocations UNIQUE (SupplierPaymentId, SupplierInvoiceId),
    CONSTRAINT FK_SIA_Payments FOREIGN KEY (SupplierPaymentId) REFERENCES db65922.dbo.SupplierPayments(SupplierPaymentId),
    CONSTRAINT FK_SIA_Invoices FOREIGN KEY (SupplierInvoiceId) REFERENCES db65922.dbo.SupplierInvoices(SupplierInvoiceId)
);
GO

/* =====================================================================================
   SECTION 14 — TREASURY
   ===================================================================================== */

CREATE TABLE db65922.dbo.CashBoxes (
    CashBoxId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashBoxes PRIMARY KEY,
    BranchId        int NOT NULL,
    Code            nvarchar(30)  NOT NULL,
    NameAr          nvarchar(150) NOT NULL,
    NameEn          nvarchar(150) NULL,
    CurrencyCode    char(3) NOT NULL CONSTRAINT DF_CashBoxes_Currency DEFAULT 'EGP',
    OpeningBalance  decimal(19,4) NOT NULL CONSTRAINT DF_CashBoxes_Opening DEFAULT 0,
    CurrentBalance  decimal(19,4) NOT NULL CONSTRAINT DF_CashBoxes_Current DEFAULT 0,
    IsActive        bit NOT NULL CONSTRAINT DF_CashBoxes_Active DEFAULT 1,
    IsDeleted       bit NOT NULL CONSTRAINT DF_CashBoxes_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_CashBoxes_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT UQ_CashBoxes_BranchCode UNIQUE (BranchId, Code),
    CONSTRAINT FK_CashBoxes_Branches FOREIGN KEY (BranchId) REFERENCES db65922.dbo.Branches(BranchId)
);
GO

CREATE TABLE db65922.dbo.CashTransactions (
    CashTransactionId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashTransactions PRIMARY KEY,
    BranchId        int NOT NULL,                   -- B4: كان ناقص
    CashBoxId       int NOT NULL,
    TransactionDate datetime2(0) NOT NULL CONSTRAINT DF_CashTx_Date DEFAULT SYSUTCDATETIME(),
    TransactionType nvarchar(20)  NOT NULL,
    Amount          decimal(19,4) NOT NULL CONSTRAINT CK_CashTx_Amount CHECK (Amount > 0),
    PaymentMethodId int NULL,
    CustomerId      int NULL,
    SupplierId      int NULL,
    OperationId     bigint NULL,
    InvoiceId       bigint NULL,
    CustodyId       bigint NULL,
    -- B4: التحويل بين خزينتين — كان حركتين من غير رابط
    TransferGroupId uniqueidentifier NULL,
    CounterpartCashBoxId int NULL,
    -- B4: شيكات وبنوك
    ChequeNumber    nvarchar(50)  NULL,
    ChequeDate      date NULL,
    BankAccount     nvarchar(100) NULL,
    ReferenceNumber nvarchar(100) NULL,
    Description     nvarchar(500) NULL,
    Status          nvarchar(20) NOT NULL CONSTRAINT DF_CashTx_Status DEFAULT N'Posted',
    ReversedByTransactionId bigint NULL,            -- B4: العكس
    IsDeleted       bit NOT NULL CONSTRAINT DF_CashTx_IsDeleted DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_CashTx_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT CK_CashTx_Type   CHECK (TransactionType IN (N'Receipt',N'Payment',N'TransferIn',N'TransferOut')),
    CONSTRAINT CK_CashTx_Status CHECK (Status IN (N'Posted',N'Void')),
    CONSTRAINT FK_CashTx_Branches     FOREIGN KEY (BranchId)        REFERENCES db65922.dbo.Branches(BranchId),
    CONSTRAINT FK_CashTx_CashBoxes    FOREIGN KEY (CashBoxId)       REFERENCES db65922.dbo.CashBoxes(CashBoxId),
    CONSTRAINT FK_CashTx_Methods      FOREIGN KEY (PaymentMethodId) REFERENCES db65922.dbo.PaymentMethods(PaymentMethodId),
    CONSTRAINT FK_CashTx_Customers    FOREIGN KEY (CustomerId)      REFERENCES db65922.dbo.Customers(CustomerId),
    CONSTRAINT FK_CashTx_Suppliers    FOREIGN KEY (SupplierId)      REFERENCES db65922.dbo.Suppliers(SupplierId),
    CONSTRAINT FK_CashTx_Operations   FOREIGN KEY (OperationId)     REFERENCES db65922.dbo.Operations(OperationId),
    CONSTRAINT FK_CashTx_Invoices     FOREIGN KEY (InvoiceId)       REFERENCES db65922.dbo.Invoices(InvoiceId),
    CONSTRAINT FK_CashTx_Custodies    FOREIGN KEY (CustodyId)       REFERENCES db65922.dbo.DriverCustodies(CustodyId),
    CONSTRAINT FK_CashTx_Counterpart  FOREIGN KEY (CounterpartCashBoxId) REFERENCES db65922.dbo.CashBoxes(CashBoxId),
    CONSTRAINT FK_CashTx_Reversed     FOREIGN KEY (ReversedByTransactionId) REFERENCES db65922.dbo.CashTransactions(CashTransactionId)
);
GO

-- الـ FK اللي اتأجل
ALTER TABLE db65922.dbo.Payments
    ADD CONSTRAINT FK_Payments_CashTx FOREIGN KEY (CashTransactionId) REFERENCES db65922.dbo.CashTransactions(CashTransactionId);
ALTER TABLE db65922.dbo.SupplierPayments
    ADD CONSTRAINT FK_SupplierPayments_CashTx FOREIGN KEY (CashTransactionId) REFERENCES db65922.dbo.CashTransactions(CashTransactionId);
GO

/* =====================================================================================
   SECTION 15 — DOCUMENTS / NOTIFICATIONS / CUSTOMER PORTAL
   ===================================================================================== */

CREATE TABLE db65922.dbo.Documents (
    DocumentId      bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Documents PRIMARY KEY,
    BranchId        int NULL,
    DocumentTypeId  int NOT NULL,
    EntityType      nvarchar(50)  NOT NULL,
    EntityId        bigint NOT NULL,
    FileName        nvarchar(255) NOT NULL,
    OriginalFileName nvarchar(255) NULL,
    StorageProvider nvarchar(30)  NOT NULL CONSTRAINT DF_Documents_Storage DEFAULT N'Local',
    StoragePath     nvarchar(1000) NOT NULL,
    ContentType     nvarchar(150) NULL,
    FileSizeBytes   bigint NULL CONSTRAINT CK_Documents_Size CHECK (FileSizeBytes IS NULL OR FileSizeBytes >= 0),
    FileHash        nvarchar(128) NULL,
    ExpiryDate      date NULL,
    Description     nvarchar(500) NULL,
    UploadedAt      datetime2(0) NOT NULL CONSTRAINT DF_Documents_UploadedAt DEFAULT SYSUTCDATETIME(),
    UploadedBy      int NULL,
    IsDeleted       bit NOT NULL CONSTRAINT DF_Documents_IsDeleted DEFAULT 0,
    DeletedAt       datetime2(0) NULL,
    DeletedBy       int NULL,
    CONSTRAINT CK_Documents_EntityType CHECK (EntityType IN (
        N'Customer',N'Supplier',N'Employee',N'Driver',N'Vehicle',N'Trailer',
        N'Booking',N'Operation',N'Trip',N'Container',N'Invoice',N'SupplierInvoice',
        N'Expense',N'Custody',N'Payment',N'Maintenance',N'Other')),
    CONSTRAINT FK_Documents_Types    FOREIGN KEY (DocumentTypeId) REFERENCES db65922.dbo.DocumentTypes(DocumentTypeId),
    CONSTRAINT FK_Documents_Branches FOREIGN KEY (BranchId)       REFERENCES db65922.dbo.Branches(BranchId)
);
GO

CREATE TABLE db65922.dbo.Notifications (
    NotificationId  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
    UserId          int NULL,
    BranchId        int NULL,
    NotificationType nvarchar(50) NOT NULL,
    Title           nvarchar(200) NOT NULL,
    Message         nvarchar(1000) NOT NULL,
    EntityType      nvarchar(50)  NULL,
    EntityId        bigint NULL,
    Priority        nvarchar(20) NOT NULL CONSTRAINT DF_Notifications_Priority DEFAULT N'Normal',
    IsRead          bit NOT NULL CONSTRAINT DF_Notifications_IsRead DEFAULT 0,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_Notifications_CreatedAt DEFAULT SYSUTCDATETIME(),
    ReadAt          datetime2(0) NULL,
    CONSTRAINT CK_Notifications_Priority CHECK (Priority IN (N'Low',N'Normal',N'High',N'Critical')),
    CONSTRAINT FK_Notifications_Branches FOREIGN KEY (BranchId) REFERENCES db65922.dbo.Branches(BranchId)
);
GO

-- D12 + X3: Magic Link — TokenHash مش الـ Token نفسه
CREATE TABLE db65922.dbo.CustomerPortalTokens (
    PortalTokenId   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerPortalTokens PRIMARY KEY,
    CustomerId      int NOT NULL,
    TokenHash       nvarchar(128) NOT NULL,          -- SHA256 hex
    Purpose         nvarchar(20)  NOT NULL CONSTRAINT DF_PortalTokens_Purpose DEFAULT N'Portal',
    InvoiceId       bigint NULL,                     -- لو رابط لفاتورة واحدة
    ExpiresAt       datetime2(0) NULL,
    MaxUses         int NULL,
    UsedCount       int NOT NULL CONSTRAINT DF_PortalTokens_Used DEFAULT 0 CONSTRAINT CK_PortalTokens_Used CHECK (UsedCount >= 0),
    IsActive        bit NOT NULL CONSTRAINT DF_PortalTokens_Active DEFAULT 1,
    LastUsedAt      datetime2(0) NULL,
    RevokedAt       datetime2(0) NULL,
    RevokedBy       int NULL,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_PortalTokens_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       int NULL,
    CONSTRAINT UQ_PortalTokens_Hash UNIQUE (TokenHash),
    CONSTRAINT CK_PortalTokens_Purpose CHECK (Purpose IN (N'Portal',N'InvoiceView',N'Statement')),
    CONSTRAINT FK_PortalTokens_Customers FOREIGN KEY (CustomerId) REFERENCES db65922.dbo.Customers(CustomerId),
    CONSTRAINT FK_PortalTokens_Invoices  FOREIGN KEY (InvoiceId)  REFERENCES db65922.dbo.Invoices(InvoiceId)
);
GO

CREATE TABLE db65922.dbo.PortalAccessLogs (
    PortalAccessLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PortalAccessLogs PRIMARY KEY,
    CustomerId      int NOT NULL,
    PortalTokenId   bigint NULL,
    AccessedAt      datetime2(0) NOT NULL CONSTRAINT DF_PortalAccess_At DEFAULT SYSUTCDATETIME(),
    IpAddress       nvarchar(64)  NULL,
    UserAgent       nvarchar(500) NULL,
    EntityType      nvarchar(50)  NULL,
    EntityId        bigint NULL,
    Action          nvarchar(30)  NOT NULL CONSTRAINT DF_PortalAccess_Action DEFAULT N'View',
    CONSTRAINT CK_PortalAccess_Action CHECK (Action IN (N'View',N'Download',N'Print',N'Login',N'Denied')),
    CONSTRAINT FK_PortalAccess_Customers FOREIGN KEY (CustomerId)    REFERENCES db65922.dbo.Customers(CustomerId),
    CONSTRAINT FK_PortalAccess_Tokens    FOREIGN KEY (PortalTokenId) REFERENCES db65922.dbo.CustomerPortalTokens(PortalTokenId)
);
GO

/* =====================================================================================
   SECTION 16 — SECURITY (🔴 A5: ASP.NET Core Identity بـ int keys)
   =====================================================================================
   ⚠️  الجداول دي مطابقة للـ Schema اللي بيولّده EF Core 8 مع
       IdentityUser<int> / IdentityRole<int>
       لو استخدمت Migrations، EF هيعملها بنفس الشكل.
   ===================================================================================== */

CREATE TABLE db65922.dbo.AspNetRoles (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetRoles PRIMARY KEY,
    Name            nvarchar(256) NULL,
    NormalizedName  nvarchar(256) NULL,
    ConcurrencyStamp nvarchar(max) NULL,
    -- حقول إضافية للنظام
    RoleCode        nvarchar(50)  NOT NULL,
    NameAr          nvarchar(100) NOT NULL,
    NameEn          nvarchar(100) NULL,
    IsSystem        bit NOT NULL CONSTRAINT DF_AspNetRoles_IsSystem DEFAULT 0,
    IsActive        bit NOT NULL CONSTRAINT DF_AspNetRoles_IsActive DEFAULT 1
);
CREATE UNIQUE INDEX RoleNameIndex ON db65922.dbo.AspNetRoles (NormalizedName) WHERE NormalizedName IS NOT NULL;
CREATE UNIQUE INDEX UX_AspNetRoles_Code ON db65922.dbo.AspNetRoles (RoleCode);
GO

CREATE TABLE db65922.dbo.AspNetUsers (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetUsers PRIMARY KEY,
    UserName        nvarchar(256) NULL,
    NormalizedUserName nvarchar(256) NULL,
    Email           nvarchar(256) NULL,
    NormalizedEmail nvarchar(256) NULL,
    EmailConfirmed  bit NOT NULL CONSTRAINT DF_AspNetUsers_EmailConfirmed DEFAULT 0,
    PasswordHash    nvarchar(max) NULL,
    SecurityStamp   nvarchar(max) NULL,
    ConcurrencyStamp nvarchar(max) NULL,
    PhoneNumber     nvarchar(max) NULL,
    PhoneNumberConfirmed bit NOT NULL CONSTRAINT DF_AspNetUsers_PhoneConfirmed DEFAULT 0,
    TwoFactorEnabled bit NOT NULL CONSTRAINT DF_AspNetUsers_2FA DEFAULT 0,
    LockoutEnd      datetimeoffset(7) NULL,
    LockoutEnabled  bit NOT NULL CONSTRAINT DF_AspNetUsers_LockoutEnabled DEFAULT 1,
    AccessFailedCount int NOT NULL CONSTRAINT DF_AspNetUsers_FailedCount DEFAULT 0,
    -- حقول إضافية للنظام
    FullName        nvarchar(200) NOT NULL,
    EmployeeId      int NULL,
    BranchId        int NULL,
    ProfileImagePath nvarchar(500) NULL,          -- A5: كان ناقص
    UserStatus      nvarchar(20) NOT NULL CONSTRAINT DF_AspNetUsers_Status DEFAULT N'Active',
    LastLoginAt     datetime2(0) NULL,
    LastLoginIp     nvarchar(64)  NULL,
    IsActive        bit NOT NULL CONSTRAINT DF_AspNetUsers_IsActive DEFAULT 1,
    CreatedAt       datetime2(0) NOT NULL CONSTRAINT DF_AspNetUsers_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt       datetime2(0) NULL,
    UpdatedBy       int NULL,
    CONSTRAINT CK_AspNetUsers_Status CHECK (UserStatus IN (N'Active',N'Inactive',N'Locked')),
    CONSTRAINT FK_AspNetUsers_Employees FOREIGN KEY (EmployeeId) REFERENCES db65922.dbo.Employees(EmployeeId),
    CONSTRAINT FK_AspNetUsers_Branches  FOREIGN KEY (BranchId)   REFERENCES db65922.dbo.Branches(BranchId)
);
CREATE UNIQUE INDEX UserNameIndex ON db65922.dbo.AspNetUsers (NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
CREATE INDEX EmailIndex ON db65922.dbo.AspNetUsers (NormalizedEmail);
GO

CREATE TABLE db65922.dbo.AspNetUserRoles (
    UserId          int NOT NULL,
    RoleId          int NOT NULL,
    CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_AspNetUserRoles_Users FOREIGN KEY (UserId) REFERENCES db65922.dbo.AspNetUsers(Id) ON DELETE CASCADE,
    CONSTRAINT FK_AspNetUserRoles_Roles FOREIGN KEY (RoleId) REFERENCES db65922.dbo.AspNetRoles(Id) ON DELETE CASCADE
);
CREATE INDEX IX_AspNetUserRoles_RoleId ON db65922.dbo.AspNetUserRoles (RoleId);
GO

CREATE TABLE db65922.dbo.AspNetUserClaims (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetUserClaims PRIMARY KEY,
    UserId          int NOT NULL,
    ClaimType       nvarchar(max) NULL,
    ClaimValue      nvarchar(max) NULL,
    CONSTRAINT FK_AspNetUserClaims_Users FOREIGN KEY (UserId) REFERENCES db65922.dbo.AspNetUsers(Id) ON DELETE CASCADE
);
CREATE INDEX IX_AspNetUserClaims_UserId ON db65922.dbo.AspNetUserClaims (UserId);
GO

CREATE TABLE db65922.dbo.AspNetUserLogins (
    LoginProvider       nvarchar(128) NOT NULL,
    ProviderKey         nvarchar(128) NOT NULL,
    ProviderDisplayName nvarchar(max) NULL,
    UserId              int NOT NULL,
    CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
    CONSTRAINT FK_AspNetUserLogins_Users FOREIGN KEY (UserId) REFERENCES db65922.dbo.AspNetUsers(Id) ON DELETE CASCADE
);
CREATE INDEX IX_AspNetUserLogins_UserId ON db65922.dbo.AspNetUserLogins (UserId);
GO

CREATE TABLE db65922.dbo.AspNetUserTokens (
    UserId          int NOT NULL,
    LoginProvider   nvarchar(128) NOT NULL,
    Name            nvarchar(128) NOT NULL,
    Value           nvarchar(max) NULL,
    CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, Name),
    CONSTRAINT FK_AspNetUserTokens_Users FOREIGN KEY (UserId) REFERENCES db65922.dbo.AspNetUsers(Id) ON DELETE CASCADE
);
GO

CREATE TABLE db65922.dbo.AspNetRoleClaims (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY,
    RoleId          int NOT NULL,
    ClaimType       nvarchar(max) NULL,
    ClaimValue      nvarchar(max) NULL,
    CONSTRAINT FK_AspNetRoleClaims_Roles FOREIGN KEY (RoleId) REFERENCES db65922.dbo.AspNetRoles(Id) ON DELETE CASCADE
);
CREATE INDEX IX_AspNetRoleClaims_RoleId ON db65922.dbo.AspNetRoleClaims (RoleId);
GO

-- 🔴 A5: كل جداول الـ UserId بقت مرتبطة بـ FK حقيقي
ALTER TABLE db65922.dbo.Notifications
    ADD CONSTRAINT FK_Notifications_Users FOREIGN KEY (UserId) REFERENCES db65922.dbo.AspNetUsers(Id);
GO

/* =====================================================================================
   SECTION 17 — PERMISSIONS  (🔴 A6)
   ===================================================================================== */

CREATE TABLE db65922.dbo.AppPermissions (
    PermissionId    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppPermissions PRIMARY KEY,
    PermissionCode  nvarchar(100) NOT NULL,
    NameAr          nvarchar(150) NOT NULL,
    NameEn          nvarchar(150) NULL,
    ModuleCode      nvarchar(50)  NOT NULL,
    ActionCode      nvarchar(50)  NOT NULL,
    SortOrder       int NOT NULL CONSTRAINT DF_AppPermissions_Sort DEFAULT 100,
    CONSTRAINT UQ_AppPermissions_Code UNIQUE (PermissionCode)
);
GO

CREATE TABLE db65922.dbo.AppRolePermissions (
    RoleId          int NOT NULL,
    PermissionId    int NOT NULL,
    GrantedAt       datetime2(0) NOT NULL CONSTRAINT DF_ARP_GrantedAt DEFAULT SYSUTCDATETIME(),
    GrantedBy       int NULL,
    CONSTRAINT PK_AppRolePermissions PRIMARY KEY (RoleId, PermissionId),
    CONSTRAINT FK_ARP_Roles       FOREIGN KEY (RoleId)       REFERENCES db65922.dbo.AspNetRoles(Id),
    CONSTRAINT FK_ARP_Permissions FOREIGN KEY (PermissionId) REFERENCES db65922.dbo.AppPermissions(PermissionId)
);
GO

-- استثناءات: صلاحية مباشرة لمستخدم معين من غير الدور كله
CREATE TABLE db65922.dbo.AppUserPermissions (
    UserId          int NOT NULL,
    PermissionId    int NOT NULL,
    IsGranted       bit NOT NULL CONSTRAINT DF_AUP_Granted DEFAULT 1,   -- 0 = منع صريح
    GrantedAt       datetime2(0) NOT NULL CONSTRAINT DF_AUP_At DEFAULT SYSUTCDATETIME(),
    GrantedBy       int NULL,
    Notes           nvarchar(300) NULL,
    CONSTRAINT PK_AppUserPermissions PRIMARY KEY (UserId, PermissionId),
    CONSTRAINT FK_AUP_Users       FOREIGN KEY (UserId)       REFERENCES db65922.dbo.AspNetUsers(Id),
    CONSTRAINT FK_AUP_Permissions FOREIGN KEY (PermissionId) REFERENCES db65922.dbo.AppPermissions(PermissionId)
);
GO

/* =====================================================================================
   SECTION 18 — AUDIT / NUMBERING
   ===================================================================================== */

CREATE TABLE db65922.dbo.AuditLogs (
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
GO

ALTER TABLE db65922.dbo.AuditLogs
    ADD CONSTRAINT FK_AuditLogs_Users FOREIGN KEY (UserId) REFERENCES db65922.dbo.AspNetUsers(Id);
GO

-- 🔴 A7: BranchId NOT NULL DEFAULT 0 + Year (للترقيم BK-2026-000001)
-- 🔴 A7: في v1 كان BranchId nullable مع UNIQUE → SQL Server بيسمح بـ NULL واحدة بس،
--         فالسلسلة "العامة" التانية كانت بتفشل.
--    الحل: nullable + Filtered Unique Index (بيستثني NULL من الـ Uniqueness).
CREATE TABLE db65922.dbo.NumberSequences (
    NumberSequenceId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NumberSequences PRIMARY KEY,
    BranchId        int NULL,                   -- NULL = عام لكل الفروع
    DocumentType    nvarchar(30)  NOT NULL,
    Prefix          nvarchar(20)  NOT NULL,
    Year            smallint NOT NULL CONSTRAINT DF_NumberSequences_Year DEFAULT YEAR(SYSUTCDATETIME()),
    CurrentNumber   bigint NOT NULL CONSTRAINT DF_NumberSequences_Current DEFAULT 0,
    NumberLength    tinyint NOT NULL CONSTRAINT DF_NumberSequences_Length DEFAULT 6,
    ResetPeriod     nvarchar(20) NOT NULL CONSTRAINT DF_NumberSequences_Reset DEFAULT N'Yearly',
    UpdatedAt       datetime2(0) NULL,
    CONSTRAINT CK_NumberSequences_Reset CHECK (ResetPeriod IN (N'Yearly',N'Monthly',N'Never')),
    CONSTRAINT FK_NumberSequences_Branches FOREIGN KEY (BranchId) REFERENCES db65922.dbo.Branches(BranchId)
);
GO

CREATE UNIQUE INDEX UX_NumberSequences_Branch
    ON db65922.dbo.NumberSequences (BranchId, DocumentType, Year)
    WHERE BranchId IS NOT NULL;

CREATE UNIQUE INDEX UX_NumberSequences_Global
    ON db65922.dbo.NumberSequences (DocumentType, Year)
    WHERE BranchId IS NULL;
GO

-- 🔒 A7: الترقيم الآمن ضد الـ Concurrency
-- sp_getapplock على مفتاح (DocumentType|BranchId|Year) قبل الزيادة
CREATE OR ALTER PROCEDURE usp_GetNextNumber
    @DocumentType nvarchar(30),
    @BranchId     int = NULL,
    @NextNumber   nvarchar(40) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Year smallint = YEAR(SYSUTCDATETIME());
    DECLARE @LockKey nvarchar(100) = CONCAT(@DocumentType, N'|', ISNULL(CAST(@BranchId AS nvarchar(10)), N'GLOBAL'), N'|', @Year);
    DECLARE @Prefix nvarchar(20), @Len tinyint, @Num bigint;

    BEGIN TRANSACTION;

    IF APPLOCK_MODE(N'public', @LockKey, N'transaction') = N'NoLock'
    BEGIN
        DECLARE @r int = sp_getapplock @LockKey, N'Exclusive', N'Transaction', 10000;
        IF @r < 0
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50001, N'تعذّر الحصول على قفل الترقيم. حاول مرة أخرى.', 1;
        END
    END

    -- ⚠️ مهم: مانستخدمش `SET @Num = CurrentNumber = CurrentNumber + 1`
    --    لأن SQL Server مش بيضمن ترتيب التقييم — @Num ممكن ياخد القيمة القديمة.
    UPDATE db65922.dbo.NumberSequences
       SET CurrentNumber = CurrentNumber + 1,
           UpdatedAt = SYSUTCDATETIME()
     WHERE DocumentType = @DocumentType
       AND Year = @Year
       AND ((@BranchId IS NULL AND BranchId IS NULL) OR BranchId = @BranchId);

    IF @@ROWCOUNT = 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50002, N'سلسلة الترقيم غير معرّفة لهذا النوع.', 1;
    END

    -- قراءة بعد التحديث (الـ app lock بيمنع أي عملية تانية تدخل في النص)
    SELECT @Num = CurrentNumber, @Prefix = Prefix, @Len = NumberLength
      FROM db65922.dbo.NumberSequences
     WHERE DocumentType = @DocumentType
       AND Year = @Year
       AND ((@BranchId IS NULL AND BranchId IS NULL) OR BranchId = @BranchId);

    SET @NextNumber = CONCAT(@Prefix, @Year, N'-', RIGHT(REPLICATE(N'0', @Len) + CAST(@Num AS nvarchar(20)), @Len));

    COMMIT TRANSACTION;
END
GO

/* =====================================================================================
   SECTION 19 — INDEXES
   ===================================================================================== */

-- Bookings
CREATE INDEX IX_Bookings_Customer_Status ON db65922.dbo.Bookings (CustomerId, Status, BookingDate) WHERE IsDeleted = 0;
CREATE INDEX IX_Bookings_Port_Date       ON db65922.dbo.Bookings (PortId, RequestedDate) WHERE IsDeleted = 0;
CREATE INDEX IX_Bookings_Branch_Status   ON db65922.dbo.Bookings (BranchId, Status) WHERE IsDeleted = 0;

-- Containers
CREATE INDEX IX_Containers_Status ON db65922.dbo.Containers (CurrentStatus) WHERE IsDeleted = 0;
CREATE INDEX IX_ContainerMovements_Container ON db65922.dbo.ContainerMovements (ContainerId, MovementDate DESC);

-- Booking lines
CREATE INDEX IX_BookingContainerLines_Booking ON db65922.dbo.BookingContainerLines (BookingId);
CREATE INDEX IX_BookingContainerDetails_Line  ON db65922.dbo.BookingContainerDetails (BookingContainerLineId);
CREATE INDEX IX_BookingContainerDetails_Container ON db65922.dbo.BookingContainerDetails (ContainerId);

-- Operations
CREATE INDEX IX_Operations_Customer_Status ON db65922.dbo.Operations (CustomerId, Status, PlannedDate) WHERE IsDeleted = 0;
CREATE INDEX IX_Operations_Booking  ON db65922.dbo.Operations (BookingId);
CREATE INDEX IX_Operations_Branch_Status ON db65922.dbo.Operations (BranchId, Status) WHERE IsDeleted = 0;
CREATE INDEX IX_OperationContainers_Container ON db65922.dbo.OperationContainers (ContainerId, OperationId);
-- بديل الـ UNIQUE Constraint (بيستثني NULL)
CREATE UNIQUE INDEX UX_OperationContainers_OpContainer
    ON db65922.dbo.OperationContainers (OperationId, ContainerId)
    WHERE ContainerId IS NOT NULL;
CREATE INDEX IX_OperationRevenueItems_Operation ON db65922.dbo.OperationRevenueItems (OperationId);

-- 🔴 Trips — أهم الفهارس (التجميع + تعارض الموارد)
CREATE INDEX IX_Trips_Driver_Date  ON db65922.dbo.Trips (DriverId, PlannedStartAt)  WHERE IsDeleted = 0;
CREATE INDEX IX_Trips_Vehicle_Date ON db65922.dbo.Trips (VehicleId, PlannedStartAt) WHERE IsDeleted = 0;
CREATE INDEX IX_Trips_Trailer_Date ON db65922.dbo.Trips (TrailerId, PlannedStartAt) WHERE IsDeleted = 0;
CREATE INDEX IX_Trips_Branch_Status ON db65922.dbo.Trips (BranchId, Status) WHERE IsDeleted = 0;
CREATE INDEX IX_TripOperations_Operation ON db65922.dbo.TripOperations (OperationId);

-- Custody
CREATE INDEX IX_Custodies_Owner_Status ON db65922.dbo.DriverCustodies (OwnerType, OwnerId, Status) WHERE IsDeleted = 0;
CREATE INDEX IX_Custodies_Trip ON db65922.dbo.DriverCustodies (TripId);
CREATE INDEX IX_CustodyTransactions_Custody ON db65922.dbo.CustodyTransactions (CustodyId, TransactionType);

-- Expenses
CREATE INDEX IX_Expenses_Operation ON db65922.dbo.Expenses (OperationId, ExpenseDate) INCLUDE (Amount, Status, ExpenseTypeId) WHERE IsDeleted = 0;
CREATE INDEX IX_Expenses_Trip      ON db65922.dbo.Expenses (TripId) INCLUDE (Amount, Status) WHERE IsDeleted = 0;
CREATE INDEX IX_Expenses_Custody   ON db65922.dbo.Expenses (CustodyId) WHERE IsDeleted = 0;
CREATE INDEX IX_Expenses_Driver    ON db65922.dbo.Expenses (DriverId, ExpenseDate) WHERE IsDeleted = 0;
CREATE INDEX IX_Expenses_Unpaid    ON db65922.dbo.Expenses (PaymentStatus, ExpenseDate) WHERE IsDeleted = 0;

-- Invoices
CREATE INDEX IX_Invoices_Customer_Date ON db65922.dbo.Invoices (CustomerId, InvoiceDate, Status) WHERE IsDeleted = 0;
CREATE INDEX IX_Invoices_Branch_Status ON db65922.dbo.Invoices (BranchId, Status, PaymentStatus) WHERE IsDeleted = 0;
CREATE INDEX IX_InvoiceItems_Operation ON db65922.dbo.InvoiceItems (OperationId);
CREATE INDEX IX_InvoiceOperations_Operation ON db65922.dbo.InvoiceOperations (OperationId);
CREATE INDEX IX_InvoiceSendLogs_Invoice ON db65922.dbo.InvoiceSendLogs (InvoiceId, SentAt DESC);

-- Payments
CREATE INDEX IX_Payments_Customer ON db65922.dbo.Payments (CustomerId, PaymentDate) WHERE IsDeleted = 0;
CREATE INDEX IX_PaymentAllocations_Invoice ON db65922.dbo.PaymentAllocations (InvoiceId);

-- Supplier payables
CREATE INDEX IX_SupplierInvoices_Supplier ON db65922.dbo.SupplierInvoices (SupplierId, InvoiceDate, Status) WHERE IsDeleted = 0;
CREATE INDEX IX_SupplierInvoiceItems_Trip ON db65922.dbo.SupplierInvoiceItems (TripId);
CREATE INDEX IX_SupplierPayments_Supplier ON db65922.dbo.SupplierPayments (SupplierId, PaymentDate) WHERE IsDeleted = 0;

-- Treasury
CREATE INDEX IX_CashTransactions_Box_Date ON db65922.dbo.CashTransactions (CashBoxId, TransactionDate) WHERE IsDeleted = 0;
CREATE INDEX IX_CashTransactions_Customer ON db65922.dbo.CashTransactions (CustomerId, TransactionType, TransactionDate) WHERE IsDeleted = 0;

-- Documents / Notifications
CREATE INDEX IX_Documents_Entity ON db65922.dbo.Documents (EntityType, EntityId) WHERE IsDeleted = 0;
CREATE INDEX IX_Documents_Expiry ON db65922.dbo.Documents (ExpiryDate) WHERE IsDeleted = 0 AND ExpiryDate IS NOT NULL;
CREATE INDEX IX_Notifications_User ON db65922.dbo.Notifications (UserId, IsRead, CreatedAt DESC);

-- Pricing
CREATE INDEX IX_CustomerPriceRules_Lookup
    ON db65922.dbo.CustomerPriceRules (CustomerId, ServiceId, PortId, ContainerTypeId, ValidFrom, ValidTo)
    WHERE IsDeleted = 0 AND IsActive = 1;

-- Audit
CREATE INDEX IX_AuditLogs_Entity ON db65922.dbo.AuditLogs (EntityType, EntityId, CreatedAt DESC);
CREATE INDEX IX_AuditLogs_User   ON db65922.dbo.AuditLogs (UserId, CreatedAt DESC);

-- Portal
CREATE INDEX IX_PortalAccessLogs_Customer ON db65922.dbo.PortalAccessLogs (CustomerId, AccessedAt DESC);
GO

/* =====================================================================================
   SECTION 20 — TRIGGERS  (مزامنة المجاميع — مصدر واحد للحقيقة)
   =====================================================================================
   ⚠️  SQL Server مابيسمحش بـ Subquery في Computed Column،
       فالمجاميع بتتحسب هنا.
   ===================================================================================== */

-- 🔴 A8: عهدة — AmountSpent / AmountReturned / AdditionalDue من CustodyTransactions
CREATE OR ALTER TRIGGER trg_CustodyTransactions_Sync ON db65922.dbo.CustodyTransactions
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH AffectedCustodies AS (
        SELECT CustodyId FROM inserted
        UNION
        SELECT CustodyId FROM deleted
    )
    UPDATE c SET
        AmountSpent    = ISNULL(t.Spent, 0),
        AmountReturned = ISNULL(t.Returned, 0),
        AdditionalDue  = CASE WHEN ISNULL(t.Spent,0) > c.AmountIssued
                              THEN ISNULL(t.Spent,0) - c.AmountIssued
                              ELSE 0 END,
        Status = CASE
            WHEN c.Status IN (N'Closed', N'Cancelled') THEN c.Status
            WHEN ISNULL(t.Spent,0) + ISNULL(t.Returned,0) = 0 THEN N'Open'
            ELSE N'PartiallySettled' END,
        UpdatedAt = SYSUTCDATETIME()
    FROM db65922.dbo.DriverCustodies c
    INNER JOIN AffectedCustodies a ON a.CustodyId = c.CustodyId
    OUTER APPLY (
        SELECT
            SUM(CASE WHEN TransactionType = N'Expense' THEN Amount ELSE 0 END) AS Spent,
            SUM(CASE WHEN TransactionType = N'Refund'  THEN Amount ELSE 0 END) AS Returned
        FROM db65922.dbo.CustodyTransactions
        WHERE CustodyId = c.CustodyId
    ) t;
END
GO

-- 🔴 A9: RevenueNet / RevenueTax من OperationRevenueItems
CREATE OR ALTER TRIGGER trg_OperationRevenueItems_Sync ON db65922.dbo.OperationRevenueItems
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH AffectedOps AS (
        SELECT OperationId FROM inserted
        UNION
        SELECT OperationId FROM deleted
    )
    UPDATE o SET
        RevenueNet = ISNULL(r.Net, 0),
        RevenueTax = ISNULL(r.Tax, 0),
        UpdatedAt  = SYSUTCDATETIME()
    FROM db65922.dbo.Operations o
    INNER JOIN AffectedOps a ON a.OperationId = o.OperationId
    OUTER APPLY (
        SELECT SUM(LineNet) AS Net, SUM(LineTax) AS Tax
        FROM db65922.dbo.OperationRevenueItems
        WHERE OperationId = o.OperationId
    ) r;
END
GO

-- 🔴 A10: ActualCost = مصروفات مباشرة (مربوطة بالعملية) + نصيبها من تكلفة الرحلة
CREATE OR ALTER TRIGGER trg_Operations_CostSync ON db65922.dbo.TripCostAllocations
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH AffectedOps AS (
        SELECT OperationId FROM inserted
        UNION
        SELECT OperationId FROM deleted
    )
    UPDATE o SET
        ActualCost = ISNULL(e.DirectCost, 0) + ISNULL(a.AllocCost, 0),
        UpdatedAt  = SYSUTCDATETIME()
    FROM db65922.dbo.Operations o
    INNER JOIN AffectedOps af ON af.OperationId = o.OperationId
    OUTER APPLY (
        SELECT SUM(Amount) AS DirectCost
        FROM db65922.dbo.Expenses
        WHERE OperationId = o.OperationId AND Status <> N'Cancelled' AND IsDeleted = 0
    ) e
    OUTER APPLY (
        SELECT SUM(AllocatedAmount) AS AllocCost
        FROM db65922.dbo.TripCostAllocations
        WHERE OperationId = o.OperationId
    ) a;
END
GO

CREATE OR ALTER TRIGGER trg_Expenses_OpCostSync ON db65922.dbo.Expenses
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH AffectedOps AS (
        SELECT OperationId FROM inserted WHERE OperationId IS NOT NULL
        UNION
        SELECT OperationId FROM deleted  WHERE OperationId IS NOT NULL
    )
    UPDATE o SET
        ActualCost = ISNULL(e.DirectCost, 0) + ISNULL(a.AllocCost, 0),
        UpdatedAt  = SYSUTCDATETIME()
    FROM db65922.dbo.Operations o
    INNER JOIN AffectedOps af ON af.OperationId = o.OperationId
    OUTER APPLY (
        SELECT SUM(Amount) AS DirectCost
        FROM db65922.dbo.Expenses
        WHERE OperationId = o.OperationId AND Status <> N'Cancelled' AND IsDeleted = 0
    ) e
    OUTER APPLY (
        SELECT SUM(AllocatedAmount) AS AllocCost
        FROM db65922.dbo.TripCostAllocations
        WHERE OperationId = o.OperationId
    ) a;
END
GO

-- 🔴 A1: PaidAmount على الفاتورة من PaymentAllocations
CREATE OR ALTER TRIGGER trg_PaymentAllocations_InvoiceSync ON db65922.dbo.PaymentAllocations
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH AffectedInvoices AS (
        SELECT InvoiceId FROM inserted
        UNION
        SELECT InvoiceId FROM deleted
    )
    UPDATE i SET
        PaidAmount = ISNULL(p.Paid, 0),
        PaymentStatus = CASE
            WHEN i.Status = N'Cancelled' THEN i.PaymentStatus
            WHEN ISNULL(p.Paid,0) = 0 THEN N'Unpaid'
            WHEN i.GrandTotal > 0 AND ISNULL(p.Paid,0) > i.GrandTotal THEN N'Overpaid'
            WHEN i.GrandTotal > 0 AND ISNULL(p.Paid,0) >= i.GrandTotal THEN N'Paid'
            ELSE N'PartiallyPaid' END,
        UpdatedAt = SYSUTCDATETIME()
    FROM db65922.dbo.Invoices i
    INNER JOIN AffectedInvoices a ON a.InvoiceId = i.InvoiceId
    OUTER APPLY (
        SELECT SUM(pa.AllocatedAmount) AS Paid
        FROM db65922.dbo.PaymentAllocations pa
        INNER JOIN db65922.dbo.Payments py ON py.PaymentId = pa.PaymentId
        WHERE pa.InvoiceId = i.InvoiceId AND py.Status = N'Posted' AND py.IsDeleted = 0
    ) p;
END
GO

-- A11: PaidAmount على فاتورة المورد
CREATE OR ALTER TRIGGER trg_SupplierInvoiceAllocations_Sync ON db65922.dbo.SupplierInvoiceAllocations
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH AffectedInvoices AS (
        SELECT SupplierInvoiceId FROM inserted
        UNION
        SELECT SupplierInvoiceId FROM deleted
    )
    UPDATE si SET
        PaidAmount = ISNULL(p.Paid, 0),
        PaymentStatus = CASE
            WHEN ISNULL(p.Paid,0) = 0 THEN N'Unpaid'
            WHEN ISNULL(p.Paid,0) >= si.GrandTotal THEN N'Paid'
            ELSE N'PartiallyPaid' END,
        UpdatedAt = SYSUTCDATETIME()
    FROM db65922.dbo.SupplierInvoices si
    INNER JOIN AffectedInvoices a ON a.SupplierInvoiceId = si.SupplierInvoiceId
    OUTER APPLY (
        SELECT SUM(sia.AllocatedAmount) AS Paid
        FROM db65922.dbo.SupplierInvoiceAllocations sia
        INNER JOIN db65922.dbo.SupplierPayments sp ON sp.SupplierPaymentId = sia.SupplierPaymentId
        WHERE sia.SupplierInvoiceId = si.SupplierInvoiceId AND sp.Status = N'Posted' AND sp.IsDeleted = 0
    ) p;
END
GO

-- Treasury: رصيد الخزينة
CREATE OR ALTER TRIGGER trg_CashTransactions_BalanceSync ON db65922.dbo.CashTransactions
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH AffectedBoxes AS (
        SELECT CashBoxId FROM inserted
        UNION
        SELECT CashBoxId FROM deleted
    )
    UPDATE cb SET
        CurrentBalance = cb.OpeningBalance + ISNULL(t.Net, 0)
    FROM db65922.dbo.CashBoxes cb
    INNER JOIN AffectedBoxes a ON a.CashBoxId = cb.CashBoxId
    OUTER APPLY (
        SELECT SUM(CASE
                WHEN ct.TransactionType IN (N'Receipt', N'TransferIn')  THEN  ct.Amount
                WHEN ct.TransactionType IN (N'Payment', N'TransferOut') THEN -ct.Amount
                ELSE 0 END) AS Net
        FROM db65922.dbo.CashTransactions ct
        WHERE ct.CashBoxId = cb.CashBoxId AND ct.Status = N'Posted' AND ct.IsDeleted = 0
    ) t;
END
GO

-- A3: مزامنة AssignedQty في BookingContainerLines
CREATE OR ALTER TRIGGER trg_BookingContainerDetails_QtySync ON db65922.dbo.BookingContainerDetails
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
    FROM db65922.dbo.BookingContainerLines l
    INNER JOIN AffectedLines a ON a.BookingContainerLineId = l.BookingContainerLineId
    OUTER APPLY (
        SELECT COUNT(*) AS Cnt
        FROM db65922.dbo.BookingContainerDetails
        WHERE BookingContainerLineId = l.BookingContainerLineId AND Status <> N'Cancelled'
    ) d;
END
GO

/* =====================================================================================
   SECTION 21 — SEED MASTER DATA
   =====================================================================================
   ⚠️  كل الـ INSERTs بـ IF NOT EXISTS → السكريبت idempotent، يتشغّل أكتر من مرة بأمان.
   ===================================================================================== */

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.Branches)
    INSERT db65922.dbo.Branches (BranchCode, NameAr, NameEn)
    VALUES ('MAIN', N'الفرع الرئيسي', N'Main Branch');
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.Departments)
    INSERT db65922.dbo.Departments (Code, NameAr, NameEn) VALUES
    ('OPS',   N'التشغيل',              N'Operations'),
    ('FIN',   N'المالية',              N'Finance'),
    ('HR',    N'الموارد البشرية',      N'Human Resources'),
    ('FLEET', N'النقل والأسطول',       N'Fleet'),
    ('ADMIN', N'الإدارة',              N'Administration');
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.JobTitles)
    INSERT db65922.dbo.JobTitles (Code, NameAr, NameEn) VALUES
    ('GM',      N'مدير عام',            N'General Manager'),
    ('OPSMGR',  N'مدير تشغيل',          N'Operations Manager'),
    ('OPSEMP',  N'موظف تشغيل',          N'Operations Employee'),
    ('ACCT',    N'محاسب',               N'Accountant'),
    ('FINMGR',  N'مدير مالي',           N'Finance Manager'),
    ('HRMGR',   N'مدير موارد بشرية',    N'HR Manager'),
    ('FLT MGR', N'مدير أسطول',          N'Fleet Manager'),
    ('DRIVER',  N'سائق',                N'Driver'),
    ('DATAENT', N'مدخل بيانات',         N'Data Entry');
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.PaymentTerms)
    INSERT db65922.dbo.PaymentTerms (Code, NameAr, NameEn, DueDays) VALUES
    ('CASH', N'نقدي',   N'Cash',    0),
    ('15D',  N'15 يوم', N'15 Days', 15),
    ('30D',  N'30 يوم', N'30 Days', 30),
    ('60D',  N'60 يوم', N'60 Days', 60),
    ('90D',  N'90 يوم', N'90 Days', 90);
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.PaymentMethods)
    INSERT db65922.dbo.PaymentMethods (Code, NameAr, NameEn, RequiresChequeNo, IsCashBased, SortOrder) VALUES
    ('CASH',   N'نقدي',           N'Cash',          0, 1, 10),
    ('CHEQUE', N'شيك',            N'Cheque',        1, 0, 20),
    ('BANK',   N'تحويل بنكي',     N'Bank Transfer', 0, 0, 30),
    ('CARD',   N'بطاقة ائتمانية', N'Credit Card',   0, 0, 40),
    ('WALLET', N'محفظة إلكترونية',N'E-Wallet',      0, 0, 50);
GO

-- 🔴 A4 + D3: الضريبة
-- ⚠️  تحذير: لازم محاسبك يؤكد النسب قبل الإنتاج.
--    نقل البضائع في مصر غالبًا ضريبة جدول 5% (غير قابلة للخصم)،
--    وخدمات الترانزيت عبر الموانئ معفاة (تعديل 2026).
IF NOT EXISTS (SELECT 1 FROM db65922.dbo.TaxRates)
    INSERT db65922.dbo.TaxRates (Code, NameAr, NameEn, Rate, TaxKind, IsDeductible, RequiresExemptionReason, ValidFrom) VALUES
    ('STD14',  N'ضريبة القيمة المضافة 14%',      N'VAT 14%',           14.0000, N'Standard', 1, 0, '2016-09-08'),
    ('SCH5',   N'ضريبة جدول 5% - نقل بضائع',     N'Schedule 5% Freight', 5.0000, N'Schedule', 0, 0, '2016-09-08'),
    ('ZERO',   N'ضريبة صفرية (تصدير)',           N'Zero Rated',          0.0000, N'Zero',     0, 0, '2016-09-08'),
    ('EXEMPT', N'معفاة',                         N'Exempt',              0.0000, N'Exempt',   0, 1, '2016-09-08');
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.Ports)
    INSERT db65922.dbo.Ports (PortCode, NameAr, NameEn, CityAr, CityEn) VALUES
    ('ALX', N'ميناء الإسكندرية',      N'Alexandria Port',   N'الإسكندرية', N'Alexandria'),
    ('DEK', N'ميناء الدخيلة',         N'Dekheila Port',     N'الإسكندرية', N'Alexandria'),
    ('DAM', N'ميناء دمياط',           N'Damietta Port',     N'دمياط',      N'Damietta'),
    ('PSD', N'ميناء شرق بورسعيد',     N'East Port Said',    N'بورسعيد',    N'Port Said'),
    ('PSE', N'ميناء غرب بورسعيد',     N'West Port Said',    N'بورسعيد',    N'Port Said'),
    ('SOC', N'ميناء السخنة',          N'Sokhna Port',       N'السويس',     N'Suez'),
    ('SUE', N'ميناء السويس',          N'Suez Port',         N'السويس',     N'Suez'),
    ('ADB', N'ميناء الأدبية',         N'Adabiya Port',      N'السويس',     N'Suez'),
    ('SAF', N'ميناء سفاجا',           N'Safaga Port',       N'البحر الأحمر', N'Red Sea');
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.Destinations)
    INSERT db65922.dbo.Destinations (Code, NameAr, NameEn, CityAr, CityEn) VALUES
    ('CAI',   N'القاهرة',              N'Cairo',              N'القاهرة',       N'Cairo'),
    ('10RAM', N'العاشر من رمضان',      N'10th of Ramadan',    N'الشرقية',       N'Al Sharqia'),
    ('6OCT',  N'السادس من أكتوبر',     N'6th of October',     N'الجيزة',        N'Giza'),
    ('OBAID', N'العبور',               N'Obour',              N'القليوبية',     N'Qalyubia'),
    ('SADAT', N'السادات',              N'Sadat City',         N'المنوفية',      N'Monufia'),
    ('AMRIA', N'العامرية',             N'Amreya',             N'الإسكندرية',    N'Alexandria'),
    ('BRG',   N'برج العرب',            N'Borg El Arab',       N'الإسكندرية',    N'Alexandria'),
    ('GIZA',  N'الجيزة',               N'Giza',               N'الجيزة',        N'Giza'),
    ('TONS',  N'طنطا',                 N'Tanta',              N'الغربية',       N'Gharbia'),
    ('MAN',   N'المنصورة',             N'Mansoura',           N'الدقهلية',      N'Dakahlia'),
    ('SUZ',   N'السويس',               N'Suez',               N'السويس',        N'Suez'),
    ('ISM',   N'الإسماعيلية',          N'Ismailia',           N'الإسماعيلية',   N'Ismailia'),
    ('PORTS', N'بورسعيد',              N'Port Said',          N'بورسعيد',       N'Port Said'),
    ('DAM',   N'دمياط',                N'Damietta',           N'دمياط',         N'Damietta'),
    ('ASY',   N'أسيوط',                N'Assiut',             N'أسيوط',         N'Assiut'),
    ('ASWAN', N'أسوان',                N'Aswan',              N'أسوان',         N'Aswan');
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.TripTypes)
BEGIN
    DECLARE @trStd int = (SELECT TaxRateId FROM db65922.dbo.TaxRates WHERE Code = 'STD14');
    DECLARE @trSch int = (SELECT TaxRateId FROM db65922.dbo.TaxRates WHERE Code = 'SCH5');
    DECLARE @trExm int = (SELECT TaxRateId FROM db65922.dbo.TaxRates WHERE Code = 'EXEMPT');
    INSERT db65922.dbo.TripTypes (Code, NameAr, NameEn, DefaultTaxRateId) VALUES
    ('IMPORT',  N'وارد من الميناء',  N'Import from Port', @trSch),
    ('EXPORT',  N'صادر إلى الميناء', N'Export to Port',   @trSch),
    ('TRANSIT', N'ترانزيت',          N'Transit',          @trExm),
    ('LOCAL',   N'نقل داخلي',        N'Local Transport',  @trSch),
    ('PORT2P',  N'ميناء إلى ميناء',  N'Port to Port',     @trSch);
END
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.Services)
BEGIN
    DECLARE @svcTax int = (SELECT TaxRateId FROM db65922.dbo.TaxRates WHERE Code = 'SCH5');
    -- ⚠️ الأسعار الافتراضية = 0 → لازم تتعبّى من الشاشة قبل التشغيل
    INSERT db65922.dbo.Services (ServiceCode, NameAr, NameEn, Unit, DefaultSellingPrice, DefaultCost, TaxRateId, IsTaxable) VALUES
    ('TRANS',    N'نقل حاوية',        N'Container Transportation', N'Trip',  0, 0, @svcTax, 1),
    ('TRANS20',  N'نقل حاوية 20 قدم', N'20ft Container Transport', N'Trip',  0, 0, @svcTax, 1),
    ('TRANS40',  N'نقل حاوية 40 قدم', N'40ft Container Transport', N'Trip',  0, 0, @svcTax, 1),
    ('REEFER',   N'نقل حاوية مبردة',  N'Reefer Transport',         N'Trip',  0, 0, @svcTax, 1),
    ('LOAD',     N'تحميل',            N'Loading',                  N'Job',   0, 0, @svcTax, 1),
    ('UNLOAD',   N'تفريغ',            N'Unloading',                N'Job',   0, 0, @svcTax, 1),
    ('WAIT',     N'انتظار',           N'Waiting',                  N'Hour',  0, 0, @svcTax, 1),
    ('ADDTRIP',  N'رحلة إضافية',      N'Additional Trip',          N'Trip',  0, 0, @svcTax, 1),
    ('OTHER',    N'خدمة أخرى',        N'Other Service',            N'Unit',  0, 0, @svcTax, 1);
END
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.ContainerTypes)
    INSERT db65922.dbo.ContainerTypes (Code, NameAr, NameEn, SizeFeet, IsReefer) VALUES
    ('20GP', N'حاوية 20 قدم عادية',      N'20 GP',       20, 0),
    ('40GP', N'حاوية 40 قدم عادية',      N'40 GP',       40, 0),
    ('40HC', N'حاوية 40 قدم High Cube',  N'40 HC',       40, 0),
    ('20RF', N'حاوية 20 قدم مبردة',      N'20 Reefer',   20, 1),
    ('40RF', N'حاوية 40 قدم مبردة',      N'40 Reefer',   40, 1),
    ('20OT', N'حاوية 20 قدم Open Top',   N'20 Open Top', 20, 0),
    ('40OT', N'حاوية 40 قدم Open Top',   N'40 Open Top', 40, 0),
    ('20FR', N'حاوية 20 قدم Flat Rack',  N'20 Flat Rack',20, 0),
    ('40FR', N'حاوية 40 قدم Flat Rack',  N'40 Flat Rack',40, 0);
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.ExpenseTypes)
    INSERT db65922.dbo.ExpenseTypes (Code, NameAr, NameEn, IsCustodyAllowed, IsOperationCost, IsTaxDeductible) VALUES
    ('FUEL',   N'وقود',                    N'Fuel',            1, 1, 1),
    ('TOLL',   N'رسوم طريق',               N'Toll',            1, 1, 1),
    ('PORT',   N'رسوم ميناء',              N'Port Fees',       1, 1, 1),
    ('PARK',   N'انتظار / أرضية ميناء',    N'Parking/Waiting', 1, 1, 1),
    ('LOAD',   N'تحميل',                   N'Loading',         1, 1, 1),
    ('UNLOAD', N'تفريغ',                   N'Unloading',       1, 1, 1),
    ('LIFT',   N'ونش / رافعة',             N'Crane/Lift',      1, 1, 1),
    ('MEAL',   N'بدل إعاشة سائق',          N'Driver Allowance',1, 1, 0),
    ('REPAIR', N'إصلاح أثناء الرحلة',      N'Road Repair',     1, 1, 1),
    ('FINE',   N'غرامة / مخالفة',          N'Fine',            1, 1, 0),
    ('MAINT',  N'صيانة دورية',             N'Maintenance',     0, 0, 1),
    ('INSUR',  N'تأمين',                   N'Insurance',       0, 0, 1),
    ('ADMIN',  N'مصروفات إدارية',          N'Administrative',  0, 0, 1),
    ('OTHER',  N'مصروفات أخرى',            N'Other',           1, 1, 0);
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.DocumentTypes)
    INSERT db65922.dbo.DocumentTypes (Code, NameAr, NameEn, HasExpiry) VALUES
    ('ID',          N'بطاقة شخصية',          N'National ID',        1),
    ('LICENSE',     N'رخصة قيادة',           N'Driving License',    1),
    ('VEH_LICENSE', N'رخصة سيارة',           N'Vehicle License',    1),
    ('TRAILER_LIC', N'رخصة مقطورة',          N'Trailer License',    1),
    ('INSURANCE',   N'وثيقة تأمين',          N'Insurance Policy',   1),
    ('MAINTENANCE', N'صيانة',                N'Maintenance',        0),
    ('POD',         N'إثبات تسليم',          N'Proof of Delivery',  0),
    ('RECEIPT',     N'إيصال مصروف',          N'Expense Receipt',    0),
    ('INVOICE',     N'فاتورة',               N'Invoice',            0),
    ('ETA_RECEIPT', N'إيصال مصلحة الضرائب',  N'ETA Receipt',        0),
    ('CONTRACT',    N'عقد',                  N'Contract',           1),
    ('OTHER',       N'مستند آخر',            N'Other',              0);
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.OperationStatuses)
    INSERT db65922.dbo.OperationStatuses (Code, NameAr, NameEn, SortOrder, IsTerminal, ColorHex) VALUES
    ('Pending',         N'قيد الانتظار',        N'Pending',          10, 0, '#6B7280'),
    ('Assigned',        N'تم التعيين',          N'Assigned',         20, 0, '#3B82F6'),
    ('DriverReceived',  N'استلم السائق',        N'Driver Received',  30, 0, '#8B5CF6'),
    ('InTransit',       N'في الطريق',           N'In Transit',       40, 0, '#F59E0B'),
    ('Delivered',       N'تم التسليم',          N'Delivered',        50, 0, '#10B981'),
    ('ExpensesPending', N'بانتظار المصروفات',   N'Expenses Pending', 60, 0, '#F97316'),
    ('CustodyPending',  N'بانتظار تسوية العهدة',N'Custody Pending',  70, 0, '#EF4444'),
    ('ReadyToClose',    N'جاهزة للإغلاق',       N'Ready To Close',   80, 0, '#06B6D4'),
    ('Closed',          N'مغلقة',               N'Closed',           90, 1, '#374151'),
    ('Cancelled',       N'ملغاة',               N'Cancelled',       100, 1, '#DC2626');
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.CompanyProfile)
    INSERT db65922.dbo.CompanyProfile
        (CompanyProfileId, LegalNameAr, LegalNameEn, TaxNumber, AddressAr, DefaultTaxRateId)
    VALUES
        (1,
         N'شركة فاست كوم لنقل الحاويات',
         N'FastCom Container Transport',
         N'000000000',                              -- ⚠️ لازم يتعبّى بالرقم الضريبي الحقيقي
         N'الإسكندرية - برج العرب',                 -- ⚠️ لازم عنوان مفصّل (محافظة/مدينة/شارع/رقم)
         (SELECT TaxRateId FROM db65922.dbo.TaxRates WHERE Code = 'SCH5'));
GO

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.CashBoxes)
BEGIN
    DECLARE @cbBranch int = (SELECT TOP 1 BranchId FROM db65922.dbo.Branches ORDER BY BranchId);
    INSERT db65922.dbo.CashBoxes (BranchId, Code, NameAr, NameEn) VALUES
    (@cbBranch, 'MAIN', N'الخزينة الرئيسية',  N'Main Cash'),
    (@cbBranch, 'OPS',  N'خزينة التشغيل',     N'Operations Cash');
END
GO

-- 🔴 A7: سلاسل الترقيم (BranchId = NULL → عام)
IF NOT EXISTS (SELECT 1 FROM db65922.dbo.NumberSequences)
BEGIN
    DECLARE @yr smallint = YEAR(SYSUTCDATETIME());
    INSERT db65922.dbo.NumberSequences (BranchId, DocumentType, Prefix, Year, CurrentNumber, NumberLength, ResetPeriod) VALUES
    (NULL, 'BOOKING',          'BK-',  @yr, 0, 6, 'Yearly'),
    (NULL, 'OPERATION',        'OP-',  @yr, 0, 6, 'Yearly'),
    (NULL, 'TRIP',             'TR-',  @yr, 0, 6, 'Yearly'),
    (NULL, 'CUSTODY',          'CUS-', @yr, 0, 6, 'Yearly'),   -- X7: CUS للعهد
    (NULL, 'CUSTOMER',         'CST-', @yr, 0, 6, 'Never'),    -- X7: CST للعميل
    (NULL, 'SUPPLIER',         'SUP-', @yr, 0, 6, 'Never'),
    (NULL, 'DRIVER',           'DRV-', @yr, 0, 6, 'Never'),
    (NULL, 'EMPLOYEE',         'EMP-', @yr, 0, 6, 'Never'),
    (NULL, 'VEHICLE',          'VEH-', @yr, 0, 6, 'Never'),
    (NULL, 'TRAILER',          'TRL-', @yr, 0, 6, 'Never'),
    (NULL, 'EXPENSE',          'EXP-', @yr, 0, 6, 'Yearly'),
    (NULL, 'INVOICE',          'INV-', @yr, 0, 6, 'Yearly'),
    (NULL, 'CREDITNOTE',       'CN-',  @yr, 0, 6, 'Yearly'),
    (NULL, 'PAYMENT',          'PAY-', @yr, 0, 6, 'Yearly'),
    (NULL, 'SUPPLIER_INVOICE', 'SINV-',@yr, 0, 6, 'Yearly'),
    (NULL, 'SUPPLIER_PAYMENT', 'SPAY-',@yr, 0, 6, 'Yearly'),
    (NULL, 'MAINTENANCE',      'MNT-', @yr, 0, 6, 'Yearly'),
    (NULL, 'CASH_RECEIPT',     'RCV-', @yr, 0, 6, 'Yearly'),
    (NULL, 'CASH_PAYMENT',     'PMT-', @yr, 0, 6, 'Yearly');
END
GO

/* =====================================================================================
   SECTION 22 — PERMISSIONS SEED   (🔴 A6: من 29 لـ 82 صلاحية)
   =====================================================================================
   ⚠️  في v1 كان AppRolePermissions فاضي تمامًا → مافيش دور له أي صلاحية.
       هنا بنزرع الصلاحيات + توزيعها على الأدوار.
   ===================================================================================== */

IF NOT EXISTS (SELECT 1 FROM db65922.dbo.AppPermissions)
BEGIN
    SET IDENTITY_INSERT db65922.dbo.AppPermissions ON;
    INSERT db65922.dbo.AppPermissions (PermissionId, PermissionCode, NameAr, NameEn, ModuleCode, ActionCode, SortOrder) VALUES
    -- DASHBOARD
    ( 1, 'DASHBOARD.VIEW',              N'عرض لوحة التحكم',        N'View Dashboard',           'DASHBOARD',       'VIEW',     100),
    -- CUSTOMER
    ( 2, 'CUSTOMER.VIEW',               N'عرض العملاء',            N'View Customers',           'CUSTOMER',        'VIEW',     200),
    ( 3, 'CUSTOMER.CREATE',             N'إضافة عميل',             N'Create Customer',          'CUSTOMER',        'CREATE',   201),
    ( 4, 'CUSTOMER.EDIT',               N'تعديل عميل',             N'Edit Customer',            'CUSTOMER',        'EDIT',     202),
    ( 5, 'CUSTOMER.DELETE',             N'حذف عميل',               N'Delete Customer',          'CUSTOMER',        'DELETE',   203),
    ( 6, 'CUSTOMER.STATEMENT',          N'كشف حساب العميل',        N'Customer Statement',       'CUSTOMER',        'STATEMENT',204),
    ( 7, 'CUSTOMER.PORTAL_TOKEN',       N'إنشاء رابط بورتال',      N'Create Portal Link',       'CUSTOMER',        'PORTAL',   205),
    -- SUPPLIER
    ( 8, 'SUPPLIER.VIEW',               N'عرض الموردين',           N'View Suppliers',           'SUPPLIER',        'VIEW',     300),
    ( 9, 'SUPPLIER.CREATE',             N'إضافة مورد',             N'Create Supplier',          'SUPPLIER',        'CREATE',   301),
    (10, 'SUPPLIER.EDIT',               N'تعديل مورد',             N'Edit Supplier',            'SUPPLIER',        'EDIT',     302),
    (11, 'SUPPLIER.DELETE',             N'حذف مورد',               N'Delete Supplier',          'SUPPLIER',        'DELETE',   303),
    -- DRIVER
    (12, 'DRIVER.VIEW',                 N'عرض السائقين',           N'View Drivers',             'DRIVER',          'VIEW',     400),
    (13, 'DRIVER.CREATE',               N'إضافة سائق',             N'Create Driver',            'DRIVER',          'CREATE',   401),
    (14, 'DRIVER.EDIT',                 N'تعديل سائق',             N'Edit Driver',              'DRIVER',          'EDIT',     402),
    (15, 'DRIVER.DELETE',               N'حذف سائق',               N'Delete Driver',            'DRIVER',          'DELETE',   403),
    -- EMPLOYEE
    (16, 'EMPLOYEE.VIEW',               N'عرض الموظفين',           N'View Employees',           'EMPLOYEE',        'VIEW',     500),
    (17, 'EMPLOYEE.CREATE',             N'إضافة موظف',             N'Create Employee',          'EMPLOYEE',        'CREATE',   501),
    (18, 'EMPLOYEE.EDIT',               N'تعديل موظف',             N'Edit Employee',            'EMPLOYEE',        'EDIT',     502),
    (19, 'EMPLOYEE.DELETE',             N'حذف موظف',               N'Delete Employee',          'EMPLOYEE',        'DELETE',   503),
    -- FLEET
    (20, 'FLEET.VIEW',                  N'عرض الأسطول',            N'View Fleet',               'FLEET',           'VIEW',     600),
    (21, 'FLEET.CREATE',                N'إضافة سيارة/مقطورة',     N'Create Vehicle/Trailer',   'FLEET',           'CREATE',   601),
    (22, 'FLEET.EDIT',                  N'تعديل سيارة/مقطورة',     N'Edit Vehicle/Trailer',     'FLEET',           'EDIT',     602),
    (23, 'FLEET.DELETE',                N'حذف سيارة/مقطورة',       N'Delete Vehicle/Trailer',   'FLEET',           'DELETE',   603),
    (24, 'MAINTENANCE.VIEW',            N'عرض الصيانة',            N'View Maintenance',         'FLEET',           'MAINTVIEW',604),
    (25, 'MAINTENANCE.CREATE',          N'تسجيل صيانة',            N'Create Maintenance',       'FLEET',           'MAINTCREATE',605),
    (26, 'MAINTENANCE.EDIT',            N'تعديل صيانة',            N'Edit Maintenance',         'FLEET',           'MAINTEDIT',606),
    (27, 'MAINTENANCE.DELETE',          N'حذف صيانة',              N'Delete Maintenance',       'FLEET',           'MAINTDELETE',607),
    -- PRICING  (⚠️ كان مافيش صلاحيات تسعير خالص في v1)
    (28, 'PRICING.VIEW',                N'عرض الأسعار',            N'View Pricing',             'PRICING',         'VIEW',     700),
    (29, 'PRICING.CREATE',              N'إضافة سعر',              N'Create Price Rule',        'PRICING',         'CREATE',   701),
    (30, 'PRICING.EDIT',                N'تعديل سعر',              N'Edit Price Rule',          'PRICING',         'EDIT',     702),
    (31, 'PRICING.DELETE',              N'حذف سعر',                N'Delete Price Rule',        'PRICING',         'DELETE',   703),
    (32, 'PRICING.APPROVE',             N'اعتماد السعر',           N'Approve Pricing',          'PRICING',         'APPROVE',  704),
    -- BOOKING
    (33, 'BOOKING.VIEW',                N'عرض الحجوزات',           N'View Bookings',            'BOOKING',         'VIEW',     800),
    (34, 'BOOKING.CREATE',              N'إنشاء حجز',              N'Create Booking',           'BOOKING',         'CREATE',   801),
    (35, 'BOOKING.EDIT',                N'تعديل حجز',              N'Edit Booking',             'BOOKING',         'EDIT',     802),
    (36, 'BOOKING.DELETE',              N'حذف حجز',                N'Delete Booking',           'BOOKING',         'DELETE',   803),
    (37, 'BOOKING.CONFIRM',             N'اعتماد حجز',             N'Confirm Booking',          'BOOKING',         'CONFIRM',  804),
    (38, 'BOOKING.CANCEL',              N'إلغاء حجز',              N'Cancel Booking',           'BOOKING',         'CANCEL',   805),
    (39, 'BOOKING.PRINT',               N'طباعة حجز',              N'Print Booking',            'BOOKING',         'PRINT',    806),
    -- OPERATION
    (40, 'OPERATION.VIEW',              N'عرض العمليات',           N'View Operations',          'OPERATION',       'VIEW',     900),
    (41, 'OPERATION.CREATE',            N'إنشاء عملية',            N'Create Operation',         'OPERATION',       'CREATE',   901),
    (42, 'OPERATION.EDIT',              N'تعديل عملية',            N'Edit Operation',           'OPERATION',       'EDIT',     902),
    (43, 'OPERATION.DELETE',            N'حذف عملية',              N'Delete Operation',         'OPERATION',       'DELETE',   903),
    (44, 'OPERATION.CLOSE',             N'إغلاق عملية',            N'Close Operation',          'OPERATION',       'CLOSE',    904),
    (45, 'OPERATION.REOPEN',            N'إعادة فتح عملية',        N'Reopen Operation',         'OPERATION',       'REOPEN',   905),
    (46, 'OPERATION.CANCEL',            N'إلغاء عملية',            N'Cancel Operation',         'OPERATION',       'CANCEL',   906),
    (47, 'OPERATION.PRINT',             N'طباعة عملية',            N'Print Operation',          'OPERATION',       'PRINT',    907),
    -- TRIP
    (48, 'TRIP.VIEW',                   N'عرض الرحلات',            N'View Trips',               'TRIP',            'VIEW',    1000),
    (49, 'TRIP.CREATE',                 N'إنشاء رحلة',             N'Create Trip',              'TRIP',            'CREATE',  1001),
    (50, 'TRIP.EDIT',                   N'تعديل رحلة',             N'Edit Trip',                'TRIP',            'EDIT',    1002),
    (51, 'TRIP.ASSIGN',                 N'تعيين سائق/سيارة',       N'Assign Driver/Vehicle',    'TRIP',            'ASSIGN',  1003),
    (52, 'TRIP.CANCEL',                 N'إلغاء رحلة',             N'Cancel Trip',              'TRIP',            'CANCEL',  1004),
    (53, 'TRIP.ALLOCATE_COST',          N'توزيع تكلفة الرحلة',     N'Allocate Trip Cost',       'TRIP',            'ALLOCATE',1005),
    -- CUSTODY
    (54, 'CUSTODY.VIEW',                N'عرض العهد',              N'View Custodies',           'CUSTODY',         'VIEW',    1100),
    (55, 'CUSTODY.CREATE',              N'صرف عهدة',               N'Create Custody',           'CUSTODY',         'CREATE',  1101),
    (56, 'CUSTODY.SETTLE',              N'تسوية عهدة',             N'Settle Custody',           'CUSTODY',         'SETTLE',  1102),
    (57, 'CUSTODY.APPROVE',             N'اعتماد تسوية العهدة',    N'Approve Custody',          'CUSTODY',         'APPROVE', 1103),
    (58, 'CUSTODY.CLOSE',               N'إغلاق عهدة',             N'Close Custody',            'CUSTODY',         'CLOSE',   1104),
    (59, 'CUSTODY.DELETE',              N'حذف عهدة',               N'Delete Custody',           'CUSTODY',         'DELETE',  1105),
    -- EXPENSE
    (60, 'EXPENSE.VIEW',                N'عرض المصروفات',          N'View Expenses',            'EXPENSE',         'VIEW',    1200),
    (61, 'EXPENSE.CREATE',              N'تسجيل مصروف',            N'Create Expense',           'EXPENSE',         'CREATE',  1201),
    (62, 'EXPENSE.EDIT',                N'تعديل مصروف',            N'Edit Expense',             'EXPENSE',         'EDIT',    1202),
    (63, 'EXPENSE.DELETE',              N'حذف مصروف',              N'Delete Expense',           'EXPENSE',         'DELETE',  1203),
    (64, 'EXPENSE.APPROVE',             N'اعتماد مصروف',           N'Approve Expense',          'EXPENSE',         'APPROVE', 1204),
    -- INVOICE
    (65, 'INVOICE.VIEW',                N'عرض الفواتير',           N'View Invoices',            'INVOICE',         'VIEW',    1300),
    (66, 'INVOICE.CREATE',              N'إنشاء فاتورة',           N'Create Invoice',           'INVOICE',         'CREATE',  1301),
    (67, 'INVOICE.EDIT',                N'تعديل فاتورة',           N'Edit Invoice',             'INVOICE',         'EDIT',    1302),
    (68, 'INVOICE.APPROVE',             N'اعتماد فاتورة',          N'Approve Invoice',          'INVOICE',         'APPROVE', 1303),
    (69, 'INVOICE.ISSUE',               N'إصدار فاتورة',           N'Issue Invoice',            'INVOICE',         'ISSUE',   1304),
    (70, 'INVOICE.SEND',                N'إرسال فاتورة',           N'Send Invoice',             'INVOICE',         'SEND',    1305),
    (71, 'INVOICE.CANCEL',              N'إلغاء فاتورة',           N'Cancel Invoice',           'INVOICE',         'CANCEL',  1306),
    (72, 'INVOICE.CREDITNOTE',          N'إصدار إشعار دائن',       N'Issue Credit Note',        'INVOICE',         'CREDIT',  1307),
    (73, 'INVOICE.PRINT',               N'طباعة فاتورة',           N'Print Invoice',            'INVOICE',         'PRINT',   1308),
    (74, 'INVOICE.DELETE',              N'حذف فاتورة',             N'Delete Invoice',           'INVOICE',         'DELETE',  1309),
    -- PAYMENT
    (75, 'PAYMENT.VIEW',                N'عرض المدفوعات',          N'View Payments',            'PAYMENT',         'VIEW',    1400),
    (76, 'PAYMENT.CREATE',              N'تسجيل دفعة',             N'Create Payment',           'PAYMENT',         'CREATE',  1401),
    (77, 'PAYMENT.EDIT',                N'تعديل دفعة',             N'Edit Payment',             'PAYMENT',         'EDIT',    1402),
    (78, 'PAYMENT.VOID',                N'إلغاء دفعة',             N'Void Payment',             'PAYMENT',         'VOID',    1403),
    -- TREASURY
    (79, 'TREASURY.VIEW',               N'عرض الخزينة',            N'View Treasury',            'TREASURY',        'VIEW',    1500),
    (80, 'TREASURY.POST',               N'تسجيل حركة خزينة',       N'Post Treasury Transaction',N'TREASURY',        'POST',    1501),
    (81, 'TREASURY.VOID',               N'إلغاء حركة خزينة',       N'Void Treasury Transaction',N'TREASURY',        'VOID',    1502),
    (82, 'TREASURY.TRANSFER',           N'تحويل بين الخزائن',      N'Transfer Between Cashboxes',N'TREASURY',       'TRANSFER',1503),
    -- SUPPLIER INVOICE / PAYMENT  (🆕 D4)
    (83, 'SUPPLIERINVOICE.VIEW',        N'عرض فواتير الموردين',    N'View Supplier Invoices',   'SUPPLIERINVOICE', 'VIEW',    1600),
    (84, 'SUPPLIERINVOICE.CREATE',      N'إدخال فاتورة مورد',      N'Create Supplier Invoice',  'SUPPLIERINVOICE', 'CREATE',  1601),
    (85, 'SUPPLIERINVOICE.EDIT',        N'تعديل فاتورة مورد',      N'Edit Supplier Invoice',    'SUPPLIERINVOICE', 'EDIT',    1602),
    (86, 'SUPPLIERINVOICE.APPROVE',     N'اعتماد فاتورة مورد',     N'Approve Supplier Invoice', 'SUPPLIERINVOICE', 'APPROVE', 1603),
    (87, 'SUPPLIERINVOICE.DELETE',      N'حذف فاتورة مورد',        N'Delete Supplier Invoice',  'SUPPLIERINVOICE', 'DELETE',  1604),
    (88, 'SUPPLIERPAYMENT.VIEW',        N'عرض مدفوعات الموردين',   N'View Supplier Payments',   'SUPPLIERPAYMENT', 'VIEW',    1700),
    (89, 'SUPPLIERPAYMENT.CREATE',      N'تسجيل دفعة مورد',        N'Create Supplier Payment',  'SUPPLIERPAYMENT', 'CREATE',  1701),
    (90, 'SUPPLIERPAYMENT.VOID',        N'إلغاء دفعة مورد',        N'Void Supplier Payment',    'SUPPLIERPAYMENT', 'VOID',    1702),
    -- DOCUMENT
    (91, 'DOCUMENT.UPLOAD',             N'رفع مستندات',            N'Upload Documents',         'DOCUMENT',        'UPLOAD',  1800),
    (92, 'DOCUMENT.DOWNLOAD',           N'تحميل مستندات',          N'Download Documents',       'DOCUMENT',        'DOWNLOAD',1801),
    (93, 'DOCUMENT.DELETE',             N'حذف مستندات',            N'Delete Documents',         'DOCUMENT',        'DELETE',  1802),
    -- MASTER DATA
    (94, 'MASTERDATA.VIEW',             N'عرض البيانات الأساسية',  N'View Master Data',         'MASTERDATA',      'VIEW',    1900),
    (95, 'MASTERDATA.MANAGE',           N'إدارة البيانات الأساسية',N'Manage Master Data',       'MASTERDATA',      'MANAGE',  1901),
    -- REPORT
    (96, 'REPORT.OPERATIONS',           N'تقارير التشغيل',         N'Operations Reports',       'REPORT',          'OPS',     2000),
    (97, 'REPORT.FINANCIAL',            N'التقارير المالية',       N'Financial Reports',        'REPORT',          'FIN',     2001),
    (98, 'REPORT.FLEET',                N'تقارير الأسطول',         N'Fleet Reports',            'REPORT',          'FLEET',   2002),
    (99, 'REPORT.EXPORT',               N'تصدير التقارير',         N'Export Reports',           'REPORT',          'EXPORT',  2003),
    -- USER / ROLE
    (100,'USER.VIEW',                   N'عرض المستخدمين',         N'View Users',               'USER',            'VIEW',    2100),
    (101,'USER.MANAGE',                 N'إدارة المستخدمين',       N'Manage Users',             'USER',            'MANAGE',  2101),
    (102,'ROLE.MANAGE',                 N'إدارة الأدوار والصلاحيات',N'Manage Roles & Permissions',N'ROLE',           'MANAGE',  2102),
    -- SETTINGS
    (103,'SETTINGS.VIEW',               N'عرض الإعدادات',          N'View Settings',            'SETTINGS',        'VIEW',    2200),
    (104,'SETTINGS.MANAGE',             N'إدارة الإعدادات',        N'Manage Settings',          'SETTINGS',        'MANAGE',  2201),
    -- PORTAL  (🆕 D12)
    (105,'PORTAL.MANAGE',               N'إدارة بورتال العملاء',   N'Manage Customer Portal',   'PORTAL',          'MANAGE',  2300),
    -- AUDIT
    (106,'AUDIT.VIEW',                  N'عرض سجل التدقيق',        N'View Audit Log',           'AUDIT',           'VIEW',    2400),
    -- NOTIFICATION
    (107,'NOTIFICATION.MANAGE',         N'إدارة الإشعارات',        N'Manage Notifications',     'NOTIFICATION',    'MANAGE',  2500);
    SET IDENTITY_INSERT db65922.dbo.AppPermissions OFF;
END
GO

-- الأدوار (11 دور زي الـ Prompt قسم 5 — v1 كان فيه 7 بس)
IF NOT EXISTS (SELECT 1 FROM db65922.dbo.AspNetRoles)
    INSERT db65922.dbo.AspNetRoles (Name, NormalizedName, RoleCode, NameAr, NameEn, IsSystem) VALUES
    ('SystemAdministrator', 'SYSTEMADMINISTRATOR', 'ADMIN',    N'مدير النظام',        N'System Administrator', 1),
    ('GeneralManager',      'GENERALMANAGER',      'GM',       N'المدير العام',       N'General Manager',      0),
    ('OperationsManager',   'OPERATIONSMANAGER',   'OPSMGR',   N'مدير التشغيل',       N'Operations Manager',   0),
    ('OperationsEmployee',  'OPERATIONSEMPLOYEE',  'OPSEMP',   N'موظف تشغيل',         N'Operations Employee',  0),
    ('FinanceManager',      'FINANCEMANAGER',      'FINMGR',   N'المدير المالي',      N'Finance Manager',      0),
    ('Accountant',          'ACCOUNTANT',          'ACCT',     N'محاسب',              N'Accountant',           0),
    ('HRManager',           'HRMANAGER',           'HRMGR',    N'مدير الموارد البشرية',N'HR Manager',          0),
    ('FleetManager',        'FLEETMANAGER',        'FLTMGR',   N'مدير الأسطول',       N'Fleet Manager',        0),
    ('DataEntry',           'DATAENTRY',           'DATAENT',  N'مدخل بيانات',        N'Data Entry',           0),
    ('Viewer',              'VIEWER',              'VIEWER',   N'مشاهد',              N'Viewer',               0),
    ('CustomerPortal',      'CUSTOMERPORTAL',      'PORTAL',   N'عميل (بورتال)',      N'Customer (Portal)',    1);
GO

-- 🔴 توزيع الصلاحيات على الأدوار
IF NOT EXISTS (SELECT 1 FROM db65922.dbo.AppRolePermissions)
BEGIN
    -- ADMIN: كل الصلاحيات
    INSERT db65922.dbo.AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, p.PermissionId FROM db65922.dbo.AspNetRoles r CROSS JOIN db65922.dbo.AppPermissions p
    WHERE r.RoleCode = 'ADMIN';

    -- GM: كل التشغيل + المالية + التقارير (بدون إدارة المستخدمين والأدوار والإعدادات)
    INSERT db65922.dbo.AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, p.PermissionId FROM db65922.dbo.AspNetRoles r CROSS JOIN db65922.dbo.AppPermissions p
    WHERE r.RoleCode = 'GM'
      AND p.ModuleCode NOT IN ('USER','ROLE','SETTINGS','AUDIT');

    -- OPSMGR: تشغيل كامل + أسطول + عهد + مصروفات (بدون فواتير/مدفوعات/خزينة)
    INSERT db65922.dbo.AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, p.PermissionId FROM db65922.dbo.AspNetRoles r CROSS JOIN db65922.dbo.AppPermissions p
    WHERE r.RoleCode = 'OPSMGR'
      AND p.ModuleCode IN ('DASHBOARD','CUSTOMER','DRIVER','FLEET','BOOKING','OPERATION','TRIP','CUSTODY','EXPENSE','DOCUMENT','MASTERDATA','REPORT','NOTIFICATION')
      AND p.PermissionCode NOT IN ('CUSTOMER.DELETE','DRIVER.DELETE','FLEET.DELETE','MAINTENANCE.DELETE','CUSTODY.DELETE','EXPENSE.DELETE','DOCUMENT.DELETE');

    -- OPSEMP: ⚠️ الـ Prompt قسم 5 بالحرف:
    --   "الموظف يستطيع إنشاء Booking ولكن لا يستطيع:
    --    تغيير السعر بعد الاعتماد، إغلاق العملية، اعتماد المصروفات، إصدار فاتورة، حذف عملية"
    INSERT db65922.dbo.AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, p.PermissionId FROM db65922.dbo.AspNetRoles r CROSS JOIN db65922.dbo.AppPermissions p
    WHERE r.RoleCode = 'OPSEMP'
      AND p.PermissionCode IN (
        'DASHBOARD.VIEW',
        'CUSTOMER.VIEW',
        'DRIVER.VIEW','FLEET.VIEW',
        'BOOKING.VIEW','BOOKING.CREATE','BOOKING.EDIT','BOOKING.PRINT',
        'OPERATION.VIEW','OPERATION.CREATE','OPERATION.EDIT','OPERATION.PRINT',
        'TRIP.VIEW','TRIP.CREATE','TRIP.EDIT','TRIP.ASSIGN',
        'CUSTODY.VIEW','CUSTODY.CREATE',
        'EXPENSE.VIEW','EXPENSE.CREATE',
        'DOCUMENT.UPLOAD','DOCUMENT.DOWNLOAD',
        'MASTERDATA.VIEW');

    -- FINMGR: المالية كاملة + الفواتير + المدفوعات + الخزينة
    INSERT db65922.dbo.AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, p.PermissionId FROM db65922.dbo.AspNetRoles r CROSS JOIN db65922.dbo.AppPermissions p
    WHERE r.RoleCode = 'FINMGR'
      AND p.ModuleCode IN ('DASHBOARD','CUSTOMER','SUPPLIER','INVOICE','PAYMENT','TREASURY','SUPPLIERINVOICE','SUPPLIERPAYMENT','EXPENSE','CUSTODY','DOCUMENT','REPORT','NOTIFICATION','MASTERDATA')
      AND p.PermissionCode NOT IN ('CUSTOMER.DELETE','SUPPLIER.DELETE','EXPENSE.DELETE');

    -- ACCT: محاسب — عرض + إدخال، بدون اعتماد أو إلغاء
    INSERT db65922.dbo.AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, p.PermissionId FROM db65922.dbo.AspNetRoles r CROSS JOIN db65922.dbo.AppPermissions p
    WHERE r.RoleCode = 'ACCT'
      AND p.PermissionCode IN (
        'DASHBOARD.VIEW',
        'CUSTOMER.VIEW','CUSTOMER.CREATE','CUSTOMER.EDIT','CUSTOMER.STATEMENT',
        'SUPPLIER.VIEW','SUPPLIER.CREATE','SUPPLIER.EDIT',
        'INVOICE.VIEW','INVOICE.CREATE','INVOICE.EDIT','INVOICE.PRINT',
        'PAYMENT.VIEW','PAYMENT.CREATE','PAYMENT.EDIT',
        'TREASURY.VIEW','TREASURY.POST',
        'SUPPLIERINVOICE.VIEW','SUPPLIERINVOICE.CREATE','SUPPLIERINVOICE.EDIT',
        'SUPPLIERPAYMENT.VIEW','SUPPLIERPAYMENT.CREATE',
        'EXPENSE.VIEW','EXPENSE.CREATE','EXPENSE.EDIT',
        'CUSTODY.VIEW','CUSTODY.SETTLE',
        'DOCUMENT.UPLOAD','DOCUMENT.DOWNLOAD',
        'REPORT.OPERATIONS','REPORT.FINANCIAL','REPORT.EXPORT',
        'MASTERDATA.VIEW');

    -- HRMGR
    INSERT db65922.dbo.AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, p.PermissionId FROM db65922.dbo.AspNetRoles r CROSS JOIN db65922.dbo.AppPermissions p
    WHERE r.RoleCode = 'HRMGR'
      AND p.PermissionCode IN (
        'DASHBOARD.VIEW',
        'EMPLOYEE.VIEW','EMPLOYEE.CREATE','EMPLOYEE.EDIT','EMPLOYEE.DELETE',
        'DRIVER.VIEW','DRIVER.CREATE','DRIVER.EDIT',
        'DOCUMENT.UPLOAD','DOCUMENT.DOWNLOAD','DOCUMENT.DELETE',
        'MASTERDATA.VIEW');

    -- FLTMGR
    INSERT db65922.dbo.AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, p.PermissionId FROM db65922.dbo.AspNetRoles r CROSS JOIN db65922.dbo.AppPermissions p
    WHERE r.RoleCode = 'FLTMGR'
      AND p.ModuleCode IN ('DASHBOARD','DRIVER','FLEET','DOCUMENT','MASTERDATA','REPORT','NOTIFICATION')
      AND p.PermissionCode NOT IN ('DRIVER.DELETE','FLEET.DELETE','MAINTENANCE.DELETE','DOCUMENT.DELETE');

    -- DATAENT: إدخال بس
    INSERT db65922.dbo.AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, p.PermissionId FROM db65922.dbo.AspNetRoles r CROSS JOIN db65922.dbo.AppPermissions p
    WHERE r.RoleCode = 'DATAENT'
      AND p.PermissionCode IN (
        'DASHBOARD.VIEW',
        'CUSTOMER.VIEW','CUSTOMER.CREATE','CUSTOMER.EDIT',
        'SUPPLIER.VIEW','SUPPLIER.CREATE','SUPPLIER.EDIT',
        'DRIVER.VIEW','DRIVER.CREATE','DRIVER.EDIT',
        'FLEET.VIEW','FLEET.CREATE','FLEET.EDIT',
        'BOOKING.VIEW','BOOKING.CREATE','BOOKING.EDIT',
        'OPERATION.VIEW','OPERATION.CREATE','OPERATION.EDIT',
        'TRIP.VIEW','TRIP.CREATE','TRIP.EDIT',
        'CUSTODY.VIEW','CUSTODY.CREATE',
        'EXPENSE.VIEW','EXPENSE.CREATE',
        'DOCUMENT.UPLOAD','DOCUMENT.DOWNLOAD',
        'MASTERDATA.VIEW','MASTERDATA.MANAGE');

    -- VIEWER: عرض بس
    INSERT db65922.dbo.AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, p.PermissionId FROM db65922.dbo.AspNetRoles r CROSS JOIN db65922.dbo.AppPermissions p
    WHERE r.RoleCode = 'VIEWER'
      AND p.ModuleCode NOT IN ('AUDIT','USER','ROLE','SETTINGS')
      AND p.PermissionCode NOT LIKE '%.MANAGE';

    -- PORTAL: الدور ده لعملاء البورتال — الوصول بيتحكم فيه بالـ Token (CustomerPortalTokens)
    --         مش بالصلاحيات. فمافيش أي صلاحية من النظام الداخلي.
END
GO

/* =====================================================================================
   SECTION 23 — VIEWS FOR REPORTING
   ===================================================================================== */

-- 🔴 A10: الربحية — مُصلّحة
-- v1 كان بيجمع كل المصروفات من غير تمييز، وبيتجاهل مصروفات الرحلة المجمّعة.
CREATE OR ALTER VIEW vw_OperationProfitability
AS
SELECT
    o.OperationId,
    o.OperationNumber,
    o.BookingId,
    b.BookingNumber,
    o.BranchId,
    o.CustomerId,
    c.NameAr                        AS CustomerName,
    o.Status,
    o.PlannedDate,
    o.ActualDeliveryAt,
    o.RevenueNet,
    o.RevenueTax,
    o.RevenueNet + o.RevenueTax     AS RevenueGross,
    -- التكلفة المباشرة (مصروفات مربوطة بالعملية نفسها)
    ISNULL(de.DirectCost, 0)        AS DirectCost,
    -- نصيب العملية من تكلفة الرحلات المجمّعة
    ISNULL(tc.AllocatedCost, 0)     AS AllocatedTripCost,
    o.ActualCost,
    o.EstimatedCost,
    -- المساهمة (قبل المصاريف الإدارية وتحميل العمالة والإهلاك)
    o.RevenueNet - o.ActualCost     AS GrossProfit,
    CAST(CASE WHEN o.RevenueNet > 0
              THEN ((o.RevenueNet - o.ActualCost) / o.RevenueNet) * 100
              ELSE 0 END AS decimal(9,2)) AS ProfitMarginPercent,
    -- الفواتير
    ISNULL(iv.InvoicedAmount, 0)    AS InvoicedAmount,
    ISNULL(iv.PaidAmount, 0)        AS CollectedAmount,
    CASE WHEN iv.InvoiceCount > 0 THEN 1 ELSE 0 END AS IsInvoiced
FROM db65922.dbo.Operations o
INNER JOIN db65922.dbo.Bookings  b ON b.BookingId  = o.BookingId
INNER JOIN db65922.dbo.Customers c ON c.CustomerId = o.CustomerId
OUTER APPLY (
    SELECT SUM(e.Amount) AS DirectCost
    FROM db65922.dbo.Expenses e
    WHERE e.OperationId = o.OperationId AND e.Status <> N'Cancelled' AND e.IsDeleted = 0
) de
OUTER APPLY (
    SELECT SUM(tca.AllocatedAmount) AS AllocatedCost
    FROM db65922.dbo.TripCostAllocations tca
    WHERE tca.OperationId = o.OperationId
) tc
OUTER APPLY (
    SELECT COUNT(DISTINCT i.InvoiceId) AS InvoiceCount,
           SUM(ii.LineTotal)           AS InvoicedAmount,
           SUM(i.PaidAmount)           AS PaidAmount
    FROM db65922.dbo.InvoiceItems ii
    INNER JOIN db65922.dbo.Invoices i ON i.InvoiceId = ii.InvoiceId
    WHERE ii.OperationId = o.OperationId AND i.Status <> N'Cancelled' AND i.IsDeleted = 0
) iv
WHERE o.IsDeleted = 0;
GO

-- 🔴 A1: رصيد العميل — من Payments (مش من CashTransactions زي v1)
CREATE OR ALTER VIEW vw_CustomerInvoiceBalance
AS
SELECT
    i.InvoiceId,
    i.InvoiceNumber,
    i.BranchId,
    i.CustomerId,
    c.NameAr            AS CustomerName,
    i.InvoiceDate,
    i.DueDate,
    i.DocumentTypeCode,
    i.Status,
    i.PaymentStatus,
    i.GrandTotal,
    i.PaidAmount,
    i.GrandTotal - i.PaidAmount AS Balance,
    CASE WHEN i.DueDate IS NOT NULL
          AND i.DueDate < CAST(SYSUTCDATETIME() AS date)
          AND i.PaymentStatus IN (N'Unpaid', N'PartiallyPaid')
         THEN 1 ELSE 0 END AS IsOverdue,
    DATEDIFF(DAY, i.DueDate, CAST(SYSUTCDATETIME() AS date)) AS DaysOverdue
FROM db65922.dbo.Invoices i
INNER JOIN db65922.dbo.Customers c ON c.CustomerId = i.CustomerId
WHERE i.IsDeleted = 0 AND i.Status <> N'Cancelled';
GO

-- قسم 37: كشف حساب العميل
CREATE OR ALTER VIEW vw_CustomerStatement
AS
-- الفواتير (مدين)
SELECT
    i.CustomerId,
    i.InvoiceDate         AS TxDate,
    N'Invoice'            AS TxType,
    i.InvoiceNumber       AS Reference,
    i.GrandTotal          AS Debit,
    CAST(0 AS decimal(19,4)) AS Credit,
    i.Status              AS Status,
    i.InvoiceId           AS SourceId
FROM db65922.dbo.Invoices i
WHERE i.IsDeleted = 0 AND i.Status <> N'Cancelled'

UNION ALL

-- المدفوعات (دائن)
SELECT
    p.CustomerId,
    p.PaymentDate         AS TxDate,
    N'Payment'            AS TxType,
    p.PaymentNumber       AS Reference,
    CAST(0 AS decimal(19,4)) AS Debit,
    p.Amount              AS Credit,
    p.Status              AS Status,
    p.PaymentId           AS SourceId
FROM db65922.dbo.Payments p
WHERE p.IsDeleted = 0 AND p.Status = N'Posted'

-- ⚠️ مرتجعات العهد (CustodyRefund) مش جزء من كشف حساب العميل —
--    دي تسوية بين الشركة والسائق، مش حركة على العميل.
--    كشف الحساب = فواتير (مدين) + مدفوعات (دائن) بس.
;
GO

-- ملخص رصيد كل عميل (للـ Credit Limit — قسم 46)
CREATE OR ALTER VIEW vw_CustomerBalanceSummary
AS
SELECT
    c.CustomerId,
    c.CustomerCode,
    c.NameAr              AS CustomerName,
    c.CreditLimit,
    ISNULL(v.TotalDue, 0) AS TotalDue,
    c.CreditLimit - ISNULL(v.TotalDue, 0) AS AvailableCredit,
    CASE WHEN c.CreditLimit > 0 AND ISNULL(v.TotalDue,0) >= c.CreditLimit
         THEN 1 ELSE 0 END AS IsOverCreditLimit,
    ISNULL(v.OverdueAmount, 0) AS OverdueAmount
FROM db65922.dbo.Customers c
OUTER APPLY (
    SELECT SUM(ib.Balance) AS TotalDue,
           SUM(CASE WHEN ib.IsOverdue = 1 THEN ib.Balance ELSE 0 END) AS OverdueAmount
    FROM db65922.dbo.vw_CustomerInvoiceBalance ib
    WHERE ib.CustomerId = c.CustomerId
) v
WHERE c.IsDeleted = 0;
GO

-- 🔴 A2: الرحلات المجمّعة — رحلة وعملياتها
CREATE OR ALTER VIEW vw_TripSummary
AS
SELECT
    t.TripId,
    t.TripNumber,
    t.BranchId,
    t.Status,
    d.FullName            AS DriverName,
    v.PlateNumber         AS VehiclePlate,
    tr.PlateNumber        AS TrailerPlate,
    t.PlannedStartAt,
    t.ActualStartAt,
    t.ActualEndAt,
    t.TotalDistanceKm,
    op.OperationCount,
    op.CustomerCount,
    -- مصروفات الرحلة
    ISNULL(ex.TripExpenses, 0) AS TripExpenses,
    -- الموزّع منها على العمليات
    ISNULL(al.AllocatedTotal, 0) AS AllocatedTotal,
    ISNULL(ex.TripExpenses, 0) - ISNULL(al.AllocatedTotal, 0) AS UnallocatedCost,
    -- العهد
    ISNULL(cu.IssuedTotal, 0) AS CustodyIssued,
    ISNULL(cu.SpentTotal, 0)  AS CustodySpent,
    ISNULL(cu.ReturnedTotal,0) AS CustodyReturned
FROM db65922.dbo.Trips t
LEFT JOIN db65922.dbo.Drivers  d  ON d.DriverId  = t.DriverId
LEFT JOIN db65922.dbo.Vehicles v  ON v.VehicleId = t.VehicleId
LEFT JOIN db65922.dbo.Trailers tr ON tr.TrailerId = t.TrailerId
OUTER APPLY (
    SELECT COUNT(*) AS OperationCount, COUNT(DISTINCT opx.CustomerId) AS CustomerCount
    FROM db65922.dbo.TripOperations tpo
    INNER JOIN db65922.dbo.Operations opx ON opx.OperationId = tpo.OperationId
    WHERE tpo.TripId = t.TripId
) op
OUTER APPLY (
    SELECT SUM(e.Amount) AS TripExpenses
    FROM db65922.dbo.Expenses e
    WHERE e.TripId = t.TripId AND e.Status <> N'Cancelled' AND e.IsDeleted = 0
) ex
OUTER APPLY (
    SELECT SUM(tca.AllocatedAmount) AS AllocatedTotal
    FROM db65922.dbo.TripCostAllocations tca WHERE tca.TripId = t.TripId
) al
OUTER APPLY (
    SELECT SUM(dc.AmountIssued) AS IssuedTotal, SUM(dc.AmountSpent) AS SpentTotal, SUM(dc.AmountReturned) AS ReturnedTotal
    FROM db65922.dbo.DriverCustodies dc WHERE dc.TripId = t.TripId AND dc.IsDeleted = 0
) cu
WHERE t.IsDeleted = 0;
GO

-- قسم 34: الـ Operations Dashboard
CREATE OR ALTER VIEW vw_DashboardOperations
AS
SELECT
    o.BranchId,
    CAST(SYSUTCDATETIME() AS date) AS ReportDate,
    SUM(CASE WHEN CAST(o.CreatedAt AS date) = CAST(SYSUTCDATETIME() AS date) THEN 1 ELSE 0 END) AS TodayOperations,
    SUM(CASE WHEN o.Status IN (N'Pending',N'Assigned') THEN 1 ELSE 0 END) AS OpenOperations,
    SUM(CASE WHEN o.Status = N'InTransit' THEN 1 ELSE 0 END) AS InTransit,
    SUM(CASE WHEN o.Status = N'Delivered' AND CAST(o.ActualDeliveryAt AS date) = CAST(SYSUTCDATETIME() AS date) THEN 1 ELSE 0 END) AS DeliveredToday,
    SUM(CASE WHEN o.Status IN (N'ExpensesPending',N'CustodyPending') THEN 1 ELSE 0 END) AS PendingSettlement,
    SUM(CASE WHEN o.Status = N'ReadyToClose' THEN 1 ELSE 0 END) AS ReadyToClose,
    SUM(CASE WHEN o.PlannedDate IS NOT NULL AND o.PlannedDate < SYSUTCDATETIME()
              AND o.Status NOT IN (N'Closed',N'Cancelled',N'Delivered') THEN 1 ELSE 0 END) AS DelayedOperations
FROM db65922.dbo.Operations o
WHERE o.IsDeleted = 0
GROUP BY o.BranchId;
GO

-- قسم 34: الـ Fleet Dashboard
CREATE OR ALTER VIEW vw_DashboardFleet
AS
SELECT
    SUM(CASE WHEN v.Status = N'Available'     THEN 1 ELSE 0 END) AS AvailableVehicles,
    SUM(CASE WHEN v.Status = N'InTrip'        THEN 1 ELSE 0 END) AS VehiclesInTrip,
    SUM(CASE WHEN v.Status = N'Maintenance'   THEN 1 ELSE 0 END) AS VehiclesInMaintenance,
    SUM(CASE WHEN v.Status = N'OutOfService'  THEN 1 ELSE 0 END) AS VehiclesOutOfService,
    SUM(CASE WHEN v.LicenseExpiryDate   IS NOT NULL AND v.LicenseExpiryDate   <= DATEADD(DAY, 30, CAST(SYSUTCDATETIME() AS date)) THEN 1 ELSE 0 END) AS LicenseExpiringSoon,
    SUM(CASE WHEN v.InsuranceExpiryDate IS NOT NULL AND v.InsuranceExpiryDate <= DATEADD(DAY, 30, CAST(SYSUTCDATETIME() AS date)) THEN 1 ELSE 0 END) AS InsuranceExpiringSoon,
    SUM(CASE WHEN vm.NextDueDate IS NOT NULL AND vm.NextDueDate <= DATEADD(DAY, 14, CAST(SYSUTCDATETIME() AS date)) THEN 1 ELSE 0 END) AS MaintenanceDue
FROM db65922.dbo.Vehicles v
OUTER APPLY (
    SELECT TOP 1 m.NextDueDate FROM db65922.dbo.VehicleMaintenance m
    WHERE m.VehicleId = v.VehicleId AND m.Status = N'Completed'
    ORDER BY m.MaintenanceDate DESC
) vm
WHERE v.IsDeleted = 0;
GO

PRINT N'✅ FastCom Schema v2 تم إنشاؤه بنجاح.';
PRINT N'⚠️  تذكير: لازم تتعبّى CompanyProfile.TaxNumber و Address قبل إصدار أي فاتورة.';
PRINT N'⚠️  تذكير: لازم محاسبك يؤكد نسب الضريبة في TaxRates قبل الإنتاج.';
PRINT N'⚠️  تذكير: مستخدم الأدمن بيتعمل من التطبيق (ASP.NET Core Identity) مش من SQL.';
GO
