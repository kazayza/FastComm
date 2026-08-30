using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("ExpenseNumber", Name = "UQ_Expenses_Number", IsUnique = true)]
public partial class Expense
{
    [Key]
    public long ExpenseId { get; set; }

    [StringLength(40)]
    public string ExpenseNumber { get; set; } = null!;

    public int BranchId { get; set; }

    public long? OperationId { get; set; }

    public long? TripId { get; set; }

    public long? CustodyId { get; set; }

    public int? DriverId { get; set; }

    public int? SupplierId { get; set; }

    public int ExpenseTypeId { get; set; }

    [Precision(0)]
    public DateTime ExpenseDate { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal Amount { get; set; }

    public int? TaxRateId { get; set; }

    [Column(TypeName = "decimal(9, 4)")]
    public decimal TaxRate { get; set; }

    public bool IsTaxDeductible { get; set; }

    [StringLength(100)]
    public string? ReferenceNumber { get; set; }

    [StringLength(20)]
    public string PaymentStatus { get; set; } = null!;

    [StringLength(20)]
    public string Status { get; set; } = null!;

    public bool IsApproved { get; set; }

    public int? ApprovedBy { get; set; }

    [Precision(0)]
    public DateTime? ApprovedAt { get; set; }

    [StringLength(500)]
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

    [ForeignKey("BranchId")]
    [InverseProperty("Expenses")]
    public virtual Branch Branch { get; set; } = null!;

    [ForeignKey("CustodyId")]
    [InverseProperty("Expenses")]
    public virtual DriverCustody? Custody { get; set; }

    [InverseProperty("Expense")]
    public virtual ICollection<CustodyTransaction> CustodyTransactions { get; set; } = new List<CustodyTransaction>();

    [ForeignKey("DriverId")]
    [InverseProperty("Expenses")]
    public virtual Driver? Driver { get; set; }

    [ForeignKey("ExpenseTypeId")]
    [InverseProperty("Expenses")]
    public virtual ExpenseType ExpenseType { get; set; } = null!;

    [ForeignKey("OperationId")]
    [InverseProperty("Expenses")]
    public virtual Operation? Operation { get; set; }

    [ForeignKey("SupplierId")]
    [InverseProperty("Expenses")]
    public virtual Supplier? Supplier { get; set; }

    [ForeignKey("TaxRateId")]
    [InverseProperty("Expenses")]
    public virtual TaxRate? TaxRateNavigation { get; set; }

    [ForeignKey("TripId")]
    [InverseProperty("Expenses")]
    public virtual Trip? Trip { get; set; }
}
