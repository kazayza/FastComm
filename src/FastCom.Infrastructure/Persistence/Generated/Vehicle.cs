using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("VehicleCode", Name = "UQ_Vehicles_Code", IsUnique = true)]
[Index("PlateNumber", Name = "UQ_Vehicles_Plate", IsUnique = true)]
public partial class Vehicle
{
    [Key]
    public int VehicleId { get; set; }

    [StringLength(30)]
    public string VehicleCode { get; set; } = null!;

    [StringLength(30)]
    public string PlateNumber { get; set; } = null!;

    [StringLength(50)]
    public string VehicleType { get; set; } = null!;

    [StringLength(100)]
    public string? Brand { get; set; }

    [StringLength(100)]
    public string? Model { get; set; }

    public short? ModelYear { get; set; }

    [StringLength(20)]
    public string OwnershipType { get; set; } = null!;

    public int? SupplierId { get; set; }

    [Column(TypeName = "decimal(10, 2)")]
    public decimal? CapacityTon { get; set; }

    public byte ContainerSlots20 { get; set; }

    public DateOnly? LicenseExpiryDate { get; set; }

    public DateOnly? InsuranceExpiryDate { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

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

    [ForeignKey("SupplierId")]
    [InverseProperty("Vehicles")]
    public virtual Supplier? Supplier { get; set; }

    [InverseProperty("Vehicle")]
    public virtual ICollection<Trip> Trips { get; set; } = new List<Trip>();

    [InverseProperty("Vehicle")]
    public virtual ICollection<VehicleMaintenance> VehicleMaintenances { get; set; } = new List<VehicleMaintenance>();
}
