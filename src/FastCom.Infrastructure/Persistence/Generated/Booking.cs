using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("BookingNumber", Name = "UQ_Bookings_Number", IsUnique = true)]
public partial class Booking
{
    [Key]
    public long BookingId { get; set; }

    [StringLength(40)]
    public string BookingNumber { get; set; } = null!;

    public int BranchId { get; set; }

    public int CustomerId { get; set; }

    [Precision(0)]
    public DateTime BookingDate { get; set; }

    public DateOnly? RequestedDate { get; set; }

    public int? ServiceId { get; set; }

    public int? PortId { get; set; }

    public int? DestinationId { get; set; }

    public int? TripTypeId { get; set; }

    [StringLength(100)]
    public string? CustomerReference { get; set; }

    public int? ContactId { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

    [StringLength(1000)]
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [Precision(0)]
    public DateTime? DeletedAt { get; set; }

    public int? DeletedBy { get; set; }

    [InverseProperty("Booking")]
    public virtual ICollection<BookingContainerLine> BookingContainerLines { get; set; } = new List<BookingContainerLine>();

    [ForeignKey("BranchId")]
    [InverseProperty("Bookings")]
    public virtual Branch Branch { get; set; } = null!;

    [ForeignKey("ContactId")]
    [InverseProperty("Bookings")]
    public virtual CustomerContact? Contact { get; set; }

    [ForeignKey("CustomerId")]
    [InverseProperty("Bookings")]
    public virtual Customer Customer { get; set; } = null!;

    [ForeignKey("DestinationId")]
    [InverseProperty("Bookings")]
    public virtual Destination? Destination { get; set; }

    [InverseProperty("Booking")]
    public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();

    [ForeignKey("PortId")]
    [InverseProperty("Bookings")]
    public virtual Port? Port { get; set; }

    [ForeignKey("ServiceId")]
    [InverseProperty("Bookings")]
    public virtual Service? Service { get; set; }

    [ForeignKey("TripTypeId")]
    [InverseProperty("Bookings")]
    public virtual TripType? TripType { get; set; }
}
