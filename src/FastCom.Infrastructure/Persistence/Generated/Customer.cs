using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("CustomerCode", Name = "UQ_Customers_Code", IsUnique = true)]
public partial class Customer
{
    [Key]
    public int CustomerId { get; set; }

    [StringLength(30)]
    public string CustomerCode { get; set; } = null!;

    [StringLength(20)]
    public string CustomerType { get; set; } = null!;

    [StringLength(200)]
    public string NameAr { get; set; } = null!;

    [StringLength(200)]
    public string? NameEn { get; set; }

    [StringLength(50)]
    public string? TaxNumber { get; set; }

    [StringLength(30)]
    public string? NationalId { get; set; }

    [StringLength(50)]
    public string? CommercialRegister { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? AddressAr { get; set; }

    [StringLength(500)]
    public string? AddressEn { get; set; }

    [StringLength(100)]
    public string? CityAr { get; set; }

    [StringLength(100)]
    public string? CityEn { get; set; }

    public int? PaymentTermId { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal CreditLimit { get; set; }

    public bool PortalEnabled { get; set; }

    [StringLength(2)]
    [Unicode(false)]
    public string PreferredLanguage { get; set; } = null!;

    public bool IsActive { get; set; }

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

    [InverseProperty("Customer")]
    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    [InverseProperty("Customer")]
    public virtual ICollection<CashTransaction> CashTransactions { get; set; } = new List<CashTransaction>();

    [InverseProperty("Customer")]
    public virtual ICollection<CustomerContact> CustomerContacts { get; set; } = new List<CustomerContact>();

    [InverseProperty("Customer")]
    public virtual ICollection<CustomerPortalToken> CustomerPortalTokens { get; set; } = new List<CustomerPortalToken>();

    [InverseProperty("Customer")]
    public virtual ICollection<CustomerPriceRule> CustomerPriceRules { get; set; } = new List<CustomerPriceRule>();

    [InverseProperty("Customer")]
    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    [InverseProperty("Customer")]
    public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();

    [ForeignKey("PaymentTermId")]
    [InverseProperty("Customers")]
    public virtual PaymentTerm? PaymentTerm { get; set; }

    [InverseProperty("Customer")]
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    [InverseProperty("Customer")]
    public virtual ICollection<PortalAccessLog> PortalAccessLogs { get; set; } = new List<PortalAccessLog>();
}
