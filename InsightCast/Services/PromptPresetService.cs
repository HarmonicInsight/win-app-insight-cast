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

            // ================================================================
            // PPTX生成（研修・プレゼン資料）
            // ================================================================
            new()
            {
                Id = "builtin_pptx_training",
                Name = "研修スライド",
                Category = "PPTX生成",
                Description = "参考資料から研修用PowerPointスライドを生成",
                SystemPrompt = @"与えられた参考資料をもとに、研修用PowerPointスライドの構成と全スライドの内容を作成してください。

【出力形式】
各スライドを以下のJSON形式で出力:

{{
  ""videoTitle"": ""プレゼンテーションのタイトル"",
  ""chapters"": [
    {{
      ""title"": ""スライドタイトル"",
      ""narration"": ""発表者用ノート（話すべきポイント、補足説明）"",
      ""imageDescription"": ""English prompt for image generation...""
    }}
  ]
}}

【構成ルール】
1. 目次（アジェンダ）
2. 学習目標（この研修で身につくこと 3-5 個）
3. 本編スライド（1スライド1メッセージ、箇条書きは5項目以内）
4. 演習・ワーク（実践的な課題を含める）
5. まとめ・振り返り
6. Q&A / 参考資料

【品質基準】
- 1スライドのテキスト量: 最大6行
- 発表者ノートに詳しい解説を記載（スライドは簡潔に）
- titleは結論型で書くこと",
                Author = "InsightCast",
                CreatedAt = now, ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_pptx_onboarding",
                Name = "新入社員研修パック",
                Category = "PPTX生成",
                Description = "会社情報・規程から新入社員向け研修スライドを生成",
                SystemPrompt = @"与えられた会社情報・社内規程・業務マニュアルから、新入社員向けオンボーディング研修スライドを作成してください。

【必須セクション】
1. 会社概要（ビジョン・ミッション・沿革・組織図）
2. 事業紹介（主要事業・製品・顧客）
3. 社内ルール（就業規則のポイント、服務規程）
4. ITセキュリティ（パスワード管理、情報漏洩防止）
5. ビジネスマナー（メール、電話、名刺交換）
6. 業務ツール紹介（使用するシステム・ツール一覧）
7. 相談先・サポート体制
8. 30日/60日/90日ロードマップ

JSON形式で出力:
{{
  ""videoTitle"": ""タイトル"",
  ""chapters"": [{{""title"": ""..."", ""narration"": ""..."", ""imageDescription"": ""...""}}]
}}

新入社員が不安なく業務を開始できるよう、温かみのある文体で。重要なルールは ⚠ で強調。",
                Author = "InsightCast",
                CreatedAt = now, ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_pptx_manual",
                Name = "業務マニュアルスライド",
                Category = "PPTX生成",
                Description = "業務手順書をステップバイステップのスライドに変換",
                SystemPrompt = @"与えられた業務手順書・マニュアルを、ステップバイステップの研修スライドに変換してください。

【変換ルール】
- 各手順を1スライドにする（1スライド = 1アクション）
- 注意事項は ⚠ マークで強調
- よくあるミスは「NG例」として別スライドに

JSON形式で出力:
{{
  ""videoTitle"": ""タイトル"",
  ""chapters"": [{{""title"": ""Step N: 操作名"", ""narration"": ""補足説明"", ""imageDescription"": ""English image prompt""}}]
}}

初めて業務を行う人が1人で完遂できるレベルの具体性を目指してください。",
                Author = "InsightCast",
                CreatedAt = now, ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_pptx_product",
                Name = "製品・サービス紹介プレゼン",
                Category = "PPTX生成",
                Description = "製品情報から営業用プレゼンテーションを生成",
                SystemPrompt = @"与えられた製品・サービス情報から、営業・マーケティング用プレゼンテーションスライドを作成してください。

【構成】
1. 課題提起（顧客が抱える3つの課題）
2. 解決策（製品の概要 — 1文で）
3. 主要機能（3-5つ、各1スライド）
4. 導入効果（数値で示す Before/After）
5. 導入事例（あれば）
6. 競合比較
7. 料金プラン
8. 導入ステップ（3-5ステップ）
9. 次のアクション（CTA）

JSON形式で出力:
{{
  ""videoTitle"": ""タイトル"",
  ""chapters"": [{{""title"": ""..."", ""narration"": ""..."", ""imageDescription"": ""...""}}]
}}

数字を積極的に使い、顧客視点で語る（機能説明ではなく価値提案）。",
                Author = "InsightCast",
                CreatedAt = now, ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_pptx_seminar",
                Name = "セミナー・ウェビナー資料",
                Category = "PPTX生成",
                Description = "セミナーやウェビナーの登壇資料を生成",
                SystemPrompt = @"与えられたテーマ・情報から、セミナー・ウェビナー用プレゼンテーションスライドを作成してください。

【構成】
1. 自己紹介（登壇者プロフィール — 1スライド）
2. 今日のアジェンダ（3-5トピック）
3. 導入（問題提起 or 興味を引く事実・統計）
4. 本編（トピックごとに 3-5 スライド）
5. まとめ（Key Takeaways 3つ）
6. Q&A
7. 次のステップ / CTA / 連絡先

JSON形式で出力:
{{
  ""videoTitle"": ""タイトル"",
  ""chapters"": [{{""title"": ""..."", ""narration"": ""..."", ""imageDescription"": ""...""}}]
}}

5分ごとにインタラクション（質問、挙手）を入れる構成にする。",
                Author = "InsightCast",
                CreatedAt = now, ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_pptx_proposal",
                Name = "提案プレゼンテーション",
                Category = "PPTX生成",
                Description = "企画・提案をプレゼンスライドに構成",
                SystemPrompt = @"与えられた企画・提案内容から、意思決定者向けプレゼンテーションスライドを作成してください。

【構成（ピラミッド原則）】
1. エグゼクティブサマリー（結論を先に）
2. 現状分析（データに基づく課題の可視化）
3. 提案内容（What: 何をするか）
4. 実施方法（How: どう実現するか）
5. 期待効果（Why: ROI・定量効果）
6. スケジュール（When: フェーズ分け）
7. 体制・リソース（Who: 必要な人員・予算）
8. リスクと対策
9. 次のステップ（承認後のアクション）

JSON形式で出力:
{{
  ""videoTitle"": ""タイトル"",
  ""chapters"": [{{""title"": ""..."", ""narration"": ""..."", ""imageDescription"": ""...""}}]
}}

結論→根拠→詳細の順。忙しい役員は3枚目で判断する。",
                Author = "InsightCast",
                CreatedAt = now, ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_pptx_study_group",
                Name = "社内勉強会・LT資料",
                Category = "PPTX生成",
                Description = "技術トピックやナレッジ共有のLT資料を生成",
                SystemPrompt = @"与えられた技術トピック・参考資料から、社内勉強会やLT用スライドを作成してください。

【構成（15-20分想定）】
1. 今日話すこと
2. 背景・なぜこのトピックか
3. 本編（核心を5-8スライドで）
4. やってみた / 検証結果
5. まとめ（3つの Key Takeaways）
6. 参考リンク・資料

JSON形式で出力:
{{
  ""videoTitle"": ""タイトル"",
  ""chapters"": [{{""title"": ""..."", ""narration"": ""..."", ""imageDescription"": ""...""}}]
}}

難しい概念は身近なアナロジーで説明。「明日から使える」実践的な内容を重視。",
                Author = "InsightCast",
                CreatedAt = now, ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_pptx_handover",
                Name = "業務引継ぎ資料",
                Category = "PPTX生成",
                Description = "業務引継ぎに必要な情報をスライドに整理",
                SystemPrompt = @"与えられた業務情報・メモから、業務引継ぎ用スライドを作成してください。

【構成】
1. 業務概要（担当業務の全体像 — 1枚で俯瞰）
2. 定常業務（日次/週次/月次/年次のタスク一覧）
3. 業務フロー
4. 関係者マップ（社内外のキーパーソンと連絡先）
5. 使用ツール・システム
6. ファイル・データの保管場所
7. よくあるトラブルと対処法
8. 注意事項・暗黙知
9. 引継ぎスケジュール（OJT計画）

JSON形式で出力:
{{
  ""videoTitle"": ""タイトル"",
  ""chapters"": [{{""title"": ""..."", ""narration"": ""前任者が口頭で補足すべきポイント"", ""imageDescription"": ""...""}}]
}}

後任者が「このスライドだけで業務を回せる」レベルの具体性を目指す。",
                Author = "InsightCast",
                CreatedAt = now, ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_pptx_compliance",
                Name = "コンプライアンス研修",
                Category = "PPTX生成",
                Description = "コンプライアンス研修スライドを生成",
                SystemPrompt = @"与えられた社内規程・法令情報から、コンプライアンス研修用スライドを作成してください。

【構成】
1. コンプライアンスとは（定義と重要性）
2. 最近の事例（違反事例と影響 — 2-3 件）
3. 当社のルール（社内規程の重要ポイント）
4. ケーススタディ（こんな時どうする？ — 3-5 シナリオ）
5. 情報セキュリティ（SNS投稿、パスワード、メール誤送信）
6. ハラスメント防止（定義、事例、相談窓口）
7. まとめ・チェックリスト
8. 確認テスト（理解度チェック 5 問）

JSON形式で出力:
{{
  ""videoTitle"": ""タイトル"",
  ""chapters"": [{{""title"": ""..."", ""narration"": ""研修講師用の補足説明"", ""imageDescription"": ""...""}}]
}}

受講者が「自分ごと」として考えられるよう、身近なシナリオを使う。",
                Author = "InsightCast",
                CreatedAt = now, ModifiedAt = now,
            },
            new()
            {
                Id = "builtin_pptx_quarterly",
                Name = "四半期レビュー・経営報告",
                Category = "PPTX生成",
                Description = "業績データから四半期レビュー用プレゼンを生成",
                SystemPrompt = @"与えられた業績データ・活動記録から、四半期レビュー用プレゼンテーションを作成してください。

【構成】
1. エグゼクティブサマリー（結論 — 3行で全体像）
2. 主要KPI（目標 vs 実績 — 4-6指標）
3. 売上・利益推移
4. 事業別ハイライト
5. 主要施策の進捗
6. 課題とアクションプラン
7. 次四半期の重点施策

JSON形式で出力:
{{
  ""videoTitle"": ""タイトル"",
  ""chapters"": [{{""title"": ""..."", ""narration"": ""..."", ""imageDescription"": ""...""}}]
}}

経営層が10分で状況を把握し、意思決定できる構成にする。数字は前年比・前期比を必ず併記。",
                Author = "InsightCast",
                CreatedAt = now, ModifiedAt = now,
            },
        ];
    }
}
