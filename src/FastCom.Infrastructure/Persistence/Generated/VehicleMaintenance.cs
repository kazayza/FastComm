using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Table("VehicleMaintenance")]
[Index("MaintenanceNumber", Name = "UQ_VehicleMaintenance_Number", IsUnique = true)]
public partial class VehicleMaintenance
{
    [Key]
    public long MaintenanceId { get; set; }

    [StringLength(40)]
    public string MaintenanceNumber { get; set; } = null!;

    public int VehicleId { get; set; }

    public int BranchId { get; set; }

    public DateOnly MaintenanceDate { get; set; }

    [StringLength(50)]
    public string MaintenanceType { get; set; } = null!;

    public int? SupplierId { get; set; }

    [Column(TypeName = "decimal(18, 1)")]
    public decimal? Odometer { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal Cost { get; set; }

    public DateOnly? NextDueDate { get; set; }

    [Column(TypeName = "decimal(18, 1)")]
    public decimal? NextDueOdometer { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(20)]
    public string Status { get; set; } = null!;

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("VehicleMaintenances")]
    public virtual Branch Branch { get; set; } = null!;

    [ForeignKey("SupplierId")]
    [InverseProperty("VehicleMaintenances")]
    public virtual Supplier? Supplier { get; set; }

    [ForeignKey("VehicleId")]
    [InverseProperty("VehicleMaintenances")]
    public virtual Vehicle Vehicle { get; set; } = null!;
}
