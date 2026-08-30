using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("SupplierPaymentNumber", Name = "UQ_SupplierPayments_Number", IsUnique = true)]
public partial class SupplierPayment
{
    [Key]
    public long SupplierPaymentId { get; set; }

    [StringLength(40)]
    public string SupplierPaymentNumber { get; set; } = null!;

    public int BranchId { get; set; }

    public int SupplierId { get; set; }

    public DateOnly PaymentDate { get; set; }

    public int PaymentMethodId { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal Amount { get; set; }

    [StringLength(50)]
    public string? ChequeNumber { get; set; }

    public DateOnly? ChequeDate { get; set; }

    [StringLength(100)]
    public string? BankAccount { get; set; }

    [StringLength(20)]
    public string Status { get; set; } = null!;

    public long? CashTransactionId { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("SupplierPayments")]
    public virtual Branch Branch { get; set; } = null!;

    [ForeignKey("CashTransactionId")]
    [InverseProperty("SupplierPayments")]
    public virtual CashTransaction? CashTransaction { get; set; }

    [ForeignKey("PaymentMethodId")]
    [InverseProperty("SupplierPayments")]
    public virtual PaymentMethod PaymentMethod { get; set; } = null!;

    [ForeignKey("SupplierId")]
    [InverseProperty("SupplierPayments")]
    public virtual Supplier Supplier { get; set; } = null!;

    [InverseProperty("SupplierPayment")]
    public virtual ICollection<SupplierInvoiceAllocation> SupplierInvoiceAllocations { get; set; } = new List<SupplierInvoiceAllocation>();
}
