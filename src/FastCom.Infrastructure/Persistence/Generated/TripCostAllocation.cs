using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("TripId", "OperationId", Name = "UQ_TripCostAllocations", IsUnique = true)]
public partial class TripCostAllocation
{
    [Key]
    public long TripCostAllocationId { get; set; }

    public long TripId { get; set; }

    public long OperationId { get; set; }

    [StringLength(20)]
    public string AllocationBasis { get; set; } = null!;

    [Column(TypeName = "decimal(9, 4)")]
    public decimal AllocationPercent { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal AllocatedAmount { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [ForeignKey("OperationId")]
    [InverseProperty("TripCostAllocations")]
    public virtual Operation Operation { get; set; } = null!;

    [ForeignKey("TripId")]
    [InverseProperty("TripCostAllocations")]
    public virtual Trip Trip { get; set; } = null!;
}
