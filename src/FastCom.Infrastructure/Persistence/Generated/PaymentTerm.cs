using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_PaymentTerms_Code", IsUnique = true)]
public partial class PaymentTerm
{
    [Key]
    public int PaymentTermId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(100)]
    public string NameAr { get; set; } = null!;

    [StringLength(100)]
    public string? NameEn { get; set; }

    public int DueDays { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [InverseProperty("PaymentTerm")]
    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();

    [InverseProperty("PaymentTerm")]
    public virtual ICollection<Supplier> Suppliers { get; set; } = new List<Supplier>();
}
