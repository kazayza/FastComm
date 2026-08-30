using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_TripTypes_Code", IsUnique = true)]
public partial class TripType
{
    [Key]
    public int TripTypeId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(100)]
    public string NameAr { get; set; } = null!;

    [StringLength(100)]
    public string? NameEn { get; set; }

    public int? DefaultTaxRateId { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [InverseProperty("TripType")]
    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    [InverseProperty("TripType")]
    public virtual ICollection<CustomerPriceRule> CustomerPriceRules { get; set; } = new List<CustomerPriceRule>();

    [ForeignKey("DefaultTaxRateId")]
    [InverseProperty("TripTypes")]
    public virtual TaxRate? DefaultTaxRate { get; set; }

    [InverseProperty("TripType")]
    public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();

    [InverseProperty("TripType")]
    public virtual ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
