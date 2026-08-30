using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

public partial class CustomerContact
{
    [Key]
    public int CustomerContactId { get; set; }

    public int CustomerId { get; set; }

    [StringLength(150)]
    public string ContactName { get; set; } = null!;

    [StringLength(100)]
    public string? JobTitle { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(50)]
    public string? Mobile { get; set; }

    [StringLength(254)]
    public string? Email { get; set; }

    public bool IsPrimary { get; set; }

    public bool CanReceiveInvoices { get; set; }

    public bool IsActive { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [InverseProperty("Contact")]
    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    [ForeignKey("CustomerId")]
    [InverseProperty("CustomerContacts")]
    public virtual Customer Customer { get; set; } = null!;
}
