using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("BranchCode", Name = "UQ_Branches_Code", IsUnique = true)]
public partial class Branch
{
    [Key]
    public int BranchId { get; set; }

    [StringLength(20)]
    public string BranchCode { get; set; } = null!;

    [StringLength(150)]
    public string NameAr { get; set; } = null!;

    [StringLength(150)]
    public string? NameEn { get; set; }

    [StringLength(500)]
    public string? AddressAr { get; set; }

    [StringLength(500)]
    public string? AddressEn { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? TaxNumber { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [Precision(0)]
    public DateTime? DeletedAt { get; set; }

    public int? DeletedBy { get; set; }

    [InverseProperty("Branch")]
    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    [InverseProperty("Branch")]
    public virtual ICollection<CashBox> CashBoxes { get; set; } = new List<CashBox>();

    [InverseProperty("Branch")]
    public virtual ICollection<CashTransaction> CashTransactions { get; set; } = new List<CashTransaction>();

    [InverseProperty("Branch")]
    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    [InverseProperty("Branch")]
    public virtual ICollection<DriverCustody> DriverCustodies { get; set; } = new List<DriverCustody>();

    [InverseProperty("Branch")]
    public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();

    [InverseProperty("Branch")]
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    [InverseProperty("Branch")]
    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    [InverseProperty("Branch")]
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    [InverseProperty("Branch")]
    public virtual ICollection<NumberSequence> NumberSequences { get; set; } = new List<NumberSequence>();

    [InverseProperty("Branch")]
    public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();

    [InverseProperty("Branch")]
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    [InverseProperty("Branch")]
    public virtual ICollection<SupplierInvoice> SupplierInvoices { get; set; } = new List<SupplierInvoice>();

    [InverseProperty("Branch")]
    public virtual ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();

    [InverseProperty("Branch")]
    public virtual ICollection<Trip> Trips { get; set; } = new List<Trip>();

    [InverseProperty("Branch")]
    public virtual ICollection<VehicleMaintenance> VehicleMaintenances { get; set; } = new List<VehicleMaintenance>();
}
