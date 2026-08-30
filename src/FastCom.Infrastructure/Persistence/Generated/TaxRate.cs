using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_TaxRates_Code", IsUnique = true)]
public partial class TaxRate
{
    [Key]
    public int TaxRateId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(100)]
    public string NameAr { get; set; } = null!;

    [StringLength(100)]
    public string? NameEn { get; set; }

    [Column(TypeName = "decimal(9, 4)")]
    public decimal Rate { get; set; }

    [StringLength(20)]
    public string TaxKind { get; set; } = null!;

    public bool IsDeductible { get; set; }

    public bool RequiresExemptionReason { get; set; }

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public bool IsActive { get; set; }

    [InverseProperty("TaxRate")]
    public virtual ICollection<CustomerPriceRule> CustomerPriceRules { get; set; } = new List<CustomerPriceRule>();

    [InverseProperty("TaxRateNavigation")]
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    [InverseProperty("TaxRateNavigation")]
    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    [InverseProperty("TaxRateNavigation")]
    public virtual ICollection<OperationRevenueItem> OperationRevenueItems { get; set; } = new List<OperationRevenueItem>();

    [InverseProperty("TaxRate")]
    public virtual ICollection<Service> Services { get; set; } = new List<Service>();

    [InverseProperty("DefaultTaxRate")]
    public virtual ICollection<TripType> TripTypes { get; set; } = new List<TripType>();
}
