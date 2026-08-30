using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("TripId", Name = "IX_SupplierInvoiceItems_Trip")]
public partial class SupplierInvoiceItem
{
    [Key]
    public long SupplierInvoiceItemId { get; set; }

    public long SupplierInvoiceId { get; set; }

    public long? TripId { get; set; }

    public long? OperationId { get; set; }

    public int? ExpenseTypeId { get; set; }

    [StringLength(500)]
    public string Description { get; set; } = null!;

    [Column(TypeName = "decimal(18, 3)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(9, 4)")]
    public decimal TaxRate { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal? LineSubtotal { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal? LineTax { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal? LineTotal { get; set; }

    [ForeignKey("ExpenseTypeId")]
    [InverseProperty("SupplierInvoiceItems")]
    public virtual ExpenseType? ExpenseType { get; set; }

    [ForeignKey("OperationId")]
    [InverseProperty("SupplierInvoiceItems")]
    public virtual Operation? Operation { get; set; }

    [ForeignKey("SupplierInvoiceId")]
    [InverseProperty("SupplierInvoiceItems")]
    public virtual SupplierInvoice SupplierInvoice { get; set; } = null!;

    [ForeignKey("TripId")]
    [InverseProperty("SupplierInvoiceItems")]
    public virtual Trip? Trip { get; set; }
}
