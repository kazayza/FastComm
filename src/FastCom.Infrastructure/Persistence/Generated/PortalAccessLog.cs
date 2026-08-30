using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("CustomerId", "AccessedAt", Name = "IX_PortalAccessLogs_Customer", IsDescending = new[] { false, true })]
public partial class PortalAccessLog
{
    [Key]
    public long PortalAccessLogId { get; set; }

    public int CustomerId { get; set; }

    public long? PortalTokenId { get; set; }

    [Precision(0)]
    public DateTime AccessedAt { get; set; }

    [StringLength(64)]
    public string? IpAddress { get; set; }

    [StringLength(500)]
    public string? UserAgent { get; set; }

    [StringLength(50)]
    public string? EntityType { get; set; }

    public long? EntityId { get; set; }

    [StringLength(30)]
    public string Action { get; set; } = null!;

    [ForeignKey("CustomerId")]
    [InverseProperty("PortalAccessLogs")]
    public virtual Customer Customer { get; set; } = null!;

    [ForeignKey("PortalTokenId")]
    [InverseProperty("PortalAccessLogs")]
    public virtual CustomerPortalToken? PortalToken { get; set; }
}
