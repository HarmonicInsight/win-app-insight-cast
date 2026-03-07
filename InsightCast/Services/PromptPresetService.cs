// =============================================================================
// PromptPresetService.cs — InsightCommon 共通基盤への静的ラッパー
//
// 旧 PromptPreset クラスは InsightCommon.AI.UserPromptPreset に統合。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using InsightCommon.AI;

namespace InsightCast.Services;

/// <summary>
/// Cast 用プロンプトプリセット管理。InsightCommon 共通基盤の静的ラッパー。
/// </summary>
public static class PromptPresetService
{
    private static readonly InsightCommon.AI.PromptPresetService s_inner =
        new("INMV", GetBuiltInPresets);

    static PromptPresetService()
    {
        MigrateOldDataOnce();
    }

    // ── CRUD ──

    public static List<UserPromptPreset> LoadAll() => s_inner.LoadAll();
    public static void Add(UserPromptPreset preset) => s_inner.Add(preset);
    public static void Update(string id, UserPromptPreset updated) => s_inner.Update(id, updated);
    public static void Remove(string id) => s_inner.Remove(id);
    public static UserPromptPreset? GetDefault() => s_inner.GetDefault();
    public static void SetDefault(string id) => s_inner.SetDefault(id);
    public static void IncrementUsage(string id) => s_inner.IncrementUsage(id);
    public static void TogglePin(string id) => s_inner.TogglePin(id);

    // ── Export / Import ──

    public static void Export(List<UserPromptPreset> presets, string filePath)
        => InsightCommon.AI.PromptPresetService.Export(presets, filePath);

    public static List<UserPromptPreset> Import(string filePath)
        => InsightCommon.AI.PromptPresetService.Import(filePath);

    public static string GenerateId()
        => InsightCommon.AI.PromptPresetService.GenerateId();

    // ── 旧データマイグレーション ──

    private static void MigrateOldDataOnce()
    {
        try
        {
            var oldPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "InsightCast", "prompt_presets.json");
            var newDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HarmonicInsight", "INMV");
            var newPath = Path.Combine(newDir, "prompt_presets.json");

            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                Directory.CreateDirectory(newDir);
                File.Copy(oldPath, newPath);
                Trace.WriteLine($"PromptPresetService: migrated {oldPath} → {newPath}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"PromptPresetService migration failed: {ex.Message}");
        }
    }

    // ── Built-in Presets for InsightCast (Video Creation) ──

