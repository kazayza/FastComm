using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

public partial class CashTransaction
{
    [Key]
    public long CashTransactionId { get; set; }

    public int BranchId { get; set; }

    public int CashBoxId { get; set; }

    [Precision(0)]
    public DateTime TransactionDate { get; set; }

    [StringLength(20)]
    public string TransactionType { get; set; } = null!;

    [Column(TypeName = "decimal(19, 4)")]
    public decimal Amount { get; set; }

    public int? PaymentMethodId { get; set; }

    public int? CustomerId { get; set; }

    public int? SupplierId { get; set; }

    public long? OperationId { get; set; }

    public long? InvoiceId { get; set; }

    public long? CustodyId { get; set; }

    public Guid? TransferGroupId { get; set; }

    public int? CounterpartCashBoxId { get; set; }

    [StringLength(50)]
    public string? ChequeNumber { get; set; }

    public DateOnly? ChequeDate { get; set; }

    [StringLength(100)]
    public string? BankAccount { get; set; }

    [StringLength(100)]
    public string? ReferenceNumber { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(20)]
    public string Status { get; set; } = null!;

    public long? ReversedByTransactionId { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("CashTransactions")]
    public virtual Branch Branch { get; set; } = null!;

    [ForeignKey("CashBoxId")]
    [InverseProperty("CashTransactionCashBoxes")]
    public virtual CashBox CashBox { get; set; } = null!;

    [ForeignKey("CounterpartCashBoxId")]
    [InverseProperty("CashTransactionCounterpartCashBoxes")]
    public virtual CashBox? CounterpartCashBox { get; set; }

    [ForeignKey("CustodyId")]
    [InverseProperty("CashTransactions")]
    public virtual DriverCustody? Custody { get; set; }

    [ForeignKey("CustomerId")]
    [InverseProperty("CashTransactions")]
    public virtual Customer? Customer { get; set; }

    [InverseProperty("ReversedByTransaction")]
    public virtual ICollection<CashTransaction> InverseReversedByTransaction { get; set; } = new List<CashTransaction>();

    [ForeignKey("InvoiceId")]
    [InverseProperty("CashTransactions")]
    public virtual Invoice? Invoice { get; set; }

    [ForeignKey("OperationId")]
    [InverseProperty("CashTransactions")]
    public virtual Operation? Operation { get; set; }

    [ForeignKey("PaymentMethodId")]
    [InverseProperty("CashTransactions")]
    public virtual PaymentMethod? PaymentMethod { get; set; }

    [InverseProperty("CashTransaction")]
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    [ForeignKey("ReversedByTransactionId")]
    [InverseProperty("InverseReversedByTransaction")]
    public virtual CashTransaction? ReversedByTransaction { get; set; }

    [ForeignKey("SupplierId")]
    [InverseProperty("CashTransactions")]
    public virtual Supplier? Supplier { get; set; }

    [InverseProperty("CashTransaction")]
    public virtual ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();
}
