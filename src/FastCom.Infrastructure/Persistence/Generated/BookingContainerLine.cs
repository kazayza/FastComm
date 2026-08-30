using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("BookingId", Name = "IX_BookingContainerLines_Booking")]
[Index("BookingId", "LineNo", Name = "UQ_BookingContainerLines", IsUnique = true)]
public partial class BookingContainerLine
{
    [Key]
    public long BookingContainerLineId { get; set; }

    public long BookingId { get; set; }

    public int LineNo { get; set; }

    public int ContainerTypeId { get; set; }

    public int RequestedQty { get; set; }

    public int AssignedQty { get; set; }

    [Column(TypeName = "decimal(18, 3)")]
    public decimal? WeightKg { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

    [StringLength(500)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [ForeignKey("BookingId")]
    [InverseProperty("BookingContainerLines")]
    public virtual Booking Booking { get; set; } = null!;

    [InverseProperty("BookingContainerLine")]
    public virtual ICollection<BookingContainerDetail> BookingContainerDetails { get; set; } = new List<BookingContainerDetail>();

    [ForeignKey("ContainerTypeId")]
    [InverseProperty("BookingContainerLines")]
    public virtual ContainerType ContainerType { get; set; } = null!;
}
