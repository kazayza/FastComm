using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

/* 🔴 أسعار نولون النقله (أجرة السائق) — جدول اقتراح تلقائي.
   المطابقة: الأكثر تحديداً يفوز (عدد الأعمدة غير الفاضية) ثم Priority ثم ValidFrom.
   من غير navigations — الأعمدة FK بس (تجنب تصادمات EF المعروفة في المشروع). */
public partial class DriverFreightRule
{
    [Key]
    public int DriverFreightRuleId { get; set; }

    public int? PortId { get; set; }

    public int? DestinationId { get; set; }

    public int? TripTypeId { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal Amount { get; set; }

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public int Priority { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }
}
