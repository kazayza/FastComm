using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("PermissionCode", Name = "UQ_AppPermissions_Code", IsUnique = true)]
public partial class AppPermission
{
    [Key]
    public int PermissionId { get; set; }

    [StringLength(100)]
    public string PermissionCode { get; set; } = null!;

    [StringLength(150)]
    public string NameAr { get; set; } = null!;

    [StringLength(150)]
    public string? NameEn { get; set; }

    [StringLength(50)]
    public string ModuleCode { get; set; } = null!;

    [StringLength(50)]
    public string ActionCode { get; set; } = null!;

    public int SortOrder { get; set; }

    [InverseProperty("Permission")]
    public virtual ICollection<AppRolePermission> AppRolePermissions { get; set; } = new List<AppRolePermission>();

    [InverseProperty("Permission")]
    public virtual ICollection<AppUserPermission> AppUserPermissions { get; set; } = new List<AppUserPermission>();
}
