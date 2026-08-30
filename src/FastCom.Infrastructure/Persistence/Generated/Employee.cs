using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("EmployeeCode", Name = "UQ_Employees_Code", IsUnique = true)]
public partial class Employee
{
    [Key]
    public int EmployeeId { get; set; }

    [StringLength(30)]
    public string EmployeeCode { get; set; } = null!;

    [StringLength(200)]
    public string FullNameAr { get; set; } = null!;

    [StringLength(200)]
    public string? FullNameEn { get; set; }

    [StringLength(30)]
    public string? NationalId { get; set; }

    [StringLength(50)]
    public string? Mobile { get; set; }

    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    public DateOnly? HireDate { get; set; }

    public int? DepartmentId { get; set; }

    public int? JobTitleId { get; set; }

    public int? BranchId { get; set; }

    public int? ManagerEmployeeId { get; set; }

    [StringLength(30)]
    public string EmploymentStatus { get; set; } = null!;

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
    [InverseProperty("Employees")]
    public virtual Branch? Branch { get; set; }

    [ForeignKey("DepartmentId")]
    [InverseProperty("Employees")]
    public virtual Department? Department { get; set; }

    [InverseProperty("Employee")]
    public virtual ICollection<Driver> Drivers { get; set; } = new List<Driver>();

    [InverseProperty("ManagerEmployee")]
    public virtual ICollection<Employee> InverseManagerEmployee { get; set; } = new List<Employee>();

    [ForeignKey("JobTitleId")]
    [InverseProperty("Employees")]
    public virtual JobTitle? JobTitle { get; set; }

    [ForeignKey("ManagerEmployeeId")]
    [InverseProperty("InverseManagerEmployee")]
    public virtual Employee? ManagerEmployee { get; set; }
}
