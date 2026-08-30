using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("OperationId", Name = "IX_InvoiceOperations_Operation")]
[Index("InvoiceId", "OperationId", Name = "UQ_InvoiceOperations", IsUnique = true)]
public partial class InvoiceOperation
{
    [Key]
    public long InvoiceOperationId { get; set; }

    public long InvoiceId { get; set; }

    public long OperationId { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("InvoiceId")]
    [InverseProperty("InvoiceOperations")]
    public virtual Invoice Invoice { get; set; } = null!;

    [ForeignKey("OperationId")]
    [InverseProperty("InvoiceOperations")]
    public virtual Operation Operation { get; set; } = null!;
}
