using System;
using System.Text.Json.Serialization;
using InsightMovie.Services;

namespace InsightMovie.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TextAlignment
    {
        Left,
        Center,
        Right
    }

    public class TextOverlay
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("xPercent")]
        public double XPercent { get; set; } = 50.0;

        [JsonPropertyName("yPercent")]
        public double YPercent { get; set; } = 50.0;

        [JsonPropertyName("fontFamily")]
        public string FontFamily { get; set; } = "Yu Gothic UI";

        [JsonPropertyName("fontSize")]
        public int FontSize { get; set; } = 64;

        [JsonPropertyName("fontBold")]
        public bool FontBold { get; set; } = true;

        [JsonPropertyName("textColor")]
        public int[] TextColor { get; set; } = new[] { 255, 255, 255 };

        [JsonPropertyName("strokeColor")]
        public int[] StrokeColor { get; set; } = new[] { 0, 0, 0 };

        [JsonPropertyName("strokeWidth")]
        public int StrokeWidth { get; set; } = 2;

        [JsonPropertyName("alignment")]
        public TextAlignment Alignment { get; set; } = TextAlignment.Center;

        [JsonPropertyName("shadowEnabled")]
        public bool ShadowEnabled { get; set; } = true;

        [JsonPropertyName("shadowColor")]
        public int[] ShadowColor { get; set; } = new[] { 0, 0, 0 };

        [JsonPropertyName("shadowOffset")]
        public int[] ShadowOffset { get; set; } = new[] { 2, 2 };

        [JsonPropertyName("opacity")]
        public double Opacity { get; set; } = 1.0;

        public TextOverlay()
        {
        }

        [JsonIgnore]
        public bool HasText => !string.IsNullOrWhiteSpace(Text);

        [JsonIgnore]
        public string DisplayLabel => string.IsNullOrWhiteSpace(Text)
            ? LocalizationService.GetString("Overlay.Empty")
            : Text.Length > 20 ? Text[..20] + "..." : Text;

        /// <summary>
        /// Creates a title overlay preset for cover pages.
        /// </summary>
        public static TextOverlay CreateTitle(string? text = null)
        {
            return new TextOverlay
            {
                Text = text ?? LocalizationService.GetString("Overlay.DefaultTitle"),
                XPercent = 50.0,
                YPercent = 40.0,
                FontSize = 72,
                FontBold = true,
                Alignment = TextAlignment.Center,
                TextColor = new[] { 255, 255, 255 },
                StrokeColor = new[] { 0, 0, 0 },
                StrokeWidth = 3,
                ShadowEnabled = true,
                ShadowColor = new[] { 0, 0, 0 },
                ShadowOffset = new[] { 3, 3 }
            };
        }

        /// <summary>
        /// Creates a subtitle overlay preset for cover pages.
        /// </summary>
        public static TextOverlay CreateSubheading(string? text = null)
        {
            return new TextOverlay
            {
                Text = text ?? LocalizationService.GetString("Overlay.DefaultSubtitle"),
                XPercent = 50.0,
                YPercent = 55.0,
                FontSize = 40,
                FontBold = false,
                Alignment = TextAlignment.Center,
                TextColor = new[] { 220, 220, 220 },
                StrokeColor = new[] { 0, 0, 0 },
                StrokeWidth = 1,
                ShadowEnabled = true,
                ShadowColor = new[] { 0, 0, 0 },
                ShadowOffset = new[] { 2, 2 }
            };
        }
    }
}
