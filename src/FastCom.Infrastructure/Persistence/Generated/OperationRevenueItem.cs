using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("OperationId", Name = "IX_OperationRevenueItems_Operation")]
public partial class OperationRevenueItem
{
    [Key]
    public long OperationRevenueItemId { get; set; }

    public long OperationId { get; set; }

    public int ServiceId { get; set; }

    public long? PriceRuleId { get; set; }

    [StringLength(300)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(18, 3)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal Discount { get; set; }

    public int? TaxRateId { get; set; }

    [Column(TypeName = "decimal(9, 4)")]
    public decimal TaxRate { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal? LineNet { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal? LineTax { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("OperationId")]
    [InverseProperty("OperationRevenueItems")]
    public virtual Operation Operation { get; set; } = null!;

    [ForeignKey("PriceRuleId")]
    [InverseProperty("OperationRevenueItems")]
    public virtual CustomerPriceRule? PriceRule { get; set; }

    [ForeignKey("ServiceId")]
    [InverseProperty("OperationRevenueItems")]
    public virtual Service Service { get; set; } = null!;

    [ForeignKey("TaxRateId")]
    [InverseProperty("OperationRevenueItems")]
    public virtual TaxRate? TaxRateNavigation { get; set; }
}
