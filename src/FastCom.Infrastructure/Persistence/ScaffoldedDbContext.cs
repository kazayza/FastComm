using System;
using System.Collections.Generic;
using FastCom.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Infrastructure.Persistence;

public partial class FastComDbContext
{


    public virtual DbSet<AppPermission> AppPermissions { get; set; }

    public virtual DbSet<AppRolePermission> AppRolePermissions { get; set; }

    public virtual DbSet<AppUserPermission> AppUserPermissions { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Booking> Bookings { get; set; }

    public virtual DbSet<BookingContainerDetail> BookingContainerDetails { get; set; }

    public virtual DbSet<BookingContainerLine> BookingContainerLines { get; set; }

    public virtual DbSet<Branch> Branches { get; set; }

    public virtual DbSet<CashBox> CashBoxes { get; set; }

    public virtual DbSet<CashTransaction> CashTransactions { get; set; }

    public virtual DbSet<CompanyProfile> CompanyProfiles { get; set; }

    public virtual DbSet<Container> Containers { get; set; }

    public virtual DbSet<ContainerMovement> ContainerMovements { get; set; }

    public virtual DbSet<ContainerType> ContainerTypes { get; set; }

    public virtual DbSet<CustodyTransaction> CustodyTransactions { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomerContact> CustomerContacts { get; set; }

    public virtual DbSet<CustomerPortalToken> CustomerPortalTokens { get; set; }

    public virtual DbSet<CustomerPriceRule> CustomerPriceRules { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<Destination> Destinations { get; set; }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<DocumentType> DocumentTypes { get; set; }

    public virtual DbSet<Driver> Drivers { get; set; }

    public virtual DbSet<DriverCustody> DriverCustodies { get; set; }

    public virtual DbSet<Employee> Employees { get; set; }

    public virtual DbSet<Expense> Expenses { get; set; }

    public virtual DbSet<ExpenseType> ExpenseTypes { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<InvoiceItem> InvoiceItems { get; set; }

    public virtual DbSet<InvoiceOperation> InvoiceOperations { get; set; }

    public virtual DbSet<InvoiceSendLog> InvoiceSendLogs { get; set; }

    public virtual DbSet<JobTitle> JobTitles { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<NumberSequence> NumberSequences { get; set; }

    public virtual DbSet<Operation> Operations { get; set; }

    public virtual DbSet<OperationContainer> OperationContainers { get; set; }

    public virtual DbSet<OperationRevenueItem> OperationRevenueItems { get; set; }

    public virtual DbSet<OperationStatus> OperationStatuses { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<PaymentAllocation> PaymentAllocations { get; set; }

    public virtual DbSet<PaymentMethod> PaymentMethods { get; set; }

    public virtual DbSet<PaymentTerm> PaymentTerms { get; set; }

    public virtual DbSet<Port> Ports { get; set; }

    public virtual DbSet<PortalAccessLog> PortalAccessLogs { get; set; }

    public virtual DbSet<PriceList> PriceLists { get; set; }

    public virtual DbSet<Service> Services { get; set; }

    public virtual DbSet<Supplier> Suppliers { get; set; }

    public virtual DbSet<SupplierInvoice> SupplierInvoices { get; set; }

    public virtual DbSet<SupplierInvoiceAllocation> SupplierInvoiceAllocations { get; set; }

    public virtual DbSet<SupplierInvoiceItem> SupplierInvoiceItems { get; set; }

    public virtual DbSet<SupplierPayment> SupplierPayments { get; set; }

    public virtual DbSet<SystemSetting> SystemSettings { get; set; }

    public virtual DbSet<TaxRate> TaxRates { get; set; }

    public virtual DbSet<Trailer> Trailers { get; set; }

    public virtual DbSet<Trip> Trips { get; set; }

    public virtual DbSet<TripCostAllocation> TripCostAllocations { get; set; }

    public virtual DbSet<TripOperation> TripOperations { get; set; }

    public virtual DbSet<TripType> TripTypes { get; set; }

    public virtual DbSet<Vehicle> Vehicles { get; set; }

    public virtual DbSet<VehicleMaintenance> VehicleMaintenances { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("Albanian_CI_AI");

        modelBuilder.Entity<AppPermission>(entity =>
        {
            entity.Property(e => e.SortOrder).HasDefaultValue(100);
        });

        modelBuilder.Entity<AppRolePermission>(entity =>
        {
            entity.Property(e => e.GrantedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Permission).WithMany(p => p.AppRolePermissions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ARP_Permissions");
        });

        modelBuilder.Entity<AppUserPermission>(entity =>
        {
            entity.Property(e => e.GrantedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsGranted).HasDefaultValue(true);

            entity.HasOne(d => d.Permission).WithMany(p => p.AppUserPermissions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AUP_Permissions");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasIndex(e => new { e.BranchId, e.Status }, "IX_Bookings_Branch_Status").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.PortId, e.RequestedDate }, "IX_Bookings_Port_Date").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.BookingDate).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue("Draft");

            entity.HasOne(d => d.Branch).WithMany(p => p.Bookings)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Bookings_Branches");

            entity.HasOne(d => d.Contact).WithMany(p => p.Bookings).HasConstraintName("FK_Bookings_Contacts");

            entity.HasOne(d => d.Customer).WithMany(p => p.Bookings)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Bookings_Customers");

            entity.HasOne(d => d.Destination).WithMany(p => p.Bookings).HasConstraintName("FK_Bookings_Destinations");

            entity.HasOne(d => d.Port).WithMany(p => p.Bookings).HasConstraintName("FK_Bookings_Ports");

            entity.HasOne(d => d.Service).WithMany(p => p.Bookings).HasConstraintName("FK_Bookings_Services");

            entity.HasOne(d => d.TripType).WithMany(p => p.Bookings).HasConstraintName("FK_Bookings_TripTypes");
        });

        modelBuilder.Entity<BookingContainerDetail>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_BookingContainerDetails_QtySync"));

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue("Pending");

            entity.HasOne(d => d.BookingContainerLine).WithMany(p => p.BookingContainerDetails)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_BCD_Lines");

            entity.HasOne(d => d.Container).WithMany(p => p.BookingContainerDetails).HasConstraintName("FK_BCD_Containers");
        });

        modelBuilder.Entity<BookingContainerLine>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue("Pending");

            entity.HasOne(d => d.Booking).WithMany(p => p.BookingContainerLines)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_BCL_Bookings");

            entity.HasOne(d => d.ContainerType).WithMany(p => p.BookingContainerLines)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_BCL_ContainerTypes");
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<CashBox>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CurrencyCode)
                .HasDefaultValue("EGP")
                .IsFixedLength();
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Branch).WithMany(p => p.CashBoxes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CashBoxes_Branches");
        });

        modelBuilder.Entity<CashTransaction>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_CashTransactions_BalanceSync"));

            entity.HasIndex(e => new { e.CashBoxId, e.TransactionDate }, "IX_CashTransactions_Box_Date").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.CustomerId, e.TransactionType, e.TransactionDate }, "IX_CashTransactions_Customer").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue("Posted");
            entity.Property(e => e.TransactionDate).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Branch).WithMany(p => p.CashTransactions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CashTx_Branches");

            entity.HasOne(d => d.CashBox).WithMany(p => p.CashTransactionCashBoxes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CashTx_CashBoxes");

            entity.HasOne(d => d.CounterpartCashBox).WithMany(p => p.CashTransactionCounterpartCashBoxes).HasConstraintName("FK_CashTx_Counterpart");

            entity.HasOne(d => d.Custody).WithMany(p => p.CashTransactions).HasConstraintName("FK_CashTx_Custodies");

            entity.HasOne(d => d.Customer).WithMany(p => p.CashTransactions).HasConstraintName("FK_CashTx_Customers");

            entity.HasOne(d => d.Invoice).WithMany(p => p.CashTransactions).HasConstraintName("FK_CashTx_Invoices");

            entity.HasOne(d => d.Operation).WithMany(p => p.CashTransactions).HasConstraintName("FK_CashTx_Operations");

            entity.HasOne(d => d.PaymentMethod).WithMany(p => p.CashTransactions).HasConstraintName("FK_CashTx_Methods");

            entity.HasOne(d => d.ReversedByTransaction).WithMany(p => p.InverseReversedByTransaction).HasConstraintName("FK_CashTx_Reversed");

            entity.HasOne(d => d.Supplier).WithMany(p => p.CashTransactions).HasConstraintName("FK_CashTx_Suppliers");
        });

        modelBuilder.Entity<CompanyProfile>(entity =>
        {
            entity.Property(e => e.CompanyProfileId).ValueGeneratedNever();
            entity.Property(e => e.DefaultCurrencyCode)
                .HasDefaultValue("EGP")
                .IsFixedLength();
            entity.Property(e => e.FiscalYearStartMonth).HasDefaultValue((byte)1);
        });

        modelBuilder.Entity<Container>(entity =>
        {
            entity.HasIndex(e => e.CurrentStatus, "IX_Containers_Status").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CurrentStatus).HasDefaultValue("Available");
            entity.Property(e => e.OwnerType).HasDefaultValue("ShippingLine");

            entity.HasOne(d => d.ContainerType).WithMany(p => p.Containers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Containers_Types");
        });

        modelBuilder.Entity<ContainerMovement>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.MovementDate).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Container).WithMany(p => p.ContainerMovements)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ContainerMovements_Containers");

            entity.HasOne(d => d.Port).WithMany(p => p.ContainerMovements).HasConstraintName("FK_ContainerMovements_Ports");
        });

        modelBuilder.Entity<ContainerType>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<CustodyTransaction>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_CustodyTransactions_Sync"));

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.TransactionDate).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Custody).WithMany(p => p.CustodyTransactions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CustodyTx_Custody");

            entity.HasOne(d => d.Expense).WithMany(p => p.CustodyTransactions).HasConstraintName("FK_CustodyTx_Expense");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CustomerType).HasDefaultValue("Company");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PreferredLanguage)
                .HasDefaultValue("ar")
                .IsFixedLength();

            entity.HasOne(d => d.PaymentTerm).WithMany(p => p.Customers).HasConstraintName("FK_Customers_PaymentTerms");
        });

        modelBuilder.Entity<CustomerContact>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerContacts)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CustomerContacts_Customers");
        });

        modelBuilder.Entity<CustomerPortalToken>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Purpose).HasDefaultValue("Portal");

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerPortalTokens)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PortalTokens_Customers");

            entity.HasOne(d => d.Invoice).WithMany(p => p.CustomerPortalTokens).HasConstraintName("FK_PortalTokens_Invoices");
        });

        modelBuilder.Entity<CustomerPriceRule>(entity =>
        {
            entity.HasIndex(e => new { e.CustomerId, e.ServiceId, e.PortId, e.ContainerTypeId, e.ValidFrom, e.ValidTo }, "IX_CustomerPriceRules_Lookup").HasFilter("([IsDeleted]=(0) AND [IsActive]=(1))");

            entity.HasIndex(e => new { e.CustomerId, e.ServiceId, e.PortId, e.DestinationId, e.ContainerTypeId, e.TripTypeId, e.ValidFrom }, "UX_CustomerPriceRules_NoOverlap")
                .IsUnique()
                .HasFilter("([IsDeleted]=(0) AND [IsActive]=(1))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Priority).HasDefaultValue(100);

            entity.HasOne(d => d.ContainerType).WithMany(p => p.CustomerPriceRules).HasConstraintName("FK_CPR_ContainerTypes");

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerPriceRules)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CPR_Customers");

            entity.HasOne(d => d.Destination).WithMany(p => p.CustomerPriceRules).HasConstraintName("FK_CPR_Destinations");

            entity.HasOne(d => d.Port).WithMany(p => p.CustomerPriceRules).HasConstraintName("FK_CPR_Ports");

            entity.HasOne(d => d.PriceList).WithMany(p => p.CustomerPriceRules).HasConstraintName("FK_CPR_PriceLists");

            entity.HasOne(d => d.Service).WithMany(p => p.CustomerPriceRules)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CPR_Services");

            entity.HasOne(d => d.TaxRate).WithMany(p => p.CustomerPriceRules).HasConstraintName("FK_CPR_TaxRates");

            entity.HasOne(d => d.TripType).WithMany(p => p.CustomerPriceRules).HasConstraintName("FK_CPR_TripTypes");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Destination>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasIndex(e => new { e.EntityType, e.EntityId }, "IX_Documents_Entity").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => e.ExpiryDate, "IX_Documents_Expiry").HasFilter("([IsDeleted]=(0) AND [ExpiryDate] IS NOT NULL)");

            entity.Property(e => e.StorageProvider).HasDefaultValue("Local");
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Branch).WithMany(p => p.Documents).HasConstraintName("FK_Documents_Branches");

            entity.HasOne(d => d.DocumentType).WithMany(p => p.Documents)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Documents_Types");
        });

        modelBuilder.Entity<DocumentType>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Driver>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.DriverType).HasDefaultValue("Internal");
            entity.Property(e => e.Status).HasDefaultValue("Active");

            entity.HasOne(d => d.Employee).WithMany(p => p.Drivers).HasConstraintName("FK_Drivers_Employees");

            entity.HasOne(d => d.Supplier).WithMany(p => p.Drivers).HasConstraintName("FK_Drivers_Suppliers");
        });

        modelBuilder.Entity<DriverCustody>(entity =>
        {
            entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.Status }, "IX_Custodies_Owner_Status").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CustodyDate).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.OwnerType).HasDefaultValue("Driver");
            entity.Property(e => e.Status).HasDefaultValue("Open");

            entity.HasOne(d => d.Branch).WithMany(p => p.DriverCustodies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Custodies_Branches");

            entity.HasOne(d => d.Trip).WithMany(p => p.DriverCustodies)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Custodies_Trips");
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.EmploymentStatus).HasDefaultValue("Active");

            entity.HasOne(d => d.Branch).WithMany(p => p.Employees).HasConstraintName("FK_Employees_Branches");

            entity.HasOne(d => d.Department).WithMany(p => p.Employees).HasConstraintName("FK_Employees_Departments");

            entity.HasOne(d => d.JobTitle).WithMany(p => p.Employees).HasConstraintName("FK_Employees_JobTitles");

            entity.HasOne(d => d.ManagerEmployee).WithMany(p => p.InverseManagerEmployee).HasConstraintName("FK_Employees_Manager");
        });

        modelBuilder.Entity<Expense>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_Expenses_OpCostSync"));

            entity.HasIndex(e => e.CustodyId, "IX_Expenses_Custody").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.DriverId, e.ExpenseDate }, "IX_Expenses_Driver").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.OperationId, e.ExpenseDate }, "IX_Expenses_Operation").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => e.TripId, "IX_Expenses_Trip").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.PaymentStatus, e.ExpenseDate }, "IX_Expenses_Unpaid").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.ExpenseDate).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsTaxDeductible).HasDefaultValue(true);
            entity.Property(e => e.PaymentStatus).HasDefaultValue("Unpaid");
            entity.Property(e => e.Status).HasDefaultValue("Posted");

            entity.HasOne(d => d.Branch).WithMany(p => p.Expenses)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Expenses_Branches");

            entity.HasOne(d => d.Custody).WithMany(p => p.Expenses).HasConstraintName("FK_Expenses_Custodies");

            entity.HasOne(d => d.Driver).WithMany(p => p.Expenses).HasConstraintName("FK_Expenses_Drivers");

            entity.HasOne(d => d.ExpenseType).WithMany(p => p.Expenses)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Expenses_Types");

            entity.HasOne(d => d.Operation).WithMany(p => p.Expenses).HasConstraintName("FK_Expenses_Operations");

            entity.HasOne(d => d.Supplier).WithMany(p => p.Expenses).HasConstraintName("FK_Expenses_Suppliers");

            entity.HasOne(d => d.TaxRateNavigation).WithMany(p => p.Expenses).HasConstraintName("FK_Expenses_TaxRates");

            entity.HasOne(d => d.Trip).WithMany(p => p.Expenses).HasConstraintName("FK_Expenses_Trips");
        });

        modelBuilder.Entity<ExpenseType>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsCustodyAllowed).HasDefaultValue(true);
            entity.Property(e => e.IsOperationCost).HasDefaultValue(true);
            entity.Property(e => e.IsTaxDeductible).HasDefaultValue(true);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasIndex(e => new { e.BranchId, e.Status, e.PaymentStatus }, "IX_Invoices_Branch_Status").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.CustomerId, e.InvoiceDate, e.Status }, "IX_Invoices_Customer_Date").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CurrencyCode)
                .HasDefaultValue("EGP")
                .IsFixedLength();
            entity.Property(e => e.DocumentTypeCode).HasDefaultValue("Invoice");
            entity.Property(e => e.PaymentStatus).HasDefaultValue("Unpaid");
            entity.Property(e => e.Status).HasDefaultValue("Draft");

            entity.HasOne(d => d.Branch).WithMany(p => p.Invoices)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Invoices_Branches");

            entity.HasOne(d => d.Customer).WithMany(p => p.Invoices)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Invoices_Customers");

            entity.HasOne(d => d.OriginalInvoice).WithMany(p => p.InverseOriginalInvoice).HasConstraintName("FK_Invoices_Original");
        });

        modelBuilder.Entity<InvoiceItem>(entity =>
        {
            entity.Property(e => e.LineSubtotal).HasComputedColumnSql("(CONVERT([decimal](19,4),[Quantity]*[UnitPrice]-[Discount]))", true);
            entity.Property(e => e.LineTax).HasComputedColumnSql("(CONVERT([decimal](19,4),(([Quantity]*[UnitPrice]-[Discount])*[TaxRate])/(100.0)))", true);
            entity.Property(e => e.LineTotal).HasComputedColumnSql("(CONVERT([decimal](19,4),([Quantity]*[UnitPrice]-[Discount])*((1)+[TaxRate]/(100.0))))", true);

            entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceItems)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InvoiceItems_Invoices");

            entity.HasOne(d => d.Operation).WithMany(p => p.InvoiceItems).HasConstraintName("FK_InvoiceItems_Operations");

            entity.HasOne(d => d.PriceRule).WithMany(p => p.InvoiceItems).HasConstraintName("FK_InvoiceItems_PriceRules");

            entity.HasOne(d => d.Service).WithMany(p => p.InvoiceItems).HasConstraintName("FK_InvoiceItems_Services");

            entity.HasOne(d => d.TaxRateNavigation).WithMany(p => p.InvoiceItems).HasConstraintName("FK_InvoiceItems_TaxRates");
        });

        modelBuilder.Entity<InvoiceOperation>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceOperations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InvoiceOperations_Invoices");

            entity.HasOne(d => d.Operation).WithMany(p => p.InvoiceOperations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InvoiceOperations_Operations");
        });

        modelBuilder.Entity<InvoiceSendLog>(entity =>
        {
            entity.Property(e => e.Channel).HasDefaultValue("Email");
            entity.Property(e => e.SentAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue("Success");

            entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceSendLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InvoiceSendLogs_Invoices");
        });

        modelBuilder.Entity<JobTitle>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Priority).HasDefaultValue("Normal");

            entity.HasOne(d => d.Branch).WithMany(p => p.Notifications).HasConstraintName("FK_Notifications_Branches");
        });

        modelBuilder.Entity<NumberSequence>(entity =>
        {
            entity.HasIndex(e => new { e.BranchId, e.DocumentType, e.Year }, "UX_NumberSequences_Branch")
                .IsUnique()
                .HasFilter("([BranchId] IS NOT NULL)");

            entity.HasIndex(e => new { e.DocumentType, e.Year }, "UX_NumberSequences_Global")
                .IsUnique()
                .HasFilter("([BranchId] IS NULL)");

            entity.Property(e => e.NumberLength).HasDefaultValue((byte)6);
            entity.Property(e => e.ResetPeriod).HasDefaultValue("Yearly");
            entity.Property(e => e.Year).HasDefaultValueSql("(datepart(year,sysutcdatetime()))");

            entity.HasOne(d => d.Branch).WithMany(p => p.NumberSequences).HasConstraintName("FK_NumberSequences_Branches");
        });

        modelBuilder.Entity<Operation>(entity =>
        {
            entity.HasIndex(e => new { e.BranchId, e.Status }, "IX_Operations_Branch_Status").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.CustomerId, e.Status, e.PlannedDate }, "IX_Operations_Customer_Status").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue("Pending");

            entity.HasOne(d => d.Booking).WithMany(p => p.Operations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Operations_Bookings");

            entity.HasOne(d => d.Branch).WithMany(p => p.Operations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Operations_Branches");

            entity.HasOne(d => d.Customer).WithMany(p => p.Operations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Operations_Customers");

            entity.HasOne(d => d.Destination).WithMany(p => p.Operations).HasConstraintName("FK_Operations_Destinations");

            entity.HasOne(d => d.Port).WithMany(p => p.Operations).HasConstraintName("FK_Operations_Ports");

            entity.HasOne(d => d.Service).WithMany(p => p.Operations).HasConstraintName("FK_Operations_Services");

            entity.HasOne(d => d.TripType).WithMany(p => p.Operations).HasConstraintName("FK_Operations_TripTypes");
        });

        modelBuilder.Entity<OperationContainer>(entity =>
        {
            entity.HasIndex(e => new { e.OperationId, e.ContainerId }, "UX_OperationContainers_OpContainer")
                .IsUnique()
                .HasFilter("([ContainerId] IS NOT NULL)");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.MovementSequence).HasDefaultValue(1);
            entity.Property(e => e.Status).HasDefaultValue("Assigned");

            entity.HasOne(d => d.BookingContainerDetail).WithMany(p => p.OperationContainers).HasConstraintName("FK_OperationContainers_Details");

            entity.HasOne(d => d.Container).WithMany(p => p.OperationContainers).HasConstraintName("FK_OperationContainers_Containers");

            entity.HasOne(d => d.Operation).WithMany(p => p.OperationContainers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OperationContainers_Operations");
        });

        modelBuilder.Entity<OperationRevenueItem>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_OperationRevenueItems_Sync"));

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.LineNet).HasComputedColumnSql("(CONVERT([decimal](19,4),[Quantity]*[UnitPrice]-[Discount]))", true);
            entity.Property(e => e.LineTax).HasComputedColumnSql("(CONVERT([decimal](19,4),(([Quantity]*[UnitPrice]-[Discount])*[TaxRate])/(100.0)))", true);

            entity.HasOne(d => d.Operation).WithMany(p => p.OperationRevenueItems)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ORI_Operations");

            entity.HasOne(d => d.PriceRule).WithMany(p => p.OperationRevenueItems).HasConstraintName("FK_ORI_PriceRules");

            entity.HasOne(d => d.Service).WithMany(p => p.OperationRevenueItems)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ORI_Services");

            entity.HasOne(d => d.TaxRateNavigation).WithMany(p => p.OperationRevenueItems).HasConstraintName("FK_ORI_TaxRates");
        });

        modelBuilder.Entity<OperationStatus>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.SortOrder).HasDefaultValue(100);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasIndex(e => new { e.CustomerId, e.PaymentDate }, "IX_Payments_Customer").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue("Posted");

            entity.HasOne(d => d.Branch).WithMany(p => p.Payments)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payments_Branches");

            entity.HasOne(d => d.CashTransaction).WithMany(p => p.Payments).HasConstraintName("FK_Payments_CashTx");

            entity.HasOne(d => d.Customer).WithMany(p => p.Payments)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payments_Customers");

            entity.HasOne(d => d.PaymentMethod).WithMany(p => p.Payments)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payments_Methods");

            entity.HasOne(d => d.ReversedByPayment).WithMany(p => p.InverseReversedByPayment).HasConstraintName("FK_Payments_Reversed");
        });

        modelBuilder.Entity<PaymentAllocation>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_PaymentAllocations_InvoiceSync"));

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Invoice).WithMany(p => p.PaymentAllocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PaymentAlloc_Invoices");

            entity.HasOne(d => d.Payment).WithMany(p => p.PaymentAllocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PaymentAlloc_Payments");
        });

        modelBuilder.Entity<PaymentMethod>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsCashBased).HasDefaultValue(true);
            entity.Property(e => e.SortOrder).HasDefaultValue(100);
        });

        modelBuilder.Entity<PaymentTerm>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Port>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<PortalAccessLog>(entity =>
        {
            entity.Property(e => e.AccessedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Action).HasDefaultValue("View");

            entity.HasOne(d => d.Customer).WithMany(p => p.PortalAccessLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PortalAccess_Customers");

            entity.HasOne(d => d.PortalToken).WithMany(p => p.PortalAccessLogs).HasConstraintName("FK_PortalAccess_Tokens");
        });

        modelBuilder.Entity<PriceList>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CurrencyCode)
                .HasDefaultValue("EGP")
                .IsFixedLength();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Service>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsTaxable).HasDefaultValue(true);
            entity.Property(e => e.Unit).HasDefaultValue("Trip");

            entity.HasOne(d => d.TaxRate).WithMany(p => p.Services).HasConstraintName("FK_Services_TaxRates");
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.SupplierType).HasDefaultValue("Other");

            entity.HasOne(d => d.PaymentTerm).WithMany(p => p.Suppliers).HasConstraintName("FK_Suppliers_PaymentTerms");
        });

        modelBuilder.Entity<SupplierInvoice>(entity =>
        {
            entity.HasIndex(e => new { e.SupplierId, e.InvoiceDate, e.Status }, "IX_SupplierInvoices_Supplier").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CurrencyCode)
                .HasDefaultValue("EGP")
                .IsFixedLength();
            entity.Property(e => e.PaymentStatus).HasDefaultValue("Unpaid");
            entity.Property(e => e.Status).HasDefaultValue("Draft");

            entity.HasOne(d => d.Branch).WithMany(p => p.SupplierInvoices)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupplierInvoices_Branches");

            entity.HasOne(d => d.Supplier).WithMany(p => p.SupplierInvoices)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupplierInvoices_Suppliers");
        });

        modelBuilder.Entity<SupplierInvoiceAllocation>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_SupplierInvoiceAllocations_Sync"));

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.SupplierInvoice).WithMany(p => p.SupplierInvoiceAllocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SIA_Invoices");

            entity.HasOne(d => d.SupplierPayment).WithMany(p => p.SupplierInvoiceAllocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SIA_Payments");
        });

        modelBuilder.Entity<SupplierInvoiceItem>(entity =>
        {
            entity.Property(e => e.LineSubtotal).HasComputedColumnSql("(CONVERT([decimal](19,4),[Quantity]*[UnitPrice]))", true);
            entity.Property(e => e.LineTax).HasComputedColumnSql("(CONVERT([decimal](19,4),(([Quantity]*[UnitPrice])*[TaxRate])/(100.0)))", true);
            entity.Property(e => e.LineTotal).HasComputedColumnSql("(CONVERT([decimal](19,4),([Quantity]*[UnitPrice])*((1)+[TaxRate]/(100.0))))", true);

            entity.HasOne(d => d.ExpenseType).WithMany(p => p.SupplierInvoiceItems).HasConstraintName("FK_SII_Types");

            entity.HasOne(d => d.Operation).WithMany(p => p.SupplierInvoiceItems).HasConstraintName("FK_SII_Operations");

            entity.HasOne(d => d.SupplierInvoice).WithMany(p => p.SupplierInvoiceItems)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SII_Invoices");

            entity.HasOne(d => d.Trip).WithMany(p => p.SupplierInvoiceItems).HasConstraintName("FK_SII_Trips");
        });

        modelBuilder.Entity<SupplierPayment>(entity =>
        {
            entity.HasIndex(e => new { e.SupplierId, e.PaymentDate }, "IX_SupplierPayments_Supplier").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue("Posted");

            entity.HasOne(d => d.Branch).WithMany(p => p.SupplierPayments)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupplierPayments_Branches");

            entity.HasOne(d => d.CashTransaction).WithMany(p => p.SupplierPayments).HasConstraintName("FK_SupplierPayments_CashTx");

            entity.HasOne(d => d.PaymentMethod).WithMany(p => p.SupplierPayments)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupplierPayments_Methods");

            entity.HasOne(d => d.Supplier).WithMany(p => p.SupplierPayments)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupplierPayments_Suppliers");
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.Property(e => e.ValueType).HasDefaultValue("String");
        });

        modelBuilder.Entity<TaxRate>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsDeductible).HasDefaultValue(true);
            entity.Property(e => e.ValidFrom).HasDefaultValue(new DateOnly(2016, 9, 8));
        });

        modelBuilder.Entity<Trailer>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.OwnershipType).HasDefaultValue("Company");
            entity.Property(e => e.Status).HasDefaultValue("Available");

            entity.HasOne(d => d.Supplier).WithMany(p => p.Trailers).HasConstraintName("FK_Trailers_Suppliers");
        });

        modelBuilder.Entity<Trip>(entity =>
        {
            entity.HasIndex(e => new { e.BranchId, e.Status }, "IX_Trips_Branch_Status").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.DriverId, e.PlannedStartAt }, "IX_Trips_Driver_Date").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.TrailerId, e.PlannedStartAt }, "IX_Trips_Trailer_Date").HasFilter("([IsDeleted]=(0))");

            entity.HasIndex(e => new { e.VehicleId, e.PlannedStartAt }, "IX_Trips_Vehicle_Date").HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue("Planned");

            entity.HasOne(d => d.Branch).WithMany(p => p.Trips)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Trips_Branches");

            entity.HasOne(d => d.Driver).WithMany(p => p.Trips)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Trips_Drivers");

            entity.HasOne(d => d.Trailer).WithMany(p => p.Trips).HasConstraintName("FK_Trips_Trailers");

            entity.HasOne(d => d.TripType).WithMany(p => p.Trips).HasConstraintName("FK_Trips_TripTypes");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.Trips)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Trips_Vehicles");
        });

        modelBuilder.Entity<TripCostAllocation>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("trg_Operations_CostSync"));

            entity.Property(e => e.AllocationBasis).HasDefaultValue("Manual");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Operation).WithMany(p => p.TripCostAllocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TCA_Operation");

            entity.HasOne(d => d.Trip).WithMany(p => p.TripCostAllocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TCA_Trip");
        });

        modelBuilder.Entity<TripOperation>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.SequenceNo).HasDefaultValue(1);
            entity.Property(e => e.Status).HasDefaultValue("Assigned");

            entity.HasOne(d => d.Operation).WithMany(p => p.TripOperations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TripOperations_Operation");

            entity.HasOne(d => d.Trip).WithMany(p => p.TripOperations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TripOperations_Trip");
        });

        modelBuilder.Entity<TripType>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.DefaultTaxRate).WithMany(p => p.TripTypes).HasConstraintName("FK_TripTypes_TaxRates");
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.Property(e => e.ContainerSlots20).HasDefaultValue((byte)1);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.OwnershipType).HasDefaultValue("Company");
            entity.Property(e => e.Status).HasDefaultValue("Available");

            entity.HasOne(d => d.Supplier).WithMany(p => p.Vehicles).HasConstraintName("FK_Vehicles_Suppliers");
        });

        modelBuilder.Entity<VehicleMaintenance>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status).HasDefaultValue("Completed");

            entity.HasOne(d => d.Branch).WithMany(p => p.VehicleMaintenances)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VehMaint_Branches");

            entity.HasOne(d => d.Supplier).WithMany(p => p.VehicleMaintenances).HasConstraintName("FK_VehMaint_Suppliers");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.VehicleMaintenances)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VehMaint_Vehicles");
        });

// (removed - would recurse)
    
        // ============================================================
        //  FastCom: Global Query Filter for soft-deleted rows
        //  (every query now ignores rows where IsDeleted = 1)
        // ============================================================
        modelBuilder.ApplySoftDeleteFilters();
    }


}
