using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_JobTitles_Code", IsUnique = true)]
public partial class JobTitle
{
    [Key]
    public int JobTitleId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(100)]
    public string NameAr { get; set; } = null!;

    [StringLength(100)]
    public string? NameEn { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [InverseProperty("JobTitle")]
    public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
