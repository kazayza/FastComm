using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("CustodyId", "TransactionType", Name = "IX_CustodyTransactions_Custody")]
public partial class CustodyTransaction
{
    [Key]
    public long CustodyTransactionId { get; set; }

    public long CustodyId { get; set; }

    public long? ExpenseId { get; set; }

    [StringLength(20)]
    public string TransactionType { get; set; } = null!;

    [Column(TypeName = "decimal(19, 4)")]
    public decimal Amount { get; set; }

    [Precision(0)]
    public DateTime TransactionDate { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("CustodyId")]
    [InverseProperty("CustodyTransactions")]
    public virtual DriverCustody Custody { get; set; } = null!;

    [ForeignKey("ExpenseId")]
    [InverseProperty("CustodyTransactions")]
    public virtual Expense? Expense { get; set; }
}
