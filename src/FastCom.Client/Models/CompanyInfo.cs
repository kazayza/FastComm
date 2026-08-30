namespace FastCom.Client.Models;

/// <summary>
/// 🔴 D11: بيانات الشركة بتيجي من جدول <c>CompanyProfile</c> — مش من SystemSettings.
/// </summary>
public class CompanyInfo
{
    public string LegalNameAr   { get; set; } = "فاست كوم";
    public string? TradeNameAr  { get; set; }
    public string? TradeNameEn  { get; set; }
    public string  TaxNumber    { get; set; } = "";
    public string  AddressAr    { get; set; } = "";
    public string? CityAr       { get; set; }
    public string? Phone        { get; set; }
    public string? Mobile       { get; set; }
    public string? Email        { get; set; }
    public string? LogoPath     { get; set; }

    /// <summary>الاسم اللي بيتعرض في الـ Header.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(TradeNameAr) ? LegalNameAr : TradeNameAr;

    // 🔍 للتشخيص — الـ API بيرجّعها لو فيه مشكلة
    public string? Error      { get; set; }
    public string? ErrorType  { get; set; }
    public string? ErrorInner { get; set; }

    /// <summary>فيه مشكلة في جلب البيانات؟</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
}
