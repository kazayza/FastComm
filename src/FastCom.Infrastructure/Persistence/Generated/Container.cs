using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("ContainerNumber", Name = "UQ_Containers_Number", IsUnique = true)]
public partial class Container
{
    [Key]
    public long ContainerId { get; set; }

    [StringLength(20)]
    public string ContainerNumber { get; set; } = null!;

    public int ContainerTypeId { get; set; }

    [StringLength(20)]
    public string OwnerType { get; set; } = null!;

    [StringLength(150)]
    public string? OwnerName { get; set; }

    [Column(TypeName = "decimal(18, 3)")]
    public decimal? WeightKg { get; set; }

    [StringLength(30)]
    public string CurrentStatus { get; set; } = null!;

    [StringLength(500)]
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [Precision(0)]
    public DateTime? DeletedAt { get; set; }

    public int? DeletedBy { get; set; }

    [InverseProperty("Container")]
    public virtual ICollection<BookingContainerDetail> BookingContainerDetails { get; set; } = new List<BookingContainerDetail>();

    [InverseProperty("Container")]
    public virtual ICollection<ContainerMovement> ContainerMovements { get; set; } = new List<ContainerMovement>();

    [ForeignKey("ContainerTypeId")]
    [InverseProperty("Containers")]
    public virtual ContainerType ContainerType { get; set; } = null!;

    [InverseProperty("Container")]
    public virtual ICollection<OperationContainer> OperationContainers { get; set; } = new List<OperationContainer>();
}
