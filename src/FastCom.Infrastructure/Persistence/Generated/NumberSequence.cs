using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

public partial class NumberSequence
{
    [Key]
    public int NumberSequenceId { get; set; }

    public int? BranchId { get; set; }

    [StringLength(30)]
    public string DocumentType { get; set; } = null!;

    [StringLength(20)]
    public string Prefix { get; set; } = null!;

    public short Year { get; set; }

    public long CurrentNumber { get; set; }

    public byte NumberLength { get; set; }

    [StringLength(20)]
    public string ResetPeriod { get; set; } = null!;

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("NumberSequences")]
    public virtual Branch? Branch { get; set; }
}
