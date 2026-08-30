using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("OperationId", Name = "IX_TripOperations_Operation")]
[Index("TripId", "OperationId", Name = "UQ_TripOperations", IsUnique = true)]
public partial class TripOperation
{
    [Key]
    public long TripOperationId { get; set; }

    public long TripId { get; set; }

    public long OperationId { get; set; }

    public int SequenceNo { get; set; }

    [Precision(0)]
    public DateTime? PickupAt { get; set; }

    [Precision(0)]
    public DateTime? DeliveryAt { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

    [StringLength(500)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("OperationId")]
    [InverseProperty("TripOperations")]
    public virtual Operation Operation { get; set; } = null!;

    [ForeignKey("TripId")]
    [InverseProperty("TripOperations")]
    public virtual Trip Trip { get; set; } = null!;
}
