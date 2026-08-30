using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("PortCode", Name = "UQ_Ports_Code", IsUnique = true)]
public partial class Port
{
    [Key]
    public int PortId { get; set; }

    [StringLength(20)]
    public string PortCode { get; set; } = null!;

    [StringLength(150)]
    public string NameAr { get; set; } = null!;

    [StringLength(150)]
    public string? NameEn { get; set; }

    [StringLength(100)]
    public string? CityAr { get; set; }

    [StringLength(100)]
    public string? CityEn { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    [InverseProperty("Port")]
    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    [InverseProperty("Port")]
    public virtual ICollection<ContainerMovement> ContainerMovements { get; set; } = new List<ContainerMovement>();

    [InverseProperty("Port")]
    public virtual ICollection<CustomerPriceRule> CustomerPriceRules { get; set; } = new List<CustomerPriceRule>();

    [InverseProperty("Port")]
    public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();
}
