using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("SupplierInvoiceNumber", Name = "UQ_SupplierInvoices_Number", IsUnique = true)]
public partial class SupplierInvoice
{
    [Key]
    public long SupplierInvoiceId { get; set; }

    [StringLength(40)]
    public string SupplierInvoiceNumber { get; set; } = null!;

    [StringLength(100)]
    public string? SupplierRefNumber { get; set; }

    public int BranchId { get; set; }

    public int SupplierId { get; set; }

    public DateOnly InvoiceDate { get; set; }

    public DateOnly? DueDate { get; set; }

    [StringLength(3)]
    [Unicode(false)]
    public string CurrencyCode { get; set; } = null!;

    [Column(TypeName = "decimal(19, 4)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal TaxTotal { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal GrandTotal { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal PaidAmount { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

    [StringLength(20)]
    public string PaymentStatus { get; set; } = null!;

    [StringLength(1000)]
    public string? Notes { get; set; }

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

    [Precision(0)]
    public DateTime? ApprovedAt { get; set; }

    public int? ApprovedBy { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("SupplierInvoices")]
    public virtual Branch Branch { get; set; } = null!;

    [ForeignKey("SupplierId")]
    [InverseProperty("SupplierInvoices")]
    public virtual Supplier Supplier { get; set; } = null!;

    [InverseProperty("SupplierInvoice")]
    public virtual ICollection<SupplierInvoiceAllocation> SupplierInvoiceAllocations { get; set; } = new List<SupplierInvoiceAllocation>();

    [InverseProperty("SupplierInvoice")]
    public virtual ICollection<SupplierInvoiceItem> SupplierInvoiceItems { get; set; } = new List<SupplierInvoiceItem>();
}
