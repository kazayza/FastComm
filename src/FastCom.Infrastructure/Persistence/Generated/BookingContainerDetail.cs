using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("ContainerId", Name = "IX_BookingContainerDetails_Container")]
[Index("BookingContainerLineId", Name = "IX_BookingContainerDetails_Line")]
public partial class BookingContainerDetail
{
    [Key]
    public long BookingContainerDetailId { get; set; }

    public long BookingContainerLineId { get; set; }

    public long? ContainerId { get; set; }

    [StringLength(20)]
    public string? ContainerNumberText { get; set; }

    [StringLength(50)]
    public string? SealNumber { get; set; }

    [StringLength(150)]
    public string? ShippingLine { get; set; }

    [Column("BLNumber")]
    [StringLength(100)]
    public string? Blnumber { get; set; }

    [StringLength(100)]
    public string? BookingReference { get; set; }

    [Column(TypeName = "decimal(18, 3)")]
    public decimal? WeightKg { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

    [StringLength(500)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [ForeignKey("BookingContainerLineId")]
    [InverseProperty("BookingContainerDetails")]
    public virtual BookingContainerLine BookingContainerLine { get; set; } = null!;

    [ForeignKey("ContainerId")]
    [InverseProperty("BookingContainerDetails")]
    public virtual Container? Container { get; set; }

    [InverseProperty("BookingContainerDetail")]
    public virtual ICollection<OperationContainer> OperationContainers { get; set; } = new List<OperationContainer>();
}
