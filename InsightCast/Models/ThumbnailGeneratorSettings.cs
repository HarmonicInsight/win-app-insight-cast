using System.Text.Json.Serialization;

namespace InsightCast.Models
{
    /// <summary>
    /// サムネイルジェネレーターの設定（テンプレート保存用）
    /// </summary>
    public class ThumbnailGeneratorSettings
    {
        [JsonPropertyName("patternIndex")]
        public int PatternIndex { get; set; } = 0;

        [JsonPropertyName("mainText")]
        public string MainText { get; set; } = string.Empty;

        [JsonPropertyName("subText")]
        public string SubText { get; set; } = string.Empty;

        [JsonPropertyName("subSubText")]
        public string SubSubText { get; set; } = string.Empty;

        [JsonPropertyName("mainFontIndex")]
        public int MainFontIndex { get; set; } = 0;

        [JsonPropertyName("subFontIndex")]
        public int SubFontIndex { get; set; } = 0;

        [JsonPropertyName("subSubFontIndex")]
        public int SubSubFontIndex { get; set; } = 0;

        [JsonPropertyName("mainFontSizeIndex")]
        public int MainFontSizeIndex { get; set; } = 0;

        [JsonPropertyName("subFontSizeIndex")]
        public int SubFontSizeIndex { get; set; } = 0;

        [JsonPropertyName("subSubFontSizeIndex")]
        public int SubSubFontSizeIndex { get; set; } = 0;

        [JsonPropertyName("mainColorIndex")]
        public int MainColorIndex { get; set; } = 0;

        [JsonPropertyName("subColorIndex")]
        public int SubColorIndex { get; set; } = 1;

        [JsonPropertyName("subSubColorIndex")]
        public int SubSubColorIndex { get; set; } = 2;

        [JsonPropertyName("bgColorIndex")]
        public int BgColorIndex { get; set; } = 0;

        [JsonPropertyName("bgImagePath")]
        public string? BgImagePath { get; set; }
    }
}
