using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_Destinations_Code", IsUnique = true)]
public partial class Destination
{
    [Key]
    public int DestinationId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(150)]
    public string NameAr { get; set; } = null!;

    [StringLength(150)]
    public string? NameEn { get; set; }

    [StringLength(100)]
    public string? CityAr { get; set; }

    [StringLength(100)]
    public string? CityEn { get; set; }

    [Column(TypeName = "decimal(8, 1)")]
    public decimal? DistanceKm { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [InverseProperty("Destination")]
    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    [InverseProperty("Destination")]
    public virtual ICollection<CustomerPriceRule> CustomerPriceRules { get; set; } = new List<CustomerPriceRule>();

    [InverseProperty("Destination")]
    public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();
}
