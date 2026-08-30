using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_OperationStatuses_Code", IsUnique = true)]
public partial class OperationStatus
{
    [Key]
    public int StatusId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(100)]
    public string NameAr { get; set; } = null!;

    [StringLength(100)]
    public string? NameEn { get; set; }

    public int SortOrder { get; set; }

    public bool IsTerminal { get; set; }

    [StringLength(7)]
    public string? ColorHex { get; set; }

    public bool IsActive { get; set; }
}
