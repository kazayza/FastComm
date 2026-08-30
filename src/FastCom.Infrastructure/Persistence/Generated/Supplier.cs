using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("SupplierCode", Name = "UQ_Suppliers_Code", IsUnique = true)]
public partial class Supplier
{
    [Key]
    public int SupplierId { get; set; }

    [StringLength(30)]
    public string SupplierCode { get; set; } = null!;

    [StringLength(30)]
    public string SupplierType { get; set; } = null!;

    [StringLength(200)]
    public string NameAr { get; set; } = null!;

    [StringLength(200)]
    public string? NameEn { get; set; }

    [StringLength(50)]
    public string? TaxNumber { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    public int? PaymentTermId { get; set; }

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

    [InverseProperty("Supplier")]
    public virtual ICollection<CashTransaction> CashTransactions { get; set; } = new List<CashTransaction>();

    [InverseProperty("Supplier")]
    public virtual ICollection<Driver> Drivers { get; set; } = new List<Driver>();

    [InverseProperty("Supplier")]
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    [ForeignKey("PaymentTermId")]
    [InverseProperty("Suppliers")]
    public virtual PaymentTerm? PaymentTerm { get; set; }

    [InverseProperty("Supplier")]
    public virtual ICollection<SupplierInvoice> SupplierInvoices { get; set; } = new List<SupplierInvoice>();

    [InverseProperty("Supplier")]
    public virtual ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();

    [InverseProperty("Supplier")]
    public virtual ICollection<Trailer> Trailers { get; set; } = new List<Trailer>();

    [InverseProperty("Supplier")]
    public virtual ICollection<VehicleMaintenance> VehicleMaintenances { get; set; } = new List<VehicleMaintenance>();

    [InverseProperty("Supplier")]
    public virtual ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
}
