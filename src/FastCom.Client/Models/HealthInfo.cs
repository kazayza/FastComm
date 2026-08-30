namespace FastCom.Client.Models;

/// <summary>نتيجة <c>GET /api/health</c> — بنستخدمها في صفحة الحالة.</summary>
public class HealthInfo
{
    public string? Status            { get; set; }
    public DateTime ServerTime       { get; set; }
    public DateTime CairoTime        { get; set; }
    public string? Database          { get; set; }
    public int TablesInDatabase      { get; set; }
    public int Permissions           { get; set; }
    public int Roles                 { get; set; }
    public int Views                 { get; set; }
    public int Triggers              { get; set; }
    public int Procedures            { get; set; }
    public string? Error             { get; set; }

    // 🔍 حقول تشخيص جديدة
    public string? ErrorType         { get; set; }
    public string? ErrorInner        { get; set; }
    public int     CompanyRows       { get; set; }
    public string? CompanyName       { get; set; }
    public string? CompanyRowsError  { get; set; }
    public string? CompanyNameError  { get; set; }
}
