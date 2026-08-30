using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("InvoiceNumber", Name = "UQ_Invoices_Number", IsUnique = true)]
public partial class Invoice
{
    [Key]
    public long InvoiceId { get; set; }

    [StringLength(40)]
    public string InvoiceNumber { get; set; } = null!;

    public int BranchId { get; set; }

    public int CustomerId { get; set; }

    public DateOnly InvoiceDate { get; set; }

    public DateOnly? DueDate { get; set; }

    [StringLength(20)]
    public string InvoiceType { get; set; } = null!;

    [StringLength(20)]
    public string DocumentTypeCode { get; set; } = null!;

    public long? OriginalInvoiceId { get; set; }

    [StringLength(20)]
    public string? ReasonCode { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = null!;

    [StringLength(30)]
    public string PaymentStatus { get; set; } = null!;

    [StringLength(3)]
    [Unicode(false)]
    public string CurrencyCode { get; set; } = null!;

    [Column(TypeName = "decimal(19, 4)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal DiscountTotal { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal TaxTotal { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal GrandTotal { get; set; }

    [Column(TypeName = "decimal(19, 4)")]
    public decimal PaidAmount { get; set; }

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
    public DateTime? IssuedAt { get; set; }

    public int? IssuedBy { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("Invoices")]
    public virtual Branch Branch { get; set; } = null!;

    [InverseProperty("Invoice")]
    public virtual ICollection<CashTransaction> CashTransactions { get; set; } = new List<CashTransaction>();

    [ForeignKey("CustomerId")]
    [InverseProperty("Invoices")]
    public virtual Customer Customer { get; set; } = null!;

    [InverseProperty("Invoice")]
    public virtual ICollection<CustomerPortalToken> CustomerPortalTokens { get; set; } = new List<CustomerPortalToken>();

    [InverseProperty("OriginalInvoice")]
    public virtual ICollection<Invoice> InverseOriginalInvoice { get; set; } = new List<Invoice>();

    [InverseProperty("Invoice")]
    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    [InverseProperty("Invoice")]
    public virtual ICollection<InvoiceOperation> InvoiceOperations { get; set; } = new List<InvoiceOperation>();

    [InverseProperty("Invoice")]
    public virtual ICollection<InvoiceSendLog> InvoiceSendLogs { get; set; } = new List<InvoiceSendLog>();

    [ForeignKey("OriginalInvoiceId")]
    [InverseProperty("InverseOriginalInvoice")]
    public virtual Invoice? OriginalInvoice { get; set; }

    [InverseProperty("Invoice")]
    public virtual ICollection<PaymentAllocation> PaymentAllocations { get; set; } = new List<PaymentAllocation>();
}
