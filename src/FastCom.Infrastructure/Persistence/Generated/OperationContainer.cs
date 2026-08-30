using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("ContainerId", "OperationId", Name = "IX_OperationContainers_Container")]
public partial class OperationContainer
{
    [Key]
    public long OperationContainerId { get; set; }

    public long OperationId { get; set; }

    public long? ContainerId { get; set; }

    public long? BookingContainerDetailId { get; set; }

    public int MovementSequence { get; set; }

    [Column("BLNumber")]
    [StringLength(100)]
    public string? Blnumber { get; set; }

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

    [ForeignKey("BookingContainerDetailId")]
    [InverseProperty("OperationContainers")]
    public virtual BookingContainerDetail? BookingContainerDetail { get; set; }

    [ForeignKey("ContainerId")]
    [InverseProperty("OperationContainers")]
    public virtual Container? Container { get; set; }

    [ForeignKey("OperationId")]
    [InverseProperty("OperationContainers")]
    public virtual Operation Operation { get; set; } = null!;
}
