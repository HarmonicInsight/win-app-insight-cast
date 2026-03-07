using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using InsightCast.Services;

namespace InsightCast.Models
{
    public class TextStyle
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("name")]
        public string Name { get; set; } = "デフォルト";

        [JsonPropertyName("fontFamily")]
        public string FontFamily { get; set; } = "Yu Gothic UI";

        [JsonPropertyName("fontSize")]
        public int FontSize { get; set; } = 48;

        [JsonPropertyName("fontBold")]
        public bool FontBold { get; set; } = true;

        [JsonPropertyName("textColor")]
        public int[] TextColor { get; set; } = new[] { 255, 255, 255 };

        [JsonPropertyName("strokeColor")]
        public int[] StrokeColor { get; set; } = new[] { 0, 0, 0 };

        [JsonPropertyName("strokeWidth")]
        public int StrokeWidth { get; set; } = 3;

        [JsonPropertyName("backgroundColor")]
        public int[] BackgroundColor { get; set; } = new[] { 0, 0, 0 };

        [JsonPropertyName("backgroundOpacity")]
        public double BackgroundOpacity { get; set; } = 0.7;

        [JsonPropertyName("shadowEnabled")]
        public bool ShadowEnabled { get; set; } = true;

        [JsonPropertyName("shadowColor")]
        public int[] ShadowColor { get; set; } = new[] { 0, 0, 0 };

        [JsonPropertyName("shadowOffset")]
        public int[] ShadowOffset { get; set; } = new[] { 2, 2 };

        public TextStyle()
        {
        }

        public TextStyle(string id, string name)
        {
            Id = id;
            Name = name;
        }

        [JsonIgnore]
        public string HexTextColor => GetHexTextColor();

        [JsonIgnore]
        public string HexStrokeColor => GetHexStrokeColor();

        private static int SafeChannel(int[] arr, int index)
            => arr != null && index < arr.Length ? Math.Clamp(arr[index], 0, 255) : 0;

        public string GetHexTextColor()
        {
            return $"#{SafeChannel(TextColor, 0):X2}{SafeChannel(TextColor, 1):X2}{SafeChannel(TextColor, 2):X2}";
        }

        public string GetHexStrokeColor()
        {
            return $"#{SafeChannel(StrokeColor, 0):X2}{SafeChannel(StrokeColor, 1):X2}{SafeChannel(StrokeColor, 2):X2}";
        }

        // --- Presets ---

        public static IReadOnlyList<TextStyle> PRESET_STYLES => new List<TextStyle>()
        {
            new TextStyle
            {
                Id = "default",
                Name = LocalizationService.GetString("Style.Preset.Default"),
                FontFamily = "Yu Gothic UI",
                FontSize = 28,
                FontBold = true,
                TextColor = new[] { 255, 255, 255 },
                StrokeColor = new[] { 0, 0, 0 },
                StrokeWidth = 3,
                BackgroundColor = new[] { 0, 0, 0 },
                BackgroundOpacity = 0.7,
                ShadowEnabled = true,
                ShadowColor = new[] { 0, 0, 0 },
                ShadowOffset = new[] { 2, 2 }
            },
            new TextStyle
            {
                Id = "news",
                Name = LocalizationService.GetString("Style.Preset.News"),
                FontFamily = "Yu Gothic UI",
                FontSize = 42,
                FontBold = true,
                TextColor = new[] { 255, 255, 255 },
                StrokeColor = new[] { 0, 0, 128 },
                StrokeWidth = 2,
                BackgroundColor = new[] { 0, 0, 128 },
                BackgroundOpacity = 0.8,
                ShadowEnabled = false,
                ShadowColor = new[] { 0, 0, 0 },
                ShadowOffset = new[] { 0, 0 }
            },
            new TextStyle
            {
                Id = "cinema",
                Name = LocalizationService.GetString("Style.Preset.Cinema"),
                FontFamily = "Yu Mincho",
                FontSize = 52,
                FontBold = false,
                TextColor = new[] { 255, 255, 255 },
                StrokeColor = new[] { 0, 0, 0 },
                StrokeWidth = 1,
                BackgroundColor = new[] { 0, 0, 0 },
                BackgroundOpacity = 0.0,
                ShadowEnabled = true,
                ShadowColor = new[] { 0, 0, 0 },
                ShadowOffset = new[] { 3, 3 }
            },
            new TextStyle
            {
                Id = "variety",
                Name = LocalizationService.GetString("Style.Preset.Variety"),
                FontFamily = "Yu Gothic UI",
                FontSize = 56,
                FontBold = true,
                TextColor = new[] { 255, 255, 0 },
                StrokeColor = new[] { 255, 0, 0 },
                StrokeWidth = 4,
                BackgroundColor = new[] { 0, 0, 0 },
                BackgroundOpacity = 0.0,
                ShadowEnabled = true,
                ShadowColor = new[] { 0, 0, 0 },
                ShadowOffset = new[] { 3, 3 }
            },
            new TextStyle
            {
                Id = "documentary",
                Name = LocalizationService.GetString("Style.Preset.Documentary"),
                FontFamily = "Yu Gothic UI",
                FontSize = 44,
                FontBold = false,
                TextColor = new[] { 255, 255, 255 },
                StrokeColor = new[] { 0, 0, 0 },
                StrokeWidth = 2,
                BackgroundColor = new[] { 0, 0, 0 },
                BackgroundOpacity = 0.5,
                ShadowEnabled = false,
                ShadowColor = new[] { 0, 0, 0 },
                ShadowOffset = new[] { 0, 0 }
            },
            new TextStyle
            {
                Id = "education",
                Name = LocalizationService.GetString("Style.Preset.Education"),
                FontFamily = "Yu Gothic UI",
                FontSize = 46,
                FontBold = true,
                TextColor = new[] { 51, 51, 51 },
                StrokeColor = new[] { 255, 255, 255 },
                StrokeWidth = 3,
                BackgroundColor = new[] { 255, 255, 255 },
                BackgroundOpacity = 0.9,
                ShadowEnabled = false,
                ShadowColor = new[] { 0, 0, 0 },
                ShadowOffset = new[] { 0, 0 }
            },
            new TextStyle
            {
                Id = "horror",
                Name = LocalizationService.GetString("Style.Preset.Horror"),
                FontFamily = "Yu Mincho",
                FontSize = 50,
                FontBold = true,
                TextColor = new[] { 255, 0, 0 },
                StrokeColor = new[] { 0, 0, 0 },
                StrokeWidth = 2,
                BackgroundColor = new[] { 0, 0, 0 },
                BackgroundOpacity = 0.6,
                ShadowEnabled = true,
                ShadowColor = new[] { 128, 0, 0 },
                ShadowOffset = new[] { 2, 2 }
            },
            new TextStyle
            {
                Id = "cute",
                Name = LocalizationService.GetString("Style.Preset.Cute"),
                FontFamily = "Yu Gothic UI",
                FontSize = 44,
                FontBold = true,
                TextColor = new[] { 255, 105, 180 },
                StrokeColor = new[] { 255, 255, 255 },
                StrokeWidth = 3,
                BackgroundColor = new[] { 255, 228, 225 },
                BackgroundOpacity = 0.7,
                ShadowEnabled = false,
                ShadowColor = new[] { 0, 0, 0 },
                ShadowOffset = new[] { 0, 0 }
            },
            new TextStyle
            {
                Id = "tech",
                Name = LocalizationService.GetString("Style.Preset.Tech"),
                FontFamily = "Consolas",
                FontSize = 40,
                FontBold = false,
                TextColor = new[] { 0, 255, 0 },
                StrokeColor = new[] { 0, 0, 0 },
                StrokeWidth = 1,
                BackgroundColor = new[] { 0, 0, 0 },
                BackgroundOpacity = 0.85,
                ShadowEnabled = true,
                ShadowColor = new[] { 0, 128, 0 },
                ShadowOffset = new[] { 1, 1 }
            },
            new TextStyle
            {
                Id = "elegant",
                Name = LocalizationService.GetString("Style.Preset.Elegant"),
                FontFamily = "Yu Mincho",
                FontSize = 46,
                FontBold = false,
                TextColor = new[] { 212, 175, 55 },
                StrokeColor = new[] { 0, 0, 0 },
                StrokeWidth = 1,
                BackgroundColor = new[] { 0, 0, 0 },
                BackgroundOpacity = 0.4,
                ShadowEnabled = true,
                ShadowColor = new[] { 0, 0, 0 },
                ShadowOffset = new[] { 2, 2 }
            }
        };

        public static List<(string FontName, string DisplayName)> AVAILABLE_FONTS => new()
        {
            ("Yu Gothic UI", LocalizationService.GetString("Font.YuGothicUI")),
            ("Yu Gothic", LocalizationService.GetString("Font.YuGothic")),
            ("Yu Mincho", LocalizationService.GetString("Font.YuMincho")),
            ("Meiryo", LocalizationService.GetString("Font.Meiryo")),
            ("Meiryo UI", LocalizationService.GetString("Font.MeiryoUI")),
            ("MS Gothic", LocalizationService.GetString("Font.MSGothic")),
            ("MS Mincho", LocalizationService.GetString("Font.MSMincho")),
            ("MS PGothic", LocalizationService.GetString("Font.MSPGothic")),
            ("MS PMincho", LocalizationService.GetString("Font.MSPMincho")),
            ("BIZ UDGothic", LocalizationService.GetString("Font.BIZUDGothic")),
            ("BIZ UDMincho", LocalizationService.GetString("Font.BIZUDMincho")),
            ("Consolas", "Consolas"),
            ("Arial", "Arial"),
            ("Segoe UI", "Segoe UI")
        };

        public static List<(string Name, int[] Color)> PRESET_TEXT_COLORS => new()
        {
            (LocalizationService.GetString("Color.White"), new[] { 255, 255, 255 }),
            (LocalizationService.GetString("Color.Black"), new[] { 0, 0, 0 }),
            (LocalizationService.GetString("Color.Red"), new[] { 255, 0, 0 }),
            (LocalizationService.GetString("Color.Blue"), new[] { 0, 0, 255 }),
            (LocalizationService.GetString("Color.Yellow"), new[] { 255, 255, 0 }),
            (LocalizationService.GetString("Color.Green"), new[] { 0, 128, 0 }),
            (LocalizationService.GetString("Color.Pink"), new[] { 255, 105, 180 }),
            (LocalizationService.GetString("Color.Orange"), new[] { 255, 165, 0 }),
            (LocalizationService.GetString("Color.Gold"), new[] { 212, 175, 55 }),
            (LocalizationService.GetString("Color.LightBlue"), new[] { 0, 191, 255 })
        };

        public static List<(string Name, int[] Color)> PRESET_STROKE_COLORS => new()
        {
            (LocalizationService.GetString("Color.Black"), new[] { 0, 0, 0 }),
            (LocalizationService.GetString("Color.White"), new[] { 255, 255, 255 }),
            (LocalizationService.GetString("Color.Navy"), new[] { 0, 0, 128 }),
            (LocalizationService.GetString("Color.Red"), new[] { 255, 0, 0 }),
            (LocalizationService.GetString("Color.Green"), new[] { 0, 128, 0 }),
            (LocalizationService.GetString("Color.Gray"), new[] { 128, 128, 128 }),
            (LocalizationService.GetString("Color.Brown"), new[] { 139, 69, 19 }),
            (LocalizationService.GetString("Color.Purple"), new[] { 128, 0, 128 })
        };
    }
}
