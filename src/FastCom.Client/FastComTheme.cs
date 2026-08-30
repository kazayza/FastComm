using MudBlazor;

namespace FastCom.Client;

/// <summary>
/// 🎨 الهوية البصرية لـ فاست كوم.
/// <para>
/// 🔵 الأزرق #0B56DF و🟠 البرتقالي #FF8015 مستخرجين بالقياس من logo.pdf (الهوية الرسمية).
/// الكحلي العميق #0A1B3B مشتق من الأزرق — للخلفيات الغامقة (Sidebar/AppBar).
/// </para>
/// </summary>
public static class FastComTheme
{
    // ── الألوان الأساسية (الهوية الرسمية — مستخرجة من logo.pdf) ───────────────
    public const string Blue       = "#0B56DF";  // Primary   — أزرق الهوية
    public const string BlueDark   = "#083CA8";
    public const string Deep       = "#0A1B3B";  // خلفيات غامقة (Sidebar/AppBar)
    public const string DeepDark   = "#060F24";
    public const string DeepLight  = "#16306B";
    public const string Orange     = "#FF8015";  // Secondary — برتقالي الهوية
    public const string OrangeDark = "#E06A00";

    // ── ألوان الحالة ──────────────────────────────────────────────────────────
    public const string Success   = "#1B9E5E";   // مكتمل / مدفوع
    public const string Warning   = "#E8A317";   // متأخر / جزئي
    public const string Error     = "#D64545";   // ملغي / متأخر جدًا
    public const string Info      = "#2E86AB";   // قيد التنفيذ

    // ── الخلفيات ──────────────────────────────────────────────────────────────
    public const string Surface   = "#F5F7FA";
    public const string Card      = "#FFFFFF";
    public const string TextMain  = "#1A2332";
    public const string TextMuted = "#6B7A8F";

    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary        = Blue,
            PrimaryDarken  = BlueDark,
            Secondary      = Orange,
            SecondaryDarken= OrangeDark,
            Success        = Success,
            Warning        = Warning,
            Error          = Error,
            Info           = Info,
            AppbarBackground = Deep,
            AppbarText     = Colors.Shades.White,
            DrawerBackground = Deep,
            DrawerText     = "#C9D6E4",
            DrawerIcon     = "#8FA6BF",
            Background     = Surface,
            Surface        = Card,
            TextPrimary    = TextMain,
            TextSecondary  = TextMuted,
            ActionDefault  = TextMuted,
            TableLines     = "#E3E9F0",
            Divider        = "#E3E9F0",
        },
        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily = new[] { "Cairo", "Segoe UI", "Tahoma", "sans-serif" },
                FontSize   = ".9rem",
                FontWeight = 400,
            },
            H4 = new H4
            {
                FontFamily = new[] { "Cairo", "Segoe UI", "Tahoma", "sans-serif" },
                FontSize   = "1.6rem",
                FontWeight = 700,
            },
            H5 = new H5
            {
                FontFamily = new[] { "Cairo", "Segoe UI", "Tahoma", "sans-serif" },
                FontSize   = "1.25rem",
                FontWeight = 700,
            },
            H6 = new H6
            {
                FontFamily = new[] { "Cairo", "Segoe UI", "Tahoma", "sans-serif" },
                FontSize   = "1.05rem",
                FontWeight = 600,
            },
            Body1 = new Body1
            {
                FontFamily = new[] { "Cairo", "Segoe UI", "Tahoma", "sans-serif" },
                FontSize   = ".9rem",
            },
            Button = new Button
            {
                FontFamily   = new[] { "Cairo", "Segoe UI", "Tahoma", "sans-serif" },
                FontSize     = ".875rem",
                FontWeight   = 600,
                TextTransform = "none",
            },
        },
        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft  = "270px",
            DrawerWidthRight = "270px",
            // ⚠️ AppBarHeight مش موجود في MudBlazor 7 LayoutProperties
            //    الـ AppBar بياخد ارتفاعه من الـ CSS (var(--mud-appbar-height))
        },
    };
}
