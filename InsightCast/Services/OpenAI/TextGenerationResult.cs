namespace InsightCast.Services.OpenAI
{
    /// <summary>
    /// テキスト生成の結果。
    /// </summary>
    public class TextGenerationResult
    {
        /// <summary>
        /// 生成が成功したかどうか。
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 生成されたテキスト。
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// 使用されたトークン数。
        /// </summary>
        public int TokensUsed { get; set; }

        /// <summary>
        /// エラーメッセージ（失敗時）。
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// エラー結果を作成します。
        /// </summary>
        public static TextGenerationResult Error(string message) => new()
        {
            Success = false,
            ErrorMessage = message
        };
    }
}
