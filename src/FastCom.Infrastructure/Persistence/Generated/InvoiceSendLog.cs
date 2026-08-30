using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("InvoiceId", "SentAt", Name = "IX_InvoiceSendLogs_Invoice", IsDescending = new[] { false, true })]
public partial class InvoiceSendLog
{
    [Key]
    public long InvoiceSendLogId { get; set; }

    public long InvoiceId { get; set; }

    [Precision(0)]
    public DateTime SentAt { get; set; }

    public int? SentBy { get; set; }

    [StringLength(254)]
    public string Recipient { get; set; } = null!;

    [StringLength(20)]
    public string Channel { get; set; } = null!;

    [StringLength(20)]
    public string Status { get; set; } = null!;

    [StringLength(1000)]
    public string? ErrorMessage { get; set; }

    [StringLength(500)]
    public string? AttachmentPath { get; set; }

    [ForeignKey("InvoiceId")]
    [InverseProperty("InvoiceSendLogs")]
    public virtual Invoice Invoice { get; set; } = null!;
}
