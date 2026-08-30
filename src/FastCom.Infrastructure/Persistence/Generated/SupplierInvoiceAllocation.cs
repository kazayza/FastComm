using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("SupplierPaymentId", "SupplierInvoiceId", Name = "UQ_SupplierInvoiceAllocations", IsUnique = true)]
public partial class SupplierInvoiceAllocation
{
    [Key]
    public long SupplierInvoiceAllocationId { get; set; }

    public long SupplierPaymentId { get; set; }

    public long SupplierInvoiceId { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal AllocatedAmount { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("SupplierInvoiceId")]
    [InverseProperty("SupplierInvoiceAllocations")]
    public virtual SupplierInvoice SupplierInvoice { get; set; } = null!;

    [ForeignKey("SupplierPaymentId")]
    [InverseProperty("SupplierInvoiceAllocations")]
    public virtual SupplierPayment SupplierPayment { get; set; } = null!;
}
