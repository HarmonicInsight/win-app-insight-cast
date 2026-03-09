using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InsightCast.Models;
using InsightCast.Services.Claude;

namespace InsightCast.Services
{
    public class ChapterGenerationService
    {
        private readonly IClaudeService _claudeService;

        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public ChapterGenerationService(IClaudeService claudeService)
        {
            _claudeService = claudeService;
        }

        public async Task<ChapterStructure> GenerateChaptersAsync(
            string referenceContext,
            int chapterCount,
            string? additionalInstructions = null,
            CancellationToken ct = default)
        {
            var systemPrompt = BuildSystemPrompt(chapterCount, additionalInstructions);
            var userMessage = string.IsNullOrWhiteSpace(referenceContext)
                ? "トピックに基づいてプレゼンテーション構成を生成してください。"
                : $"以下の参考資料に基づいてプレゼンテーション構成を生成してください。\n\n---参考資料ここから---\n{referenceContext}\n---参考資料ここまで---";

            var response = await _claudeService.SendMessageAsync(userMessage, systemPrompt, ct);

            return ParseResponse(response);
        }

        public static string BuildSystemPrompt(int chapterCount, string? additionalInstructions)
        {
            var prompt = $@"あなたはプレゼンテーション構成のプロフェッショナルであり、企業研修・営業資料・セミナー教材の設計経験が豊富です。
参考資料を深く分析し、聴衆を惹きつけるプレゼンテーション（PowerPointスライド）の構成を作成してください。

# 作成ルール

## 全体構成
- スライド数: ちょうど {chapterCount} 枚（タイトルスライドは含まない）
- 論理構造を守ること:
  - 第1章: 導入・課題提起（なぜこのテーマが重要か）
  - 中間章: 本論・データ・事例・解決策
  - 最終章: まとめ・結論・次のアクション
- 各スライドは1つの明確なメッセージに絞ること（1スライド1メッセージの原則）
- titleは結論型で書くこと（「○○の概要」ではなく「○○で生産性が30%向上する」のように要点を述べる）

## ストーリー骨格（推奨パターン）
以下を参考にスライドの流れを設計すること:
1. Title（表紙）— 自動生成のため不要
2. Purpose（なぜこのテーマか — 課題提起・目的の明示）
3. Agenda（目次 — 全体の見通し）
4. Overview（全体像・業務フロー・背景）
5. Step（手順・詳細 — 複数スライド可）
6. Decision（判断基準・分岐・例外処理）
7. Data（数値・グラフ・比較データ）
8. FAQ（よくある質問・ミス・注意点）
9. Summary/Checklist（まとめ・チェックリスト・次のアクション）

## narration（ナレーション原稿）
- プレゼンターがスライドを見せながら読み上げる原稿として書くこと
- 1スライドあたり100〜200文字程度（読み上げ30秒〜1分相当）
- 口語体で自然な語りかけ（「〜ですね」「〜してみましょう」等OK）
- 資料の数値・固有名詞・具体例を積極的に引用すること
- 冒頭で「このスライドで何を伝えるか」を明示し、最後に次スライドへの橋渡しを入れること
- 聴衆への問いかけや具体例を交えて理解を促進すること

## imageDescription（画像生成プロンプト）
- DALL-E / Midjourney / Stable Diffusion で使える英語プロンプトとして書くこと
- 必ず以下の要素を含めること:
  1. 被写体の具体的な描写（何が、どこに、どんな状態で）
  2. 構図（close-up / wide shot / isometric / flat lay 等）
  3. スタイル（professional photograph / corporate illustration / infographic / flat design icon 等）
  4. 色調・雰囲気（warm tones, clean white background, modern minimalist 等）
  5. プレゼンスライド用であることを意識（テキストの邪魔にならない、情報が読み取れる）
- 70〜120語の英語で記述すること
- 「A presentation slide showing...」のような安直な表現は禁止
- テキストやロゴは含めないこと（スライドのテキストは別途配置される）

## 言語
- title と narration: 参考資料と同じ言語で書くこと（日本語の資料なら日本語）
- imageDescription: 必ず英語で書くこと（画像生成AIは英語が最も精度が高いため）

# 出力形式

以下のJSON形式のみを出力してください。JSON以外のテキストは一切含めないでください。

{{
  ""videoTitle"": ""プレゼンテーションのタイトル"",
  ""chapters"": [
    {{
      ""title"": ""スライドタイトル（結論型で書く）"",
      ""narration"": ""このスライドのナレーション原稿..."",
      ""imageDescription"": ""Professional photograph of... detailed English prompt for image generation...""
    }}
  ]
}}";

            if (!string.IsNullOrWhiteSpace(additionalInstructions))
                prompt += $"\n\n# 追加指示\n{additionalInstructions}";

            return prompt;
        }

        private static ChapterStructure ParseResponse(string response)
        {
            var json = response.Trim();

            // Handle markdown code blocks
            if (json.StartsWith("```"))
            {
                var startIdx = json.IndexOf('{');
                var endIdx = json.LastIndexOf('}');
                if (startIdx >= 0 && endIdx > startIdx)
                    json = json[startIdx..(endIdx + 1)];
            }

            var result = JsonSerializer.Deserialize<ChapterStructure>(json, s_jsonOptions);
            if (result == null)
                throw new InvalidOperationException("Failed to parse AI response as chapter structure.");

            return result;
        }
    }
}
