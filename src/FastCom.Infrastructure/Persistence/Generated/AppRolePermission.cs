using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[PrimaryKey("RoleId", "PermissionId")]
public partial class AppRolePermission
{
    [Key]
    public int RoleId { get; set; }

    [Key]
    public int PermissionId { get; set; }

    [Precision(0)]
    public DateTime GrantedAt { get; set; }

    public int? GrantedBy { get; set; }

    [ForeignKey("PermissionId")]
    [InverseProperty("AppRolePermissions")]
    public virtual AppPermission Permission { get; set; } = null!;
}
