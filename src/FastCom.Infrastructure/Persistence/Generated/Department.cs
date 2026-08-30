using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_Departments_Code", IsUnique = true)]
public partial class Department
{
    [Key]
    public int DepartmentId { get; set; }

    [StringLength(20)]
    public string Code { get; set; } = null!;

    [StringLength(150)]
    public string NameAr { get; set; } = null!;

    [StringLength(150)]
    public string? NameEn { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [InverseProperty("Department")]
    public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
