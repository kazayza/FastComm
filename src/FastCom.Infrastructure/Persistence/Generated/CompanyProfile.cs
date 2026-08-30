using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Table("CompanyProfile")]
public partial class CompanyProfile
{
    [Key]
    public int CompanyProfileId { get; set; }

    [StringLength(300)]
    public string LegalNameAr { get; set; } = null!;

    [StringLength(300)]
    public string? LegalNameEn { get; set; }

    [StringLength(200)]
    public string? TradeNameAr { get; set; }

    [StringLength(200)]
    public string? TradeNameEn { get; set; }

    [StringLength(50)]
    public string TaxNumber { get; set; } = null!;

    [StringLength(50)]
    public string? VatRegistrationNumber { get; set; }

    [StringLength(50)]
    public string? CommercialRegister { get; set; }

    [StringLength(500)]
    public string AddressAr { get; set; } = null!;

    [StringLength(500)]
    public string? AddressEn { get; set; }

    [StringLength(100)]
    public string? CityAr { get; set; }

    [StringLength(100)]
    public string? CityEn { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(50)]
    public string? Mobile { get; set; }

    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(254)]
    public string? Website { get; set; }

    [StringLength(500)]
    public string? LogoPath { get; set; }

    [StringLength(500)]
    public string? LogoDarkPath { get; set; }

    [StringLength(500)]
    public string? StampPath { get; set; }

    [StringLength(1000)]
    public string? InvoiceFooterNoteAr { get; set; }

    [StringLength(1000)]
    public string? InvoiceFooterNoteEn { get; set; }

    [StringLength(3)]
    [Unicode(false)]
    public string DefaultCurrencyCode { get; set; } = null!;

    public int? DefaultTaxRateId { get; set; }

    public byte FiscalYearStartMonth { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }
}
