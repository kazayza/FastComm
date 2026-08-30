using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("EntityType", "EntityId", "CreatedAt", Name = "IX_AuditLogs_Entity", IsDescending = new[] { false, false, true })]
[Index("UserId", "CreatedAt", Name = "IX_AuditLogs_User", IsDescending = new[] { false, true })]
public partial class AuditLog
{
    [Key]
    public long AuditLogId { get; set; }

    public int? UserId { get; set; }

    [StringLength(30)]
    public string Action { get; set; } = null!;

    [StringLength(100)]
    public string EntityType { get; set; } = null!;

    [StringLength(100)]
    public string? EntityId { get; set; }

    public string? OldValues { get; set; }

    public string? NewValues { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(64)]
    public string? IpAddress { get; set; }

    [StringLength(500)]
    public string? UserAgent { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }
}
