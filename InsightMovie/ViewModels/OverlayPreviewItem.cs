using System.Windows;
using System.Windows.Media;
using InsightMovie.Infrastructure;
using InsightMovie.Models;

namespace InsightMovie.ViewModels
{
    /// <summary>
    /// Lightweight view model for rendering text overlay previews on the thumbnail canvas.
    /// Positions are scaled from the overlay's percentage values to a 120x180 preview area.
    /// </summary>
    public class OverlayPreviewItem : ViewModelBase
    {
        private const double PreviewWidth = 120.0;
        private const double PreviewHeight = 180.0;

        public string Text { get; }
        public double PreviewFontSize { get; }
        public double CanvasLeft { get; }
        public double CanvasTop { get; }
        public double Opacity { get; }
        public FontWeight FontWeightValue { get; }
        public TextAlignment TextAlignmentValue { get; }
        public Color TextColorValue { get; }

        public OverlayPreviewItem(TextOverlay overlay)
        {
            Text = string.IsNullOrWhiteSpace(overlay.Text) ? "" : overlay.Text;

            // Scale font size for 120px wide preview (original is ~1920px)
            PreviewFontSize = System.Math.Max(6, overlay.FontSize * PreviewWidth / 1920.0 * 2.0);

            // Convert percentage position to canvas coordinates
            CanvasLeft = overlay.XPercent / 100.0 * PreviewWidth - PreviewWidth * 0.4;
            CanvasTop = overlay.YPercent / 100.0 * PreviewHeight - PreviewFontSize * 0.6;

            // Clamp to visible area
            CanvasLeft = System.Math.Clamp(CanvasLeft, 0, PreviewWidth - 20);
            CanvasTop = System.Math.Clamp(CanvasTop, 0, PreviewHeight - PreviewFontSize);

            Opacity = overlay.Opacity;
            FontWeightValue = overlay.FontBold ? FontWeights.Bold : FontWeights.Normal;

            TextAlignmentValue = overlay.Alignment switch
            {
                Models.TextAlignment.Left => System.Windows.TextAlignment.Left,
                Models.TextAlignment.Right => System.Windows.TextAlignment.Right,
                _ => System.Windows.TextAlignment.Center
            };

            TextColorValue = overlay.TextColor is { Length: >= 3 }
                ? Color.FromRgb((byte)overlay.TextColor[0], (byte)overlay.TextColor[1], (byte)overlay.TextColor[2])
                : Colors.White;
        }
    }
}
