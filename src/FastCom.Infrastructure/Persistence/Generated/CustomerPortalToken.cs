using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("TokenHash", Name = "UQ_PortalTokens_Hash", IsUnique = true)]
public partial class CustomerPortalToken
{
    [Key]
    public long PortalTokenId { get; set; }

    public int CustomerId { get; set; }

    [StringLength(128)]
    public string TokenHash { get; set; } = null!;

    [StringLength(20)]
    public string Purpose { get; set; } = null!;

    public long? InvoiceId { get; set; }

    [Precision(0)]
    public DateTime? ExpiresAt { get; set; }

    public int? MaxUses { get; set; }

    public int UsedCount { get; set; }

    public bool IsActive { get; set; }

    [Precision(0)]
    public DateTime? LastUsedAt { get; set; }

    [Precision(0)]
    public DateTime? RevokedAt { get; set; }

    public int? RevokedBy { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("CustomerId")]
    [InverseProperty("CustomerPortalTokens")]
    public virtual Customer Customer { get; set; } = null!;

    [ForeignKey("InvoiceId")]
    [InverseProperty("CustomerPortalTokens")]
    public virtual Invoice? Invoice { get; set; }

    [InverseProperty("PortalToken")]
    public virtual ICollection<PortalAccessLog> PortalAccessLogs { get; set; } = new List<PortalAccessLog>();
}
