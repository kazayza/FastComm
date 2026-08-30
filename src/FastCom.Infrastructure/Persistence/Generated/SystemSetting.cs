using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("SettingKey", Name = "UQ_SystemSettings_Key", IsUnique = true)]
public partial class SystemSetting
{
    [Key]
    public int SettingId { get; set; }

    [StringLength(100)]
    public string SettingKey { get; set; } = null!;

    public string? SettingValue { get; set; }

    [StringLength(20)]
    public string ValueType { get; set; } = null!;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsSystem { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }
}
