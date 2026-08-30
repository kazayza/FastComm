using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_ContainerTypes_Code", IsUnique = true)]
public partial class ContainerType
{
    [Key]
    public int ContainerTypeId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(100)]
    public string NameAr { get; set; } = null!;

    [StringLength(100)]
    public string? NameEn { get; set; }

    public byte SizeFeet { get; set; }

    public bool IsReefer { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [InverseProperty("ContainerType")]
    public virtual ICollection<BookingContainerLine> BookingContainerLines { get; set; } = new List<BookingContainerLine>();

    [InverseProperty("ContainerType")]
    public virtual ICollection<Container> Containers { get; set; } = new List<Container>();

    [InverseProperty("ContainerType")]
    public virtual ICollection<CustomerPriceRule> CustomerPriceRules { get; set; } = new List<CustomerPriceRule>();
}
