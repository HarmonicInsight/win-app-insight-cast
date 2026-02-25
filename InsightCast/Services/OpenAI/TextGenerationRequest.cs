namespace InsightCast.Services.OpenAI
{
    /// <summary>
    /// テキスト生成リクエストの設定。
    /// </summary>
    public class TextGenerationRequest
    {
        /// <summary>
        /// ナレーションのトピック/テーマ。
        /// </summary>
        public string Topic { get; set; } = string.Empty;

        /// <summary>
        /// ナレーションのスタイル (educational, conversational, formal, etc.)。
        /// </summary>
        public string? Style { get; set; }

        /// <summary>
        /// 目標の長さ（秒）。
        /// </summary>
        public int TargetDurationSeconds { get; set; } = 30;

        /// <summary>
        /// 追加の指示。
        /// </summary>
        public string? AdditionalInstructions { get; set; }

        /// <summary>
        /// 使用するモデル（指定しない場合はデフォルト）。
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// 最大トークン数。
        /// </summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>
        /// Temperature（0.0-2.0）。
        /// </summary>
        public double Temperature { get; set; } = 0.7;
    }
}
