using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

/* 🔴 #88 — طلب تمويل عهدة:
   الموظف بيطلب مبلغ زيادة على عهدته، ومدير النظام بيوافق أو يرفض.
   الطلب **صف في جدول** مش إشعار — عشان يبقى فيه طابور منتظر، ومبلغ، ومين وافق،
   وقفل ضد الموافقة مرتين (`TopUpTransactionId` unique).
   الإشعار هو الجرس بس. */
[Index("CustodyId", "Status", Name = "IX_CustodyFundRequests_Custody")]
public partial class CustodyFundRequest
{
    [Key]
    public long FundRequestId { get; set; }

    public long CustodyId { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal RequestedAmount { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }

    /* Pending | Approved | Rejected | Cancelled */
    [StringLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>المبلغ اللي اتوافق عليه فعلًا — ممكن أقل من المطلوب (موافقة جزئية).</summary>
    [Column(TypeName = "decimal(19, 4)")]
    public decimal? ApprovedAmount { get; set; }

    public int RequestedBy { get; set; }

    [Precision(0)]
    public DateTime RequestedAt { get; set; }

    public int? DecidedBy { get; set; }

    [Precision(0)]
    public DateTime? DecidedAt { get; set; }

    [StringLength(500)]
    public string? DecisionNotes { get; set; }

    /// <summary>
    /// حركة «تغذية العهدة» الناتجة عن الموافقة.
    /// <para>UNIQUE (مفهرس بفلتر <c>IS NOT NULL</c>) ⇒ <b>موافقة واحدة بس على الطلب</b> —
    /// مستحيل يتحوّل للموظف مرتين.</para>
    /// </summary>
    public long? TopUpTransactionId { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    [ForeignKey("CustodyId")]
    [InverseProperty("FundRequests")]
    public virtual DriverCustody Custody { get; set; } = null!;

    [ForeignKey("TopUpTransactionId")]
    public virtual CustodyTransaction? TopUpTransaction { get; set; }
}
