using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

/* 🔴 أنواع الإيصالات اللي بتتدفع من خزينة الهيئة (جاري).
   جدول خفيف — المستخدم يزيد عليه من الشاشة ويختار منه وقت تسجيل المصروف. */
[Index("Code", Name = "UQ_ReceiptTypes_Code", IsUnique = true)]
public partial class ReceiptType
{
    [Key]
    public int ReceiptTypeId { get; set; }

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

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [InverseProperty("ReceiptType")]
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}
