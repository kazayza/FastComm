using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

public partial class CustomerPriceRule
{
    [Key]
    public long CustomerPriceRuleId { get; set; }

    public int CustomerId { get; set; }

    public int? PriceListId { get; set; }

    public int ServiceId { get; set; }

    public int? PortId { get; set; }

    public int? DestinationId { get; set; }

    public int? ContainerTypeId { get; set; }

    public int? TripTypeId { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal? CostPrice { get; set; }

    public int? TaxRateId { get; set; }

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public int Priority { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [ForeignKey("ContainerTypeId")]
    [InverseProperty("CustomerPriceRules")]
    public virtual ContainerType? ContainerType { get; set; }

    [ForeignKey("CustomerId")]
    [InverseProperty("CustomerPriceRules")]
    public virtual Customer Customer { get; set; } = null!;

    [ForeignKey("DestinationId")]
    [InverseProperty("CustomerPriceRules")]
    public virtual Destination? Destination { get; set; }

    [InverseProperty("PriceRule")]
    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    [InverseProperty("PriceRule")]
    public virtual ICollection<OperationRevenueItem> OperationRevenueItems { get; set; } = new List<OperationRevenueItem>();

    [ForeignKey("PortId")]
    [InverseProperty("CustomerPriceRules")]
    public virtual Port? Port { get; set; }

    [ForeignKey("PriceListId")]
    [InverseProperty("CustomerPriceRules")]
    public virtual PriceList? PriceList { get; set; }

    [ForeignKey("ServiceId")]
    [InverseProperty("CustomerPriceRules")]
    public virtual Service Service { get; set; } = null!;

    [ForeignKey("TaxRateId")]
    [InverseProperty("CustomerPriceRules")]
    public virtual TaxRate? TaxRate { get; set; }

    [ForeignKey("TripTypeId")]
    [InverseProperty("CustomerPriceRules")]
    public virtual TripType? TripType { get; set; }
}
