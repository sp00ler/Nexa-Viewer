using Windows.UI;
using Windows.UI.ViewManagement;

namespace ViewerPrn.App;

/// <summary>
/// Converts between the stored 0xAARRGGBB accent value and <see cref="Color"/>.
/// </summary>
internal static class AccentColors
{
    public static Color SystemAccent() => new UISettings().GetColorValue(UIColorType.Accent);

    public static Color? ToColor(uint? argb) => argb is null
        ? null
        : Color.FromArgb(
            (byte)((argb.Value >> 24) & 0xFF),
            (byte)((argb.Value >> 16) & 0xFF),
            (byte)((argb.Value >> 8) & 0xFF),
            (byte)(argb.Value & 0xFF));

    public static uint ToArgb(Color color) =>
        ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
}
