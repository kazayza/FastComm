using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("ContainerId", "MovementDate", Name = "IX_ContainerMovements_Container", IsDescending = new[] { false, true })]
public partial class ContainerMovement
{
    [Key]
    public long ContainerMovementId { get; set; }

    public long ContainerId { get; set; }

    [StringLength(30)]
    public string MovementType { get; set; } = null!;

    public long? BookingId { get; set; }

    public long? OperationId { get; set; }

    public int? PortId { get; set; }

    [Precision(0)]
    public DateTime MovementDate { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("ContainerId")]
    [InverseProperty("ContainerMovements")]
    public virtual Container Container { get; set; } = null!;

    [ForeignKey("PortId")]
    [InverseProperty("ContainerMovements")]
    public virtual Port? Port { get; set; }
}
