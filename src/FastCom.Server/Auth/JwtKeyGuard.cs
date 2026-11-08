namespace FastCom.Server.Auth;

/// <summary>
/// حارس مفتاح توقيع JWT: يرفض الإقلاع أو إصدار التوكنات إذا كان المفتاح
/// هو النص البديل المنشور في المستودع أو أقصر من الحد الأدنى.
/// القيم الحقيقية تصل عبر أسرار المستخدم أو متغيرات البيئة — وملف
/// الإعدادات المتتبع لا يحمل إلا نصًا بديلًا معلنًا لا يصلح للتوقيع.
/// </summary>
public static class JwtKeyGuard
{
    public static string RequireValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "مفتاح JWT غير معرّف — عيّن Jwt:Key عبر أسرار المستخدم أو متغيرات البيئة");

        if (key.Contains("SET_VIA", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "مفتاح JWT ما زال النص البديل المنشور في ملف الإعدادات — عيّن مفتاحًا حقيقيًا عبر أسرار المستخدم أو متغيرات البيئة قبل التشغيل");

        if (key.Length < 64)
            throw new InvalidOperationException(
                $"مفتاح JWT يجب ألا يقل عن 64 حرفًا — الحالي {key.Length} حرف");

        return key;
    }
}
