namespace FastCom.Infrastructure.Persistence;

/// <summary>
/// 🔐 مصدر بيانات المستخدم الحالي — لأغراض الـ Audit.
/// </summary>
/// <remarks>
/// التنفيذ الحقيقي بيبقى في المشاريع اللي عندها وصول للـ HTTP context
/// (في FastCom.Server بنستخدم <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>).
/// قاعدة البيانات نفسها (DesignTime/Jobs) بتشتغل من غير هانتر.
/// </remarks>
public interface IUserContext
{
    /// <summary>معرّف المستخدم الحالي (من الـ JWT claims) — أو <c>null</c> خارج طلب.</summary>
    int? UserId { get; }

    /// <summary>عنوان IP الخاص بالطلب — أو <c>null</c>.</summary>
    string? IpAddress { get; }

    /// <summary>الـ User-Agent بتاع المتصفح/العميل — أو <c>null</c>.</summary>
    string? UserAgent { get; }
}