namespace FastCom.Client.Models;

/// <summary>نتيجة بحث واحدة من <c>GET /api/search?q=...</c>.</summary>
public class SearchResult
{
    public string Entity   { get; set; } = "";
    public long   Id       { get; set; }
    public string Code     { get; set; } = "";
    public string Title    { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Badge    { get; set; } = "";

    /// <summary>النص اللي بيظهر في قائمة الـ Autocomplete.</summary>
    public string Display =>
        string.IsNullOrWhiteSpace(Code) ? Title : $"{Code} — {Title}";

    /// <summary>
    /// ⚠️ لازم <c>ToString()</c> — لأن <c>MudAutocomplete</c> بيعرض
    /// ناتج <c>ToString()</c> في القائمة وفي الـ input بعد الاختيار.
    /// </summary>
    public override string ToString() => Display;
}

/// <summary>استجابة <c>GET /api/search</c>.</summary>
public class SearchResponse
{
    public string Query { get; set; } = "";
    public int Count { get; set; }
    public List<SearchResult> Results { get; set; } = new();

    /// <summary>🔐 الوحدات اللي المستخدم يقدر يبحث فيها.</summary>
    public List<string> SearchableIn { get; set; } = new();

    /// <summary>🔐 الوحدات اللي ماعندوش صلاحيتها.</summary>
    public List<string> DeniedEntities { get; set; } = new();

    public string? Message { get; set; }

    public string? Error     { get; set; }
    public string? ErrorType { get; set; }
}
