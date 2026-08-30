using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("ServiceCode", Name = "UQ_Services_Code", IsUnique = true)]
public partial class Service
{
    [Key]
    public int ServiceId { get; set; }

    [StringLength(30)]
    public string ServiceCode { get; set; } = null!;

    [StringLength(150)]
    public string NameAr { get; set; } = null!;

    [StringLength(150)]
    public string? NameEn { get; set; }

    [StringLength(30)]
    public string Unit { get; set; } = null!;

    [Column(TypeName = "decimal(19, 4)")]
    public decimal DefaultSellingPrice { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal DefaultCost { get; set; }

    public int? TaxRateId { get; set; }

    public bool IsTaxable { get; set; }

    [StringLength(50)]
    public string? Gs1Code { get; set; }

    [StringLength(50)]
    public string? EgsCode { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    [InverseProperty("Service")]
    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    [InverseProperty("Service")]
    public virtual ICollection<CustomerPriceRule> CustomerPriceRules { get; set; } = new List<CustomerPriceRule>();

    [InverseProperty("Service")]
    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    [InverseProperty("Service")]
    public virtual ICollection<OperationRevenueItem> OperationRevenueItems { get; set; } = new List<OperationRevenueItem>();

    [InverseProperty("Service")]
    public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();

    [ForeignKey("TaxRateId")]
    [InverseProperty("Services")]
    public virtual TaxRate? TaxRate { get; set; }
}
