using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("TripId", Name = "IX_Custodies_Trip")]
[Index("CustodyNumber", Name = "UQ_DriverCustodies_Number", IsUnique = true)]
public partial class DriverCustody
{
    [Key]
    public long CustodyId { get; set; }

    [StringLength(40)]
    public string CustodyNumber { get; set; } = null!;

    public int BranchId { get; set; }

    public long TripId { get; set; }

    [StringLength(20)]
    public string OwnerType { get; set; } = null!;

    public int OwnerId { get; set; }

    [Precision(0)]
    public DateTime CustodyDate { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal AmountIssued { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal AmountSpent { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal AmountReturned { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal AdditionalDue { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

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

    [Precision(0)]
    public DateTime? ClosedAt { get; set; }

    public int? ClosedBy { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("DriverCustodies")]
    public virtual Branch Branch { get; set; } = null!;

    [InverseProperty("Custody")]
    public virtual ICollection<CashTransaction> CashTransactions { get; set; } = new List<CashTransaction>();

    [InverseProperty("Custody")]
    public virtual ICollection<CustodyTransaction> CustodyTransactions { get; set; } = new List<CustodyTransaction>();

    [InverseProperty("Custody")]
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    [ForeignKey("TripId")]
    [InverseProperty("DriverCustodies")]
    public virtual Trip Trip { get; set; } = null!;
}
