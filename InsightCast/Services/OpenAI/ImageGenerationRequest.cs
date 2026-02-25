namespace InsightCast.Services.OpenAI
{
    /// <summary>
    /// 画像生成リクエストの設定。
    /// </summary>
    public class ImageGenerationRequest
    {
        /// <summary>
        /// 画像の説明/プロンプト。
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 画像のスタイル (photorealistic, illustration, anime, etc.)。
        /// </summary>
        public string? Style { get; set; }

        /// <summary>
        /// 使用するモデル（指定しない場合はデフォルト）。
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// 画像サイズ (1024x1024, 1792x1024, 1024x1792)。
        /// </summary>
        public string? Size { get; set; }

        /// <summary>
        /// 画像品質 (standard, hd)。
        /// </summary>
        public string? Quality { get; set; }
    }
}
