using System.Text.Json.Serialization;

namespace InsightCast.Models
{
    /// <summary>
    /// AI生成の設定を保持するクラス。
    /// シーンごとにナレーションや画像をAIで生成するかを制御します。
    /// </summary>
    public class AIGenerationSettings
    {
        /// <summary>
        /// ナレーションをAIで生成するかどうか。
        /// </summary>
        [JsonPropertyName("generateNarration")]
        public bool GenerateNarration { get; set; } = false;

        /// <summary>
        /// ナレーション生成のトピック/テーマ。
        /// </summary>
        [JsonPropertyName("narrationTopic")]
        public string? NarrationTopic { get; set; }

        /// <summary>
        /// ナレーションのスタイル (educational, conversational, formal, etc.)。
        /// </summary>
        [JsonPropertyName("narrationStyle")]
        public string? NarrationStyle { get; set; }

        /// <summary>
        /// ナレーションの目標秒数。
        /// </summary>
        [JsonPropertyName("targetDurationSeconds")]
        public int TargetDurationSeconds { get; set; } = 30;

        /// <summary>
        /// 追加の指示。
        /// </summary>
        [JsonPropertyName("additionalInstructions")]
        public string? AdditionalInstructions { get; set; }

        /// <summary>
        /// 画像をAIで生成するかどうか。
        /// </summary>
        [JsonPropertyName("generateImage")]
        public bool GenerateImage { get; set; } = false;

        /// <summary>
        /// 画像生成の説明/プロンプト。
        /// </summary>
        [JsonPropertyName("imageDescription")]
        public string? ImageDescription { get; set; }

        /// <summary>
        /// 画像のスタイル (photorealistic, illustration, anime, etc.)。
        /// </summary>
        [JsonPropertyName("imageStyle")]
        public string? ImageStyle { get; set; }

        /// <summary>
        /// AI生成が必要かどうかを判定します。
        /// </summary>
        [JsonIgnore]
        public bool RequiresGeneration => GenerateNarration || GenerateImage;

        /// <summary>
        /// ナレーション生成が有効で、トピックが設定されているかを判定します。
        /// </summary>
        [JsonIgnore]
        public bool CanGenerateNarration => GenerateNarration && !string.IsNullOrEmpty(NarrationTopic);

        /// <summary>
        /// 画像生成が有効で、説明が設定されているかを判定します。
        /// </summary>
        [JsonIgnore]
        public bool CanGenerateImage => GenerateImage && !string.IsNullOrEmpty(ImageDescription);
    }
}
