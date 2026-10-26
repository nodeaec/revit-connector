using System.Windows;
using System.Windows.Media;

namespace NodeAec.Connector.UI;

/// <summary>
/// Node.aec web official palette (light-mode tokens) applied to Connector windows and buttons:
/// background #E7E6E6, text #232323, primary #1E4E79, and accent #A79D12.
/// Centralizes colors and icons to keep a consistent visual identity.
/// </summary>
public static class UiTheme
{
    /// <summary>Window background (#E7E6E6, light-background token).</summary>
    public static readonly Color Background = Color.FromRgb(0xE7, 0xE6, 0xE6);

    /// <summary>Card background (white, for contrast against the background).</summary>
    public static readonly Color Card = Color.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>Borders and dividers (neutral gray).</summary>
    public static readonly Color Border = Color.FromRgb(0xCF, 0xCF, 0xCF);

    /// <summary>Primary text (#232323).</summary>
    public static readonly Color Text = Color.FromRgb(0x23, 0x23, 0x23);

    /// <summary>Secondary text (hints and descriptions, neutral gray).</summary>
    public static readonly Color TextSecondary = Color.FromRgb(0x5F, 0x5F, 0x5F);

    /// <summary>Brand primary (#1E4E79): titles, primary actions, and links.</summary>
    public static readonly Color Primary = Color.FromRgb(0x1E, 0x4E, 0x79);

    /// <summary>Brand accent (#A79D12): warnings, badges, and details.</summary>
    public static readonly Color Accent = Color.FromRgb(0xA7, 0x9D, 0x12);

    /// <summary>Neutral background for secondary buttons.</summary>
    public static readonly Color SoftBackground = Color.FromRgb(0xDA, 0xD8, 0xD8);

    /// <summary>Connector window background: a lighter neutral than <see cref="Background"/>, so white panels read as one calm surface.</summary>
    public static readonly Color Canvas = Color.FromRgb(0xF3, 0xF3, 0xF2);

    /// <summary>Thin dividers between plugin rows.</summary>
    public static readonly Color Divider = Color.FromRgb(0xE6, 0xE6, 0xE4);

    /// <summary>Success text and dot (licenses OK, plugin liberado).</summary>
    public static readonly Color Good = Color.FromRgb(0x2F, 0x7D, 0x4A);

    /// <summary>Success fill behind the license status strip and OK labels.</summary>
    public static readonly Color GoodSoft = Color.FromRgb(0xE8, 0xF4, 0xEC);

    /// <summary>Success border of the license status strip.</summary>
    public static readonly Color GoodBorder = Color.FromRgb(0xA9, 0xD3, 0xB6);

    /// <summary>Warning text and dot (licenses out of date, plugin expiring or blocked).</summary>
    public static readonly Color Warning = Color.FromRgb(0x9A, 0x55, 0x0E);

    /// <summary>Warning fill behind the status strip and warning labels.</summary>
    public static readonly Color WarningSoft = Color.FromRgb(0xFB, 0xF1, 0xE0);

    /// <summary>Warning border of the license status strip.</summary>
    public static readonly Color WarningBorder = Color.FromRgb(0xE6, 0xC5, 0x8C);

    /// <summary>Blocked plugin label text (expired, cancelled, suspended).</summary>
    public static readonly Color Danger = Color.FromRgb(0xA8, 0x2E, 0x26);

    /// <summary>Blocked plugin label fill.</summary>
    public static readonly Color DangerSoft = Color.FromRgb(0xFB, 0xEA, 0xE8);

    /// <summary>Fill of the "new version" banner (a pale tint of <see cref="Primary"/>).</summary>
    public static readonly Color InfoSoft = Color.FromRgb(0xE7, 0xEF, 0xF7);

    /// <summary>Border of the "new version" banner.</summary>
    public static readonly Color InfoBorder = Color.FromRgb(0x9D, 0xB9, 0xD5);

    /// <summary>
    /// Creates a frozen brush (thread-safe, no resource leaks).
    /// </summary>
    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
