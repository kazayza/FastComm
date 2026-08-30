namespace FastCom.Domain.Entities;

/// <summary>
/// أي Entity لها حقول تدقيق (Audit Fields).
/// <para>
/// الـ Entities نفسها بتتولّد من قاعدة البيانات بأمر
/// <c>dotnet ef dbcontext scaffold</c> — الـ Interface ده بيتنفّذ عليها
/// يدويًا لما نحتاج نتعامل معاها بشكل عام (مثلاً في <c>ApplyAuditFields</c>).
/// </para>
/// </summary>
public interface IAuditableEntity
{
    /// <summary>تاريخ الإنشاء (UTC).</summary>
    DateTime CreatedAt { get; set; }

    /// <summary>معرّف المستخدم اللي أنشأ السجل.</summary>
    int? CreatedBy { get; set; }

    /// <summary>تاريخ آخر تعديل (UTC).</summary>
    DateTime? UpdatedAt { get; set; }

    /// <summary>معرّف المستخدم اللي عدّل السجل.</summary>
    int? UpdatedBy { get; set; }
}

/// <summary>
/// أي Entity بتدعم الحذف الناعم (Soft Delete).
/// <para>
/// الـ Global Query Filter في
/// <c>SoftDeleteQueryFilter.ApplySoftDeleteFilters()</c>
/// بيتجاهل الصفوف اللي <c>IsDeleted = true</c> تلقائيًا في أي استعلام.
/// </para>
/// </summary>
public interface ISoftDeletable
{
    /// <summary>هل السجل محذوف منطقيًا؟</summary>
    bool IsDeleted { get; set; }

    /// <summary>تاريخ الحذف (UTC).</summary>
    DateTime? DeletedAt { get; set; }

    /// <summary>معرّف المستخدم اللي حذف السجل.</summary>
    int? DeletedBy { get; set; }
}
