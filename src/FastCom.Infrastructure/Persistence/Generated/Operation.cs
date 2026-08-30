using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("BookingId", Name = "IX_Operations_Booking")]
[Index("OperationNumber", Name = "UQ_Operations_Number", IsUnique = true)]
public partial class Operation
{
    [Key]
    public long OperationId { get; set; }

    [StringLength(40)]
    public string OperationNumber { get; set; } = null!;

    public int BranchId { get; set; }

    public long BookingId { get; set; }

    public int CustomerId { get; set; }

    public int? ServiceId { get; set; }

    public int? PortId { get; set; }

    public int? DestinationId { get; set; }

    public int? TripTypeId { get; set; }

    [Precision(0)]
    public DateTime? PlannedDate { get; set; }

    [Precision(0)]
    public DateTime? ActualStartAt { get; set; }

    [Precision(0)]
    public DateTime? ActualDeliveryAt { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

    [Column(TypeName = "decimal(19, 4)")]
    public decimal RevenueNet { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal RevenueTax { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal EstimatedCost { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal ActualCost { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

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

    [Precision(0)]
    public DateTime? ClosedAt { get; set; }

    public int? ClosedBy { get; set; }

    [ForeignKey("BookingId")]
    [InverseProperty("Operations")]
    public virtual Booking Booking { get; set; } = null!;

    [ForeignKey("BranchId")]
    [InverseProperty("Operations")]
    public virtual Branch Branch { get; set; } = null!;

    [InverseProperty("Operation")]
    public virtual ICollection<CashTransaction> CashTransactions { get; set; } = new List<CashTransaction>();

    [ForeignKey("CustomerId")]
    [InverseProperty("Operations")]
    public virtual Customer Customer { get; set; } = null!;

    [ForeignKey("DestinationId")]
    [InverseProperty("Operations")]
    public virtual Destination? Destination { get; set; }

    [InverseProperty("Operation")]
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    [InverseProperty("Operation")]
    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    [InverseProperty("Operation")]
    public virtual ICollection<InvoiceOperation> InvoiceOperations { get; set; } = new List<InvoiceOperation>();

    [InverseProperty("Operation")]
    public virtual ICollection<OperationContainer> OperationContainers { get; set; } = new List<OperationContainer>();

    [InverseProperty("Operation")]
    public virtual ICollection<OperationRevenueItem> OperationRevenueItems { get; set; } = new List<OperationRevenueItem>();

    [ForeignKey("PortId")]
    [InverseProperty("Operations")]
    public virtual Port? Port { get; set; }

    [ForeignKey("ServiceId")]
    [InverseProperty("Operations")]
    public virtual Service? Service { get; set; }

    [InverseProperty("Operation")]
    public virtual ICollection<SupplierInvoiceItem> SupplierInvoiceItems { get; set; } = new List<SupplierInvoiceItem>();

    [InverseProperty("Operation")]
    public virtual ICollection<TripCostAllocation> TripCostAllocations { get; set; } = new List<TripCostAllocation>();

    [InverseProperty("Operation")]
    public virtual ICollection<TripOperation> TripOperations { get; set; } = new List<TripOperation>();

    [ForeignKey("TripTypeId")]
    [InverseProperty("Operations")]
    public virtual TripType? TripType { get; set; }
}
