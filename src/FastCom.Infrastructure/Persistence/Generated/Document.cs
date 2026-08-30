using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Domain.Entities;

public partial class Document
{
    [Key]
    public long DocumentId { get; set; }

    public int? BranchId { get; set; }

    public int DocumentTypeId { get; set; }

    [StringLength(50)]
    public string EntityType { get; set; } = null!;

    public long EntityId { get; set; }

    [StringLength(255)]
    public string FileName { get; set; } = null!;

    [StringLength(255)]
    public string? OriginalFileName { get; set; }

    [StringLength(30)]
    public string StorageProvider { get; set; } = null!;

    [StringLength(1000)]
    public string StoragePath { get; set; } = null!;

    [StringLength(150)]
    public string? ContentType { get; set; }

    public long? FileSizeBytes { get; set; }

    [StringLength(128)]
    public string? FileHash { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Precision(0)]
    public DateTime UploadedAt { get; set; }

    public int? UploadedBy { get; set; }

    public bool IsDeleted { get; set; }

    [Precision(0)]
    public DateTime? DeletedAt { get; set; }

    public int? DeletedBy { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("Documents")]
    public virtual Branch? Branch { get; set; }

    [ForeignKey("DocumentTypeId")]
    [InverseProperty("Documents")]
    public virtual DocumentType DocumentType { get; set; } = null!;
}
