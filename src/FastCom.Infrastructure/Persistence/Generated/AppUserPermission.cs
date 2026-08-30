using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[PrimaryKey("UserId", "PermissionId")]
public partial class AppUserPermission
{
    [Key]
    public int UserId { get; set; }

    [Key]
    public int PermissionId { get; set; }

    public bool IsGranted { get; set; }

    [Precision(0)]
    public DateTime GrantedAt { get; set; }

    public int? GrantedBy { get; set; }

    [StringLength(300)]
    public string? Notes { get; set; }

    [ForeignKey("PermissionId")]
    [InverseProperty("AppUserPermissions")]
    public virtual AppPermission Permission { get; set; } = null!;
}
