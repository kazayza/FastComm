using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("TripNumber", Name = "UQ_Trips_Number", IsUnique = true)]
public partial class Trip
{
    [Key]
    public long TripId { get; set; }

    [StringLength(40)]
    public string TripNumber { get; set; } = null!;

    public int BranchId { get; set; }

    public int DriverId { get; set; }

    public int VehicleId { get; set; }

    public int? TrailerId { get; set; }

    public int? TripTypeId { get; set; }

    [Precision(0)]
    public DateTime? PlannedStartAt { get; set; }

    [Precision(0)]
    public DateTime? ActualStartAt { get; set; }

    [Precision(0)]
    public DateTime? ActualEndAt { get; set; }

    [Column(TypeName = "decimal(18, 1)")]
    public decimal? StartOdometer { get; set; }

    [Column(TypeName = "decimal(18, 1)")]
    public decimal? EndOdometer { get; set; }

    [Column(TypeName = "decimal(18, 1)")]
    public decimal? TotalDistanceKm { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

    [StringLength(1000)]
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

    [ForeignKey("BranchId")]
    [InverseProperty("Trips")]
    public virtual Branch Branch { get; set; } = null!;

    [ForeignKey("DriverId")]
    [InverseProperty("Trips")]
    public virtual Driver Driver { get; set; } = null!;

    [InverseProperty("Trip")]
    public virtual ICollection<DriverCustody> DriverCustodies { get; set; } = new List<DriverCustody>();

    [InverseProperty("Trip")]
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    [InverseProperty("Trip")]
    public virtual ICollection<SupplierInvoiceItem> SupplierInvoiceItems { get; set; } = new List<SupplierInvoiceItem>();

    [ForeignKey("TrailerId")]
    [InverseProperty("Trips")]
    public virtual Trailer? Trailer { get; set; }

    [InverseProperty("Trip")]
    public virtual ICollection<TripCostAllocation> TripCostAllocations { get; set; } = new List<TripCostAllocation>();

    [InverseProperty("Trip")]
    public virtual ICollection<TripOperation> TripOperations { get; set; } = new List<TripOperation>();

    [ForeignKey("TripTypeId")]
    [InverseProperty("Trips")]
    public virtual TripType? TripType { get; set; }

    [ForeignKey("VehicleId")]
    [InverseProperty("Trips")]
    public virtual Vehicle Vehicle { get; set; } = null!;
}