    private static List<UserPromptPreset> GetBuiltInPresets()
    {
        var now = new DateTime(2025, 1, 1);
        return
        [
            new()
            {
                Id = "builtin_narration_basic",
                Name = "ナレーション原稿作成",
                Category = "ナレーション",
                Description = "トピックからナレーション原稿を自動生成",
                SystemPrompt = "あなたは教育動画のナレーションライターです。与えられたトピックについて、以下の形式でナレーション原稿を作成してください。\n\n- 1シーン = 1段落（200〜300文字程度）\n- 視聴者に語りかける口調（です・ます調）\n- 専門用語は初出時に簡潔に説明\n- 各シーンは【シーン1】【シーン2】... で区切る\n\n原稿のみを出力してください。",
                Author = "InsightCast",
                IsDefault = true,
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_narration_tutorial",
                Name = "チュートリアル動画向け",
                Category = "ナレーション",
                Description = "操作説明・ハウツー動画向けのナレーション",
                SystemPrompt = "あなたはソフトウェアチュートリアル動画のナレーターです。与えられた操作手順について、以下の形式でナレーション原稿を作成してください。\n\n- ステップバイステップで説明\n- 「まず〜してください」「次に〜します」のような誘導表現\n- 操作の意図・目的も簡潔に説明\n- 各ステップは【ステップ1】【ステップ2】... で区切る\n- 最後に要点のまとめを入れる\n\n原稿のみを出力してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_narration_presentation",
                Name = "プレゼン動画向け",
                Category = "ナレーション",
                Description = "ビジネスプレゼンテーション向けのナレーション",
                SystemPrompt = "あなたはビジネスプレゼンテーションのナレーターです。与えられた内容について、以下の形式でナレーション原稿を作成してください。\n\n構成:\n1. 導入（問題提起・興味喚起）\n2. 本論（ポイントを3つ程度に整理）\n3. 結論（まとめ・次のアクション）\n\n- プロフェッショナルで説得力のある口調\n- データや事実を根拠として示す\n- 各セクションは【導入】【ポイント1】... で区切る\n\n原稿のみを出力してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_subtitle_optimize",
                Name = "字幕テキスト最適化",
                Category = "字幕・テキスト",
                Description = "長いナレーションを字幕向けに短縮",
                SystemPrompt = "与えられたナレーション原稿を字幕用に最適化してください。\n\n- 1行は20文字以内を目安\n- 冗長な表現を削除\n- 意味は変えずに簡潔に\n- 読みやすさを優先\n\n最適化後のテキストのみを出力してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_title_suggest",
                Name = "動画タイトル提案",
                Category = "字幕・テキスト",
                Description = "魅力的な動画タイトルを複数提案",
                SystemPrompt = "与えられた動画の内容について、魅力的なタイトルを5つ提案してください。\n\n条件:\n- 30文字以内\n- 視聴者の興味を引く表現\n- SEOを意識したキーワードを含む\n- バリエーション豊かに（疑問形、数字入り、ベネフィット訴求など）\n\n提案のみを出力してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_translate_en",
                Name = "英語に翻訳",
                Category = "字幕・テキスト",
                Description = "ナレーション・字幕を英語に翻訳",
                SystemPrompt = "与えられた日本語テキストを英語に翻訳してください。\n\n- 動画ナレーション・字幕にふさわしい自然な英語\n- 文語的すぎない、話し言葉寄りの表現\n- 専門用語は適切な英語表現を使用\n\n翻訳文のみを出力してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_structure_suggest",
                Name = "動画構成を提案",
                Category = "構成・企画",
                Description = "トピックから動画の構成案を生成",
                SystemPrompt = "与えられたトピックについて、教育動画の構成案を作成してください。\n\n出力形式:\n- 動画の長さ目安（分）\n- 各シーンの内容と時間配分\n- 必要な素材（画像・図解・実写など）の提案\n\n構成案を箇条書きで出力してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_cta_suggest",
                Name = "CTA（行動喚起）提案",
                Category = "構成・企画",
                Description = "動画の最後に入れるCTAを提案",
                SystemPrompt = "与えられた動画の内容に適したCTA（Call To Action / 行動喚起）を5つ提案してください。\n\n条件:\n- 視聴者が次に取るべきアクションを明確に\n- 「チャンネル登録」「詳細はリンクから」などの定番 + オリジナル\n- 動画の内容に合った自然な流れ\n\n提案のみを出力してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_proofread",
                Name = "校正・誤字脱字チェック",
                Category = "校正・改善",
                Description = "ナレーション原稿の誤字脱字を検出",
                SystemPrompt = "与えられたナレーション原稿の誤字脱字、文法ミス、句読点の誤りを検出してください。\n\n修正箇所ごとに:\n- 【誤】元のテキスト\n- 【正】修正案\n- 【理由】修正理由\n\nの形式で出力してください。修正が不要な場合は「修正箇所はありません」と回答してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_improve_engagement",
                Name = "視聴者エンゲージメント向上",
                Category = "校正・改善",
                Description = "ナレーションをより魅力的に改善",
                SystemPrompt = "与えられたナレーション原稿を、視聴者のエンゲージメントを高める形で改善してください。\n\n改善ポイント:\n- 冒頭で興味を引くフック\n- 具体例や比喩を追加\n- 視聴者への問いかけを挿入\n- リズム感のある文章に\n\n改善後の原稿と、変更点の説明を出力してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_summary",
                Name = "内容を要約",
                Category = "分析・要約",
                Description = "長い原稿の要点を簡潔にまとめる",
                SystemPrompt = "与えられた原稿の要約を作成してください。\n\n- 主要なポイントを3-5個に整理\n- 各ポイントを1-2文で簡潔に説明\n- 全体で元の原稿の20-30%程度の長さ\n\n「要約」という見出しの後に内容を出力してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_keyword_extract",
                Name = "キーワード抽出",
                Category = "分析・要約",
                Description = "動画のSEOキーワードを抽出・提案",
                SystemPrompt = "与えられた動画の内容から、SEO向けのキーワードを抽出・提案してください。\n\n出力形式:\n- メインキーワード（1-2個）\n- サブキーワード（3-5個）\n- ロングテールキーワード（3-5個）\n- 関連キーワード（5-10個）\n\n各キーワードの検索意図も簡潔に説明してください。",
                Author = "InsightCast",
                CreatedAt = now,
                ModifiedAt = now,
            },
        ];
    }
}
