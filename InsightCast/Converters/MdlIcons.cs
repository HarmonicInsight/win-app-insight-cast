using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace InsightCast.Converters;

/// <summary>
/// Segoe MDL2 Assets フォントグリフを DrawingImage に変換するヘルパー。
/// insight-sheet と同じアイコンシリーズを使用。
/// </summary>
public static class MdlIcons
{
    private static DrawingImage CreateIcon(string glyph, string colorHex = "#1C1917", double fontSize = 16)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        brush.Freeze();

        var typeface = new Typeface(
            new FontFamily("Segoe MDL2 Assets"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var formattedText = new FormattedText(
            glyph,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

        var geometry = formattedText.BuildGeometry(new Point(0, 0));

        // 中央配置のためバウンディングボックスで正規化
        var bounds = geometry.Bounds;
        if (!bounds.IsEmpty && (bounds.Width > 0 || bounds.Height > 0))
        {
            var group = new DrawingGroup();
            // 中央に配置（16x16 の中心に）
            var scale = Math.Min(16.0 / Math.Max(bounds.Width, 1), 16.0 / Math.Max(bounds.Height, 1));
            scale = Math.Min(scale, 1.0); // 拡大はしない
            var offsetX = (16.0 - bounds.Width * scale) / 2 - bounds.X * scale;
            var offsetY = (16.0 - bounds.Height * scale) / 2 - bounds.Y * scale;

            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(scale, scale));
            transform.Children.Add(new TranslateTransform(offsetX, offsetY));
            transform.Freeze();

            var gd = new GeometryDrawing(brush, null, geometry);
            gd.Freeze();
            group.Children.Add(gd);
            group.Transform = transform;
            group.Freeze();

            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        var drawing = new GeometryDrawing(brush, null, geometry);
        drawing.Freeze();
        var img = new DrawingImage(drawing);
        img.Freeze();
        return img;
    }

    // ── File menu (Application Menu) ──
    public static DrawingImage NewFile { get; } = CreateIcon("\uE8A5");              // Page
    public static DrawingImage OpenFile { get; } = CreateIcon("\uE838");             // OpenFolder
    public static DrawingImage Recent { get; } = CreateIcon("\uE823");               // Clock
    public static DrawingImage SaveFile { get; } = CreateIcon("\uE74E");             // Save
    public static DrawingImage SaveAs { get; } = CreateIcon("\uE792");               // SaveAs
    public static DrawingImage Import { get; } = CreateIcon("\uE8B5");               // Import
    public static DrawingImage Export { get; } = CreateIcon("\uE898");               // Export/Share
    public static DrawingImage Exit { get; } = CreateIcon("\uE7E8");                 // ChromeClose

    // ── Scene group ──
    public static DrawingImage Add { get; } = CreateIcon("\uE710", "#B8942F");       // Plus
    public static DrawingImage Delete { get; } = CreateIcon("\uE74D", "#DC2626");     // Delete
    public static DrawingImage MoveUp { get; } = CreateIcon("\uE74A");               // ChevronUp
    public static DrawingImage MoveDown { get; } = CreateIcon("\uE74B");             // ChevronDown

    // ── Export group ──
    public static DrawingImage Video { get; } = CreateIcon("\uE714", "#B8942F");     // Video
    public static DrawingImage Play { get; } = CreateIcon("\uE768", "#16A34A");      // Play

    // ── Template group ──
    public static DrawingImage Save { get; } = CreateIcon("\uE74E");                 // Save
    public static DrawingImage Load { get; } = CreateIcon("\uE838");                 // OpenFolder
    public static DrawingImage FolderOpen { get; } = CreateIcon("\uE8B7", "#B8942F"); // FolderOpen

    // ── ChatPanel header ──
    public static DrawingImage Help { get; }     = CreateIcon("\uE897");              // Help circle
    public static DrawingImage PopOut { get; }   = CreateIcon("\uE8A7");              // OpenInNewWindow
    public static DrawingImage Settings { get; } = CreateIcon("\uE713");              // Settings gear
    public static DrawingImage Key { get; }      = CreateIcon("\uE192", "#B8942F");   // Key (gold)
    public static DrawingImage AI { get; }       = CreateIcon("\uE99A", "#B8942F");   // Robot (gold)

    // ── Help / Support group ──
    public static DrawingImage Tutorial { get; } = CreateIcon("\uE897", "#B8942F");  // Help
    public static DrawingImage License { get; } = CreateIcon("\uE8D7", "#B8942F");   // Shield
    public static DrawingImage Info { get; } = CreateIcon("\uE946");                 // Info
    public static DrawingImage Document { get; } = CreateIcon("\uE8A5");             // Document
    public static DrawingImage Privacy { get; } = CreateIcon("\uE72E");              // Lock
}
