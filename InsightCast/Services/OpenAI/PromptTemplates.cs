using System.Text;

namespace InsightCast.Services.OpenAI
{
    /// <summary>
    /// AI生成用のプロンプトテンプレート。
    /// </summary>
    public static class PromptTemplates
    {
        /// <summary>
        /// ナレーション生成のシステムプロンプト。
        /// </summary>
        public const string NARRATION_SYSTEM_PROMPT = @"あなたはプロのナレーターです。以下の指示に従って、動画のナレーション原稿を作成してください。

ルール:
1. 自然で聞き取りやすい日本語を使用
2. 指定された長さに収まるよう調整（1秒あたり約4文字程度）
3. 視聴者の興味を引く構成
4. 専門用語は適切に説明
5. 句読点を適切に配置し、読みやすくする
6. ナレーション原稿のみを出力（余計な説明は不要）";

        /// <summary>
        /// ナレーション生成のユーザープロンプトを構築します。
        /// </summary>
        public static string BuildNarrationPrompt(TextGenerationRequest request)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"トピック: {request.Topic}");

            if (!string.IsNullOrEmpty(request.Style))
            {
                var styleDescription = request.Style.ToLowerInvariant() switch
                {
                    "educational" => "教育的で分かりやすい",
                    "conversational" => "親しみやすい会話調",
                    "formal" => "フォーマルで丁寧",
                    "energetic" => "元気で活発",
                    "calm" => "落ち着いた穏やか",
                    _ => request.Style
                };
                sb.AppendLine($"スタイル: {styleDescription}");
            }

            if (request.TargetDurationSeconds > 0)
            {
                var targetChars = request.TargetDurationSeconds * 4; // 1秒あたり約4文字
                sb.AppendLine($"目標長さ: 約{request.TargetDurationSeconds}秒分（約{targetChars}文字）");
            }

            if (!string.IsNullOrEmpty(request.AdditionalInstructions))
            {
                sb.AppendLine($"追加指示: {request.AdditionalInstructions}");
            }

            sb.AppendLine();
            sb.AppendLine("上記の条件に基づいて、ナレーション原稿を作成してください。");

            return sb.ToString();
        }

        /// <summary>
        /// 画像生成のプロンプトを構築します。
        /// </summary>
        public static string BuildImagePrompt(ImageGenerationRequest request)
        {
            var sb = new StringBuilder();
            sb.Append(request.Description);

            if (!string.IsNullOrEmpty(request.Style))
            {
                var styleEnglish = request.Style.ToLowerInvariant() switch
                {
                    "photorealistic" => "photorealistic, high detail photography",
                    "illustration" => "professional illustration, clean lines",
                    "anime" => "anime style, vibrant colors",
                    "minimalist" => "minimalist design, clean and simple",
                    "3d" => "3D rendered, high quality CGI",
                    "watercolor" => "watercolor painting style",
                    _ => request.Style
                };
                sb.Append($". Style: {styleEnglish}");
            }

            sb.Append(". High quality, professional, suitable for video presentation. No text in image.");

            return sb.ToString();
        }

        /// <summary>
        /// スタイルの選択肢を取得します。
        /// </summary>
        public static string[] GetNarrationStyles() => new[]
        {
            "educational",
            "conversational",
            "formal",
            "energetic",
            "calm"
        };

        /// <summary>
        /// 画像スタイルの選択肢を取得します。
        /// </summary>
        public static string[] GetImageStyles() => new[]
        {
            "photorealistic",
            "illustration",
            "anime",
            "minimalist",
            "3d",
            "watercolor"
        };
    }
}
