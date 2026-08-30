using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("BranchId", "Code", Name = "UQ_CashBoxes_BranchCode", IsUnique = true)]
public partial class CashBox
{
    [Key]
    public int CashBoxId { get; set; }

    public int BranchId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(150)]
    public string NameAr { get; set; } = null!;

    [StringLength(150)]
    public string? NameEn { get; set; }

    [StringLength(3)]
    [Unicode(false)]
    public string CurrencyCode { get; set; } = null!;

    [Column(TypeName = "decimal(19, 4)")]
    public decimal OpeningBalance { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal CurrentBalance { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("CashBoxes")]
    public virtual Branch Branch { get; set; } = null!;

    [InverseProperty("CashBox")]
    public virtual ICollection<CashTransaction> CashTransactionCashBoxes { get; set; } = new List<CashTransaction>();

    [InverseProperty("CounterpartCashBox")]
    public virtual ICollection<CashTransaction> CashTransactionCounterpartCashBoxes { get; set; } = new List<CashTransaction>();
}
