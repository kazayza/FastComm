using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("PriceListCode", Name = "UQ_PriceLists_Code", IsUnique = true)]
public partial class PriceList
{
    [Key]
    public int PriceListId { get; set; }

    [StringLength(30)]
    public string PriceListCode { get; set; } = null!;

    [StringLength(150)]
    public string NameAr { get; set; } = null!;

    [StringLength(150)]
    public string? NameEn { get; set; }

    [StringLength(3)]
    [Unicode(false)]
    public string CurrencyCode { get; set; } = null!;

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [InverseProperty("PriceList")]
    public virtual ICollection<CustomerPriceRule> CustomerPriceRules { get; set; } = new List<CustomerPriceRule>();
}
