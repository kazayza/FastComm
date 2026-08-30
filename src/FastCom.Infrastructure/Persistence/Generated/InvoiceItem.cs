using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("OperationId", Name = "IX_InvoiceItems_Operation")]
public partial class InvoiceItem
{
    [Key]
    public long InvoiceItemId { get; set; }

    public long InvoiceId { get; set; }

    public long? OperationId { get; set; }

    public int? ServiceId { get; set; }

    public long? PriceRuleId { get; set; }

    [StringLength(500)]
    public string Description { get; set; } = null!;

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
    public decimal? LineSubtotal { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal? LineTax { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal? LineTotal { get; set; }

    [ForeignKey("InvoiceId")]
    [InverseProperty("InvoiceItems")]
    public virtual Invoice Invoice { get; set; } = null!;

    [ForeignKey("OperationId")]
    [InverseProperty("InvoiceItems")]
    public virtual Operation? Operation { get; set; }

    [ForeignKey("PriceRuleId")]
    [InverseProperty("InvoiceItems")]
    public virtual CustomerPriceRule? PriceRule { get; set; }

    [ForeignKey("ServiceId")]
    [InverseProperty("InvoiceItems")]
    public virtual Service? Service { get; set; }

    [ForeignKey("TaxRateId")]
    [InverseProperty("InvoiceItems")]
    public virtual TaxRate? TaxRateNavigation { get; set; }
}
