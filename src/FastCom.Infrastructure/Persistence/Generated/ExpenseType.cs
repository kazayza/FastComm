using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_ExpenseTypes_Code", IsUnique = true)]
public partial class ExpenseType
{
    [Key]
    public int ExpenseTypeId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(150)]
    public string NameAr { get; set; } = null!;

    [StringLength(150)]
    public string? NameEn { get; set; }

    public bool IsCustodyAllowed { get; set; }

    public bool IsOperationCost { get; set; }

    public bool IsTaxDeductible { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [InverseProperty("ExpenseType")]
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    [InverseProperty("ExpenseType")]
    public virtual ICollection<SupplierInvoiceItem> SupplierInvoiceItems { get; set; } = new List<SupplierInvoiceItem>();
}
