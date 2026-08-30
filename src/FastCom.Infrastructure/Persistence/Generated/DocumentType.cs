using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

[Index("Code", Name = "UQ_DocumentTypes_Code", IsUnique = true)]
public partial class DocumentType
{
    [Key]
    public int DocumentTypeId { get; set; }

    [StringLength(30)]
    public string Code { get; set; } = null!;

    [StringLength(150)]
    public string NameAr { get; set; } = null!;

    [StringLength(150)]
    public string? NameEn { get; set; }

    public bool HasExpiry { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    [InverseProperty("DocumentType")]
    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
