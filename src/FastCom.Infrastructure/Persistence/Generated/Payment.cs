using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("PaymentNumber", Name = "UQ_Payments_Number", IsUnique = true)]
public partial class Payment
{
    [Key]
    public long PaymentId { get; set; }

    [StringLength(40)]
    public string PaymentNumber { get; set; } = null!;

    public int BranchId { get; set; }

    public int CustomerId { get; set; }

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

    public long? ReversedByPaymentId { get; set; }

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

    [Precision(0)]
    public DateTime? DeletedAt { get; set; }

    public int? DeletedBy { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("Payments")]
    public virtual Branch Branch { get; set; } = null!;

    [ForeignKey("CashTransactionId")]
    [InverseProperty("Payments")]
    public virtual CashTransaction? CashTransaction { get; set; }

    [ForeignKey("CustomerId")]
    [InverseProperty("Payments")]
    public virtual Customer Customer { get; set; } = null!;

    [InverseProperty("ReversedByPayment")]
    public virtual ICollection<Payment> InverseReversedByPayment { get; set; } = new List<Payment>();

    [InverseProperty("Payment")]
    public virtual ICollection<PaymentAllocation> PaymentAllocations { get; set; } = new List<PaymentAllocation>();

    [ForeignKey("PaymentMethodId")]
    [InverseProperty("Payments")]
    public virtual PaymentMethod PaymentMethod { get; set; } = null!;

    [ForeignKey("ReversedByPaymentId")]
    [InverseProperty("InverseReversedByPayment")]
    public virtual Payment? ReversedByPayment { get; set; }
}
