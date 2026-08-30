using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("InvoiceId", Name = "IX_PaymentAllocations_Invoice")]
[Index("PaymentId", "InvoiceId", Name = "UQ_PaymentAllocations", IsUnique = true)]
public partial class PaymentAllocation
{
    [Key]
    public long PaymentAllocationId { get; set; }

    public long PaymentId { get; set; }

    public long InvoiceId { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal AllocatedAmount { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("InvoiceId")]
    [InverseProperty("PaymentAllocations")]
    public virtual Invoice Invoice { get; set; } = null!;

    [ForeignKey("PaymentId")]
    [InverseProperty("PaymentAllocations")]
    public virtual Payment Payment { get; set; } = null!;
}
