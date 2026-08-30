using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("UserId", "IsRead", "CreatedAt", Name = "IX_Notifications_User", IsDescending = new[] { false, false, true })]
public partial class Notification
{
    [Key]
    public long NotificationId { get; set; }

    public int? UserId { get; set; }

    public int? BranchId { get; set; }

    [StringLength(50)]
    public string NotificationType { get; set; } = null!;

    [StringLength(200)]
    public string Title { get; set; } = null!;

    [StringLength(1000)]
    public string Message { get; set; } = null!;

    [StringLength(50)]
    public string? EntityType { get; set; }

    public long? EntityId { get; set; }

    [StringLength(20)]
    public string Priority { get; set; } = null!;

    public bool IsRead { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? ReadAt { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("Notifications")]
    public virtual Branch? Branch { get; set; }
}
