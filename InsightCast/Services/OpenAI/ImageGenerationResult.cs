namespace InsightCast.Services.OpenAI
{
    /// <summary>
    /// 画像生成の結果。
    /// </summary>
    public class ImageGenerationResult
    {
        /// <summary>
        /// 生成が成功したかどうか。
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 生成された画像のローカルパス。
        /// </summary>
        public string? ImagePath { get; set; }

        /// <summary>
        /// 画像の元URL（OpenAIから返されたURL）。
        /// </summary>
        public string? OriginalUrl { get; set; }

        /// <summary>
        /// エラーメッセージ（失敗時）。
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// エラー結果を作成します。
        /// </summary>
        public static ImageGenerationResult Error(string message) => new()
        {
            Success = false,
            ErrorMessage = message
        };
    }
}
