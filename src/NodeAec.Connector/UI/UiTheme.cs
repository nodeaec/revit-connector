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
