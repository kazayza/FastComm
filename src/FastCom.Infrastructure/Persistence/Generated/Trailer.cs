using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("TrailerCode", Name = "UQ_Trailers_Code", IsUnique = true)]
[Index("PlateNumber", Name = "UQ_Trailers_Plate", IsUnique = true)]
public partial class Trailer
{
    [Key]
    public int TrailerId { get; set; }

    [StringLength(30)]
    public string TrailerCode { get; set; } = null!;

    [StringLength(30)]
    public string? PlateNumber { get; set; }

    [StringLength(50)]
    public string TrailerType { get; set; } = null!;

    public byte? SizeFeet { get; set; }

    [StringLength(20)]
    public string OwnershipType { get; set; } = null!;

    public int? SupplierId { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

    public DateOnly? LicenseExpiryDate { get; set; }

    public DateOnly? InsuranceExpiryDate { get; set; }

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
    [InverseProperty("Trailers")]
    public virtual Supplier? Supplier { get; set; }

    [InverseProperty("Trailer")]
    public virtual ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
