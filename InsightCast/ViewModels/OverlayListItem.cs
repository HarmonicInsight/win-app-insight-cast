using System.Windows.Media;
using InsightCast.Infrastructure;
using InsightCast.Models;
using InsightCast.Services;

namespace InsightCast.ViewModels
{
    public class OverlayListItem : ViewModelBase
    {
        private string _displayLabel = string.Empty;
        private Color _colorIndicator = Colors.White;

        public TextOverlay Overlay { get; }

        public string DisplayLabel
        {
            get => _displayLabel;
            set => SetProperty(ref _displayLabel, value);
        }

        public Color ColorIndicator
        {
            get => _colorIndicator;
            set => SetProperty(ref _colorIndicator, value);
        }

        public OverlayListItem(TextOverlay overlay, int index)
        {
            Overlay = overlay;
            UpdateLabel(index);
        }

        public void UpdateLabel(int index)
        {
            var text = string.IsNullOrWhiteSpace(Overlay.Text) ? LocalizationService.GetString("Overlay.Empty") : Overlay.Text;
            if (text.Length > 15)
                text = text[..15] + "...";

            DisplayLabel = $"[{index + 1}] {text}";

            // Update color indicator from overlay text color
            if (Overlay.TextColor is { Length: >= 3 })
            {
                ColorIndicator = Color.FromRgb(
                    (byte)Overlay.TextColor[0],
                    (byte)Overlay.TextColor[1],
                    (byte)Overlay.TextColor[2]);
            }
        }
    }
}
