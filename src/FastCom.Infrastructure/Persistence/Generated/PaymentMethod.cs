using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_PaymentMethods_Code", IsUnique = true)]
public partial class PaymentMethod
{
    [Key]
    public int PaymentMethodId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(100)]
    public string NameAr { get; set; } = null!;

    [StringLength(100)]
    public string? NameEn { get; set; }

    public bool RequiresChequeNo { get; set; }

    public bool IsCashBased { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    [InverseProperty("PaymentMethod")]
    public virtual ICollection<CashTransaction> CashTransactions { get; set; } = new List<CashTransaction>();

    [InverseProperty("PaymentMethod")]
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    [InverseProperty("PaymentMethod")]
    public virtual ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();
}
