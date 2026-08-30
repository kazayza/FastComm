using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("DriverCode", Name = "UQ_Drivers_Code", IsUnique = true)]
public partial class Driver
{
    [Key]
    public int DriverId { get; set; }

    [StringLength(30)]
    public string DriverCode { get; set; } = null!;

    [StringLength(20)]
    public string DriverType { get; set; } = null!;

    public int? EmployeeId { get; set; }

    public int? SupplierId { get; set; }

    [StringLength(200)]
    public string FullName { get; set; } = null!;

    [StringLength(30)]
    public string? NationalId { get; set; }

    [StringLength(50)]
    public string? Mobile { get; set; }

    [StringLength(50)]
    public string? LicenseNumber { get; set; }

    [StringLength(50)]
    public string? LicenseType { get; set; }

    public DateOnly? LicenseExpiryDate { get; set; }

    [StringLength(20)]
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

    [ForeignKey("EmployeeId")]
    [InverseProperty("Drivers")]
    public virtual Employee? Employee { get; set; }

    [InverseProperty("Driver")]
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    [ForeignKey("SupplierId")]
    [InverseProperty("Drivers")]
    public virtual Supplier? Supplier { get; set; }

    [InverseProperty("Driver")]
    public virtual ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
