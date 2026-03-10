using System.Collections.Generic;
using InsightCommon.AI;

namespace InsightCast.Services.Claude;

/// <summary>
/// InsightCast 固有のプリセットプロンプト
///
/// モデル選定方針:
///   shunsuke (Haiku 3.5)  — パターンベースの単純変換、ルール適用、低コスト大量処理
///   megumi   (Sonnet 4)   — 翻訳、創作文生成、トーン調整、マルチステップタスク
///   manabu   (Opus 4)     — 深い構造分析、教育効果評価、複雑な全自動タスク
///   DALL-E (IsImageMode)  — 画像生成（サムネイル、イラスト）→ OpenAI API
/// </summary>
public static class InsightCastPresetPrompts
{
    public static readonly List<InsightCastPresetPrompt> All = new()
    {
        // ========================================
        // カテゴリ1: 字幕・翻訳
        //   AI がシーンのナレーション/字幕テキストを読み書きできる
        //   翻訳 → Sonnet (言語ニュアンス重要)
        //   字幕変換 → Haiku (単純なテキスト短縮)
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "subtitle_from_narration",
            CategoryJa = "字幕・翻訳",
            CategoryEn = "Subtitles & Translation",
            LabelJa = "字幕をワンクリック生成",
            LabelEn = "One-click subtitle generation",
            Icon = "📝",
            RecommendedPersonaId = "shunsuke",

            RequiresContextData = true,
            PromptJa = "各シーンのナレーションテキストを読み取り、字幕として適切な短いテキストに変換してください。字幕は1行あたり20文字以内が理想です。get_scenesツールでシーン情報を取得し、set_multiple_scenesツールで字幕を設定してください。",
            PromptEn = "Read the narration text from each scene and convert it into short, subtitle-friendly text. Ideally, each subtitle line should be within 40 characters. Use the get_scenes tool to get scene info and set_multiple_scenes to set subtitles.",
        },
        new InsightCastPresetPrompt
        {
            Id = "translate_narration_en",
            CategoryJa = "字幕・翻訳",
            CategoryEn = "Subtitles & Translation",
            LabelJa = "英語版を即座に作成",
            LabelEn = "Instantly create English version",
            Icon = "🌐",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションテキストを取得し、英語に翻訳してください。直訳ではなく、教育動画にふさわしい自然な英語表現にしてください。翻訳結果をset_multiple_scenesツールでナレーションとして上書き設定してください。",
            PromptEn = "Use get_scenes to get all narration text and translate to English. Use natural English expressions appropriate for educational videos, not literal translations. Apply translated narration with set_multiple_scenes.",
        },
        new InsightCastPresetPrompt
        {
            Id = "translate_narration_ja",
            CategoryJa = "字幕・翻訳",
            CategoryEn = "Subtitles & Translation",
            LabelJa = "日本語版を即座に作成",
            LabelEn = "Instantly create Japanese version",
            Icon = "🇯🇵",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションテキストを取得し、日本語に翻訳してください。教育動画にふさわしい分かりやすい日本語にしてください。翻訳結果をset_multiple_scenesツールでナレーションとして上書き設定してください。",
            PromptEn = "Use get_scenes to get all narration text and translate to Japanese. Use clear, easy-to-understand Japanese appropriate for educational videos. Apply translated narration with set_multiple_scenes.",
        },

        new InsightCastPresetPrompt
        {
            Id = "translate_narration_zh",
            CategoryJa = "字幕・翻訳",
            CategoryEn = "Subtitles & Translation",
            LabelJa = "中国語圏に展開（字幕追加）",
            LabelEn = "Expand to Chinese market (add subtitles)",
            Icon = "🇨🇳",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションテキストを取得し、簡体字中国語に翻訳してください。教育動画にふさわしい自然な中国語表現にしてください。翻訳結果をset_multiple_scenesツールで字幕として設定してください（ナレーションは変更しない）。",
            PromptEn = "Use get_scenes to get all narration text and translate to Simplified Chinese. Use natural Chinese expressions appropriate for educational videos. Apply translations as subtitles (not narration) with set_multiple_scenes.",
        },
        new InsightCastPresetPrompt
        {
            Id = "translate_narration_ko",
            CategoryJa = "字幕・翻訳",
            CategoryEn = "Subtitles & Translation",
            LabelJa = "韓国語圏に展開（字幕追加）",
            LabelEn = "Expand to Korean market (add subtitles)",
            Icon = "🇰🇷",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションテキストを取得し、韓国語に翻訳してください。教育動画にふさわしい自然な韓国語表現にしてください。翻訳結果をset_multiple_scenesツールで字幕として設定してください（ナレーションは変更しない）。",
            PromptEn = "Use get_scenes to get all narration text and translate to Korean. Use natural Korean expressions appropriate for educational videos. Apply translations as subtitles (not narration) with set_multiple_scenes.",
        },

        // ========================================
        // カテゴリ2: ナレーション生成
        //   AI がスライドノート・シーンタイトルを読んでナレーション生成
        //   校正・新規作成 → Sonnet
        //   ノート変換 → Sonnet
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "narration_from_notes",
            CategoryJa = "ナレーション生成",
            CategoryEn = "Narration",
            LabelJa = "スライドから即プロナレーション",
            LabelEn = "Instant pro narration from slides",
            Icon = "🎙️",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_pptx_notesツールでスライドのノート（スピーカーノート）を取得し、各シーンのナレーションに変換してください。箇条書きのノートを自然な話し言葉に変換し、set_multiple_scenesツールで設定してください。1シーンあたり100〜200文字を目安にしてください。ノートが空のシーンはシーンタイトルから内容を推測してナレーションを作成してください。",
            PromptEn = "Use get_pptx_notes to get slide speaker notes and convert them into narration for each scene. Transform bullet-point notes into natural spoken language, aiming for 50-100 words per scene. Use set_multiple_scenes to apply. For scenes without notes, infer content from the scene title.",
        },
        new InsightCastPresetPrompt
        {
            Id = "improve_narration",
            CategoryJa = "ナレーション生成",
            CategoryEn = "Narration",
            LabelJa = "伝わるナレーションに磨き上げ",
            LabelEn = "Polish narration for maximum impact",
            Icon = "✨",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを読み取り、文法の修正、表現の改善、分かりやすさの向上を行ってください。大幅な変更は避け、自然な改善にとどめてください。改善したテキストをset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to read all narration text, then proofread for grammar, improve expressions, and enhance clarity. Avoid major changes — keep improvements natural. Apply changes with set_multiple_scenes.",
        },
        new InsightCastPresetPrompt
        {
            Id = "create_narration",
            CategoryJa = "ナレーション生成",
            CategoryEn = "Narration",
            LabelJa = "タイトルだけでナレーション完成",
            LabelEn = "Complete narration from titles alone",
            Icon = "📖",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのタイトルと既存テキストを取得し、各シーンに適したナレーションを新規作成してください。導入→本題→まとめの流れを意識し、1シーンあたり100〜150文字の自然な話し言葉にしてください。set_multiple_scenesで設定してください。\n\n※AIはシーンのタイトル・ナレーション・字幕のテキスト情報のみ参照できます。画像や動画の内容は参照できません。",
            PromptEn = "Use get_scenes to get all scene titles and existing text, then create new narration for each scene. Follow an intro → body → summary flow, aiming for 50-80 words per scene in natural spoken language. Apply with set_multiple_scenes.\n\nNote: AI can only read scene titles, narration, and subtitle text. It cannot see images or video content.",
        },

        // ========================================
        // カテゴリ3: 構成・レビュー
        //   テキスト内容に基づく構成分析 → Opus
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "check_structure",
            CategoryJa = "構成・レビュー",
            CategoryEn = "Structure Review",
            LabelJa = "視聴完了率を上げる構成診断",
            LabelEn = "Optimize structure for viewer retention",
            Icon = "🔍",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールとget_project_summaryツールでプロジェクト情報を取得し、ナレーションの構成を分析してアドバイスしてください:\n\n1. シーン間のナレーションの流れと論理構造\n2. 各シーンのナレーション量のバランス（長すぎ・短すぎ）\n3. 導入→本題→まとめの構成が適切か\n4. ナレーションが空のシーン、メディアが未設定のシーンの指摘\n5. 具体的な改善提案（シーンの追加・削除・並べ替えが必要な場合は、add_scene / remove_scene / move_scene ツールで対応可能であることも伝える）\n\n※テキスト情報とメディアパスの有無のみ分析できます。画像・動画の内容は参照できません。",
            PromptEn = "Use get_scenes and get_project_summary to analyze the narration structure and provide advice:\n\n1. Flow and logical structure between scene narrations\n2. Balance of narration length (too long/short)\n3. Whether intro → body → summary structure is appropriate\n4. Flag scenes with empty narration or missing media\n5. Specific improvement suggestions (mention that add_scene / remove_scene / move_scene tools are available if structural changes are needed)\n\nNote: Only text data and media path presence can be analyzed. Image/video content cannot be reviewed.",
        },
        new InsightCastPresetPrompt
        {
            Id = "check_completeness",
            CategoryJa = "構成・レビュー",
            CategoryEn = "Structure Review",
            LabelJa = "公開前の品質チェック",
            LabelEn = "Pre-publish quality check",
            Icon = "✅",
            RecommendedPersonaId = "shunsuke",

            RequiresContextData = true,
            PromptJa = "get_scenesツールとget_project_summaryツールで全シーンを確認し、以下の抜け漏れをチェックしてリスト化してください:\n\n- ナレーションが空のシーン\n- 字幕が空のシーン（ナレーションはあるのに字幕がない）\n- メディア（画像/動画）が未設定のシーン\n- ナレーションが極端に短いシーン（30文字未満）\n- ナレーションが極端に長いシーン（300文字超）\n\nシーン番号と現状を表形式で示してください。",
            PromptEn = "Use get_scenes and get_project_summary to check all scenes and list any gaps:\n\n- Scenes with empty narration\n- Scenes with narration but no subtitles\n- Scenes with no media (image/video) set\n- Scenes with very short narration (under 30 chars)\n- Scenes with very long narration (over 300 chars)\n\nList specific scene numbers and their current state in a table format.",
        },

        // ========================================
        // カテゴリ4: トーン調整
        //   全て Sonnet (言語的ニュアンス・文体制御が重要)
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "tone_concise",
            CategoryJa = "トーン調整",
            CategoryEn = "Tone Adjustment",
            LabelJa = "ムダを削ぎ落として時短",
            LabelEn = "Cut the fluff, save viewer time",
            Icon = "✂️",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを取得し、より簡潔に書き直してください:\n\n- 冗長な表現や繰り返しを削除\n- 一文を短くし、要点を先に伝える\n- 「〜ということになります」→「〜です」等の圧縮\n- 各シーンのナレーションを現在の70〜80%の文字数に圧縮\n- 重要な情報は削らず、表現のみ簡潔にする\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to get all narration and rewrite it more concisely:\n\n- Remove redundant expressions and repetition\n- Shorten sentences, lead with key points\n- Compress to 70-80% of current word count per scene\n- Preserve important information, only simplify expression\n\nApply with set_multiple_scenes.",
        },
        new InsightCastPresetPrompt
        {
            Id = "tone_formal",
            CategoryJa = "トーン調整",
            CategoryEn = "Tone Adjustment",
            LabelJa = "経営層・社外向けトーンに格上げ",
            LabelEn = "Elevate tone for executives & clients",
            Icon = "👔",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを取得し、よりフォーマルで丁寧なトーンに調整してください:\n\n- 敬語（です・ます調）を徹底\n- 「〜してね」→「〜してください」等のカジュアル表現を修正\n- ビジネスにふさわしい語彙を使用\n- 社外向けプレゼン・経営層向け報告にも使えるトーンに\n- 過度に硬くならないよう、分かりやすさは維持\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to get all narration and adjust to a more formal, polite tone:\n\n- Use consistently professional language\n- Replace casual expressions with formal equivalents\n- Use business-appropriate vocabulary\n- Suitable for executive presentations and external audiences\n- Maintain clarity — don't make it overly stiff\n\nApply with set_multiple_scenes.",
        },
        new InsightCastPresetPrompt
        {
            Id = "tone_casual",
            CategoryJa = "トーン調整",
            CategoryEn = "Tone Adjustment",
            LabelJa = "親しみやすく視聴者との距離を縮める",
            LabelEn = "Build rapport with a friendly tone",
            Icon = "😊",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを取得し、カジュアルで親しみやすいトーンに調整してください:\n\n- 「〜ですよね」「〜しましょう」等の親しみやすい表現を使用\n- 視聴者に語りかけるような口調（「皆さん」「一緒に〜」）\n- 堅い専門用語をやさしい言葉に置き換え\n- YouTube動画やSNSコンテンツにふさわしいトーン\n- 敬語は維持しつつ、距離感を縮める\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to get all narration and adjust to a casual, friendly tone:\n\n- Use approachable expressions and conversational style\n- Address viewers directly ('you', 'let's')\n- Replace jargon with simpler words\n- Suitable for YouTube and social media content\n- Stay respectful while being warm and engaging\n\nApply with set_multiple_scenes.",
        },

        // ========================================
        // カテゴリ5: 品質チェック
        //   発音ヒント → Haiku (ルールベースの機械的変換)
        //   長さ均一化・文体統一 → Sonnet (全シーン横断分析+リライト)
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "normalize_length",
            CategoryJa = "品質チェック",
            CategoryEn = "Quality Check",
            LabelJa = "テンポを整えて視聴体験を向上",
            LabelEn = "Balance pacing for better viewing",
            Icon = "⚖️",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを取得し、長さのバラつきを分析してください。極端に短い・長いシーンを特定し、全体のバランスが取れるよう調整してください:\n\n- 目標: 各シーン80〜150文字程度\n- 短すぎるシーン: 補足説明を追加\n- 長すぎるシーン: 要点を絞って簡潔に\n- 内容の意味は変えない\n\nset_multiple_scenesで調整結果を設定してください。",
            PromptEn = "Use get_scenes to analyze narration length variation across scenes. Identify scenes that are too short or too long, then balance them:\n\n- Target: 40-80 words per scene\n- Too short: Add supporting detail\n- Too long: Condense to key points\n- Preserve original meaning\n\nApply with set_multiple_scenes.",
        },
        new InsightCastPresetPrompt
        {
            Id = "add_furigana_hints",
            CategoryJa = "品質チェック",
            CategoryEn = "Quality Check",
            LabelJa = "音声読み上げの品質を向上",
            LabelEn = "Enhance text-to-speech quality",
            Icon = "🔤",
            RecommendedPersonaId = "shunsuke",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを確認し、VOICEVOX音声合成エンジンが正しく読み上げられるよう発音補正してください:\n\n- 英語略語をカタカナに変換（「API」→「エーピーアイ」「IT」→「アイティー」等）\n- 数字の読み方を明確化（「10個」→「じゅっこ」等、必要に応じてひらがなに）\n- 難読固有名詞にひらがな表記を検討\n- 記号を読み上げ可能な表現に変換（「→」→「から」等）\n- 句読点の位置を読みやすさの観点で調整\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to review all narration and optimize for VOICEVOX text-to-speech engine:\n\n- Convert abbreviations to phonetic form (e.g., 'API' → 'A-P-I')\n- Clarify number readings where ambiguous\n- Add phonetic readings for difficult proper nouns\n- Replace symbols with speakable expressions ('→' → 'to')\n- Adjust punctuation for natural reading pace\n\nApply with set_multiple_scenes.",
        },
        new InsightCastPresetPrompt
        {
            Id = "tone_consistency",
            CategoryJa = "品質チェック",
            CategoryEn = "Quality Check",
            LabelJa = "ブランドの一貫性を確保",
            LabelEn = "Ensure brand consistency",
            Icon = "🎯",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを分析し、トーンと文体を統一してください:\n\n- 敬体（です・ます調）か常体（だ・である調）を統一\n- 主語の使い方を統一（「私たち」「当社」等）\n- 接続詞の使い方を統一\n- 文末表現のバリエーションを適度に維持\n- 全体を通して一貫した印象になるよう調整\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to analyze all narration and unify the tone and writing style:\n\n- Standardize formality level across scenes\n- Unify subject references ('we', 'our company', etc.)\n- Standardize conjunction usage\n- Maintain appropriate variation in sentence endings\n- Ensure a consistent impression throughout\n\nApply with set_multiple_scenes.",
        },

        // ========================================
        // カテゴリ6: 全自動
        //   複雑なマルチステップ → Opus
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "oneclick_full_video",
            CategoryJa = "全自動",
            CategoryEn = "Full Auto",
            LabelJa = "ワンクリックで動画コンテンツ完成",
            LabelEn = "One-click video content creation",
            Icon = "🚀",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "全シーンのナレーションと字幕を一括で生成してください:\n\n1. get_pptx_notesでスライドのスピーカーノートを取得\n2. get_scenesで全シーンのタイトルと現状を確認\n3. シーン数が内容に対して不足している場合は、add_sceneで必要なシーンを追加\n4. ノートの内容をもとに、各シーンの教育的なナレーションを生成（100〜150文字/シーン）\n5. ナレーションから字幕用の短縮テキストを生成（20文字以内/シーン）\n6. set_multiple_scenesで全シーンのナレーションと字幕を一括設定（1回のツール呼び出しでまとめて設定すること）\n\nノートがないシーンは、シーンのタイトルから内容を推測してナレーションを作成してください。自然な話し言葉で、導入→本題→まとめの流れを意識してください。",
            PromptEn = "Auto-generate narration and subtitles for all scenes:\n\n1. Use get_pptx_notes to get slide speaker notes\n2. Use get_scenes to check current state and titles\n3. If the scene count is insufficient for the content, add scenes with add_scene\n4. Generate educational narration for each scene based on notes (50-80 words/scene)\n5. Create shortened subtitle text from narration (under 40 chars/scene)\n6. Use set_multiple_scenes to apply ALL narration and subtitles in a single tool call\n\nFor scenes without notes, infer content from the scene title. Use natural spoken language with an intro → body → summary flow.",
        },
        new InsightCastPresetPrompt
        {
            Id = "oneclick_bilingual",
            CategoryJa = "全自動",
            CategoryEn = "Full Auto",
            LabelJa = "日英バイリンガル動画を一発作成",
            LabelEn = "Create bilingual JP/EN video instantly",
            Icon = "🌏",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "日本語ナレーション＋英語字幕を一括生成してください:\n\n1. get_scenesで全シーンのタイトルと現状を確認、get_pptx_notesでノートも取得\n2. ノートやタイトルをもとに各シーンの日本語ナレーションを生成（100〜150文字/シーン）\n3. そのナレーションを自然な英語に翻訳し、字幕として設定\n4. set_multiple_scenesで全シーンに日本語ナレーション＋英語字幕を一括設定\n\n日本語ナレーションは自然な話し言葉、英語字幕は直訳ではなく自然な表現にしてください。",
            PromptEn = "Auto-generate JP narration + EN subtitles:\n\n1. Use get_scenes and get_pptx_notes to gather info\n2. Generate Japanese narration for each scene from notes/titles (100-150 chars/scene)\n3. Translate narration to natural English for subtitles\n4. Use set_multiple_scenes to apply JP narration + EN subtitles\n\nJapanese narration should be natural spoken language. English subtitles should be natural, not literal translations.",
        },

        // ========================================
        // カテゴリ7: サムネイル・CTR最適化
        //   CTR予測・サムネイル改善提案 → Opus
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "thumbnail_ctr_predict",
            CategoryJa = "サムネイル・CTR最適化",
            CategoryEn = "Thumbnail & CTR",
            LabelJa = "クリック率を最大化するサムネ診断",
            LabelEn = "Maximize click-through with thumbnail audit",
            Icon = "📊",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"get_scenesとget_project_summaryでプロジェクト情報を取得し、教育動画サムネイルの効果を分析してください。

以下の観点でCTR（クリック率）予測スコアを100点満点で評価し、改善案を提示してください:

1. **テキストの訴求力** (25点): メインテキストが「自分ごと化」できるか、具体的な数字・ベネフィットがあるか
2. **視認性** (25点): 文字数は10文字以内か、フォントサイズは十分か、コントラストは明確か
3. **パターン適合性** (25点): 選択されたパターン・スタイルがコンテンツに合っているか
4. **教育動画としての信頼感** (25点): プロフェッショナルな印象か、クリックベイトになっていないか

最後に改善したサムネイルを generate_thumbnail ツールで生成してください（メインテキストとサブテキストの改善案を反映）。",
            PromptEn = @"Use get_scenes and get_project_summary to analyze the project, then evaluate the educational video thumbnail effectiveness.

Score the predicted CTR (click-through rate) out of 100 points across these dimensions:

1. **Text Appeal** (25pts): Does the main text create personal relevance? Are there specific numbers or benefits?
2. **Visibility** (25pts): Is text under 10 chars? Is font size sufficient? Is contrast clear?
3. **Pattern Fit** (25pts): Does the chosen pattern/style match the content?
4. **Educational Trust** (25pts): Does it look professional? Is it not clickbait?

Finally, generate an improved thumbnail using the generate_thumbnail tool with optimized main text and sub text.",
        },

        // ========================================
        // カテゴリ8: 教育構成チェック
        //   チャプター構造・学習効果分析 → Opus
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "chapter_structure_review",
            CategoryJa = "教育構成チェック",
            CategoryEn = "Education Review",
            LabelJa = "学習効果を最大化する構成診断",
            LabelEn = "Maximize learning impact with structure review",
            Icon = "🎓",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"get_scenesとget_project_summaryでプロジェクト全体を分析し、教育動画としての構成を詳細にレビューしてください。

以下の観点で評価し、具体的な改善提案をしてください:

## チャプター構造の評価
1. **導入部**: 学習目標が明確に提示されているか、視聴者の動機づけがあるか
2. **本論部**: 論理的な順序で進行しているか、各シーンの情報量は適切か
3. **まとめ部**: 学習内容の要約があるか、次のアクションが示されているか
4. **チャプター名**: YouTube のチャプター表示に適した名前になっているか

## 学習効果の評価
- 1つのシーンに詰め込みすぎていないか（認知負荷）
- 具体例や事例が含まれているか
- 視聴者への問いかけや考える時間があるか
- 専門用語の説明が十分か

## 改善が必要な場合
- add_scene / remove_scene / move_scene で構造変更が可能であることを伝える
- 具体的なシーン分割・統合・並べ替えの提案をする",
            PromptEn = @"Analyze the full project with get_scenes and get_project_summary, then provide a detailed review of the educational video structure.

Evaluate and provide specific improvement suggestions:

## Chapter Structure
1. **Introduction**: Are learning objectives clearly stated? Is there viewer motivation?
2. **Main Content**: Is the progression logical? Is information density appropriate per scene?
3. **Summary**: Is there a recap of key learnings? Are next actions indicated?
4. **Chapter Names**: Are they suitable for YouTube chapter display?

## Learning Effectiveness
- Is any single scene overloaded with information (cognitive load)?
- Are concrete examples or case studies included?
- Are there questions or thinking pauses for viewers?
- Are technical terms adequately explained?

## If Changes Are Needed
- Mention that add_scene / remove_scene / move_scene tools are available
- Provide specific suggestions for scene splitting, merging, or reordering",
        },

        // ========================================
        // カテゴリ9: シーン操作
        //   シーン追加・削除・並べ替え → Opus (複雑な構造変更)
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "auto_compose_scenes",
            CategoryJa = "シーン操作",
            CategoryEn = "Scene Management",
            LabelJa = "企画からシーン構成を自動生成",
            LabelEn = "Auto-build scene structure from your plan",
            Icon = "🏗️",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "以下の手順でシーン構成を自動生成してください:\n\n1. get_scenesとget_pptx_notesで現在のシーン構成とノートを確認\n2. 導入→本題→まとめの構成に必要なシーンを分析し、不足分をadd_sceneで追加（末尾に追加でOK）\n3. 必要に応じてmove_sceneでシーン順序を最適化\n4. 全シーンのナレーションをまとめてset_multiple_scenesで一括設定（個別のset_scene_narrationではなく、必ず一括で設定すること）\n5. 結果を報告\n\n重要: ツール呼び出し回数を節約するため、ナレーション設定は必ずset_multiple_scenesで一度にまとめて行ってください。",
            PromptEn = "Auto-compose the scene structure:\n\n1. Use get_scenes and get_pptx_notes to check current structure and notes\n2. Analyze what scenes are needed for an intro → body → summary flow, add missing ones with add_scene (append at end is fine)\n3. Optimize scene order with move_scene if needed\n4. Set narration for ALL scenes at once using set_multiple_scenes (do NOT use individual set_scene_narration calls)\n5. Report the results\n\nImportant: To conserve tool call budget, always batch narration updates into a single set_multiple_scenes call.",
        },
        new InsightCastPresetPrompt
        {
            Id = "cleanup_scenes",
            CategoryJa = "シーン操作",
            CategoryEn = "Scene Management",
            LabelJa = "不要シーンを自動整理＋最適化",
            LabelEn = "Auto-cleanup & optimize scene flow",
            Icon = "🧹",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "シーンの整理を行ってください:\n\n1. get_scenesで全シーンを確認\n2. ナレーション・字幕・メディアがすべて空のシーンを特定し、一覧を報告\n3. 空シーンをremove_sceneで削除（インデックスが大きい方から順に削除すること。最低1シーンは残す）\n4. 削除後、get_scenesで最新状態を取得し、残ったシーンの順序を分析\n5. 論理的な流れになるようmove_sceneで並べ替えを実行\n6. 最終構成を報告\n\n重要: シーン削除時はインデックスのずれを防ぐため、必ず末尾側（大きいインデックス）から順に削除してください。",
            PromptEn = "Clean up the scene structure:\n\n1. Use get_scenes to review all scenes\n2. Identify scenes where narration, subtitles, and media are all empty, and report the list\n3. Remove empty scenes with remove_scene (delete from highest index first to avoid index shifting; keep at least 1 scene)\n4. After deletion, use get_scenes to get the updated state and analyze remaining scene order\n5. Reorder scenes with move_scene for logical flow\n6. Report the final structure\n\nImportant: Always delete scenes from highest index to lowest to prevent index shifting issues.",
        },
        // ========================================
        // カテゴリ10: 研修コンテンツ設計
        //   教育設計・学習目標整理 → Opus
        //   クイズ生成 → Sonnet
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "training_learning_objectives",
            CategoryJa = "研修コンテンツ設計",
            CategoryEn = "Training Design",
            LabelJa = "学習目標を自動設定＆導入シーン生成",
            LabelEn = "Auto-set learning objectives & intro scene",
            Icon = "🎯",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesとget_pptx_notesでプロジェクト内容を把握し、研修動画としての学習目標を設計してください:\n\n1. コンテンツ全体から3〜5個の学習目標を抽出\n2. 先頭に「学習目標」シーンをadd_sceneで追加し、目標を箇条書きナレーションとして設定\n3. 末尾に「まとめ・振り返り」シーンをadd_sceneで追加し、学習目標の達成を確認するナレーションを設定\n4. set_multiple_scenesでナレーションを一括設定\n\n学習目標は「〜できるようになる」の形式で、具体的・測定可能な表現にしてください。",
            PromptEn = "Analyze the project with get_scenes and get_pptx_notes, then design learning objectives for a training video:\n\n1. Extract 3-5 learning objectives from the content\n2. Add a 'Learning Objectives' scene at the beginning with add_scene, set objectives as bullet-point narration\n3. Add a 'Summary & Review' scene at the end with add_scene, set narration confirming objective achievement\n4. Apply all narration with set_multiple_scenes\n\nLearning objectives should be specific and measurable ('By the end, you will be able to...').",
        },
        new InsightCastPresetPrompt
        {
            Id = "training_quiz_scenes",
            CategoryJa = "研修コンテンツ設計",
            CategoryEn = "Training Design",
            LabelJa = "理解度チェッククイズを自動挿入",
            LabelEn = "Auto-insert comprehension quiz scenes",
            Icon = "❓",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesで全シーンのナレーションを読み取り、内容に基づいた理解度チェッククイズを作成してください:\n\n1. 主要な学習ポイントを3〜5個特定\n2. 各ポイントの直後にadd_sceneでクイズシーンを挿入\n3. クイズのナレーション形式: 「ここで確認クイズです。[質問]　答えは…[回答と解説]」\n4. set_multiple_scenesで全クイズシーンのナレーションを一括設定\n\nクイズは選択式（A/B/C）または○×形式で、正解と簡潔な解説を含めてください。",
            PromptEn = "Read all scene narration with get_scenes and create comprehension quiz questions:\n\n1. Identify 3-5 key learning points\n2. Insert quiz scenes after each key point with add_scene\n3. Quiz narration format: 'Quick check: [question] The answer is... [answer and explanation]'\n4. Apply all quiz narration with set_multiple_scenes\n\nUse multiple choice (A/B/C) or true/false format with correct answers and brief explanations.",
        },
        new InsightCastPresetPrompt
        {
            Id = "training_time_estimate",
            CategoryJa = "研修コンテンツ設計",
            CategoryEn = "Training Design",
            LabelJa = "研修時間の見積もり＆最適化提案",
            LabelEn = "Estimate training duration & optimize",
            Icon = "⏱️",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesとget_project_summaryでプロジェクト全体を分析し、研修動画としての時間配分を評価してください:\n\n1. 各シーンのナレーション文字数から読み上げ時間を推定（日本語: 約300文字/分）\n2. シーンごとの推定時間を表形式で一覧化\n3. 動画全体の合計時間を算出\n4. 研修動画の推奨時間（5〜15分）に対する過不足を指摘\n5. 長すぎる場合は分割案、短すぎる場合は補足すべき内容を提案\n\n視聴者の集中力維持の観点からもアドバイスしてください。",
            PromptEn = "Analyze the full project with get_scenes and get_project_summary to evaluate time allocation:\n\n1. Estimate reading time per scene from narration character count (English: ~150 words/min)\n2. List estimated time per scene in table format\n3. Calculate total video duration\n4. Flag if duration falls outside recommended training range (5-15 min)\n5. Suggest splitting if too long, or additional content if too short\n\nAlso advise from a viewer attention/retention perspective.",
        },

        // ========================================
        // カテゴリ11: アクセシビリティ
        //   読みやすさ改善 → Sonnet
        //   多言語字幕 → Sonnet
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "accessibility_plain_language",
            CategoryJa = "アクセシビリティ",
            CategoryEn = "Accessibility",
            LabelJa = "誰でもわかる平易な表現に書き換え",
            LabelEn = "Rewrite in plain language for all audiences",
            Icon = "📖",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesで全シーンのナレーションを取得し、より平易でわかりやすい表現に書き換えてください:\n\n- 専門用語は初出時に「（＝○○のこと）」と補足説明を追加\n- 長い文は短い文に分割（1文40文字以内を目標）\n- カタカナ語は必要最低限にし、日本語で言い換え可能なものは置き換え\n- 抽象的な説明には具体例を追加\n- 新入社員や非専門家が理解できるレベルを目標\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to get all narration and rewrite for maximum accessibility:\n\n- Add brief explanations for technical terms on first use\n- Split long sentences (target: under 20 words per sentence)\n- Minimize jargon; use plain equivalents where possible\n- Add concrete examples for abstract explanations\n- Target comprehension level: new employees or non-specialists\n\nApply with set_multiple_scenes.",
        },
        new InsightCastPresetPrompt
        {
            Id = "accessibility_audio_description",
            CategoryJa = "アクセシビリティ",
            CategoryEn = "Accessibility",
            LabelJa = "画面の説明をナレーションに追加",
            LabelEn = "Add visual descriptions to narration",
            Icon = "👁️",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = "get_scenesで全シーンを確認し、音声だけで内容が伝わるようナレーションを補強してください:\n\n- 「この画面では」「ここに表示されている」等の指示語を具体的な説明に置き換え\n- グラフや図表を参照する場面では、数値や傾向を口頭で説明するテキストを追加\n- 「ご覧のように」→ 具体的に何が表示されているかを説明\n- 画面を見なくても内容が理解できるレベルを目標\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to review all scenes and enhance narration so content is understandable by audio alone:\n\n- Replace vague references ('as shown here') with specific descriptions\n- Add verbal descriptions of data, trends, and visuals\n- Ensure charts/graphs are described with key numbers and patterns\n- Target: full comprehension without viewing the screen\n\nApply with set_multiple_scenes.",
        },

        // ========================================
        // カテゴリ12: AIガイド
        //   AIにできることを提案 → Haiku (軽量)
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "ai_capabilities_guide",
            CategoryJa = "AIガイド",
            CategoryEn = "AI Guide",
            LabelJa = "この動画でAIにできることを提案",
            LabelEn = "Suggest what AI can do for this video",
            Icon = "💡",
            RecommendedPersonaId = "shunsuke",

            RequiresContextData = true,
            PromptJa = @"あなたはInsight Training StudioのAIアシスタントです。
get_scenesとget_project_summaryでこのプロジェクトの現状を確認した上で、
ユーザーが「〜してください」と指示すればすぐ実行できるアクションを具体的に提案してください。

あなたが使えるツール:
- get_scenes: 全シーンのタイトル・ナレーション・字幕・メディアパスを取得
- set_multiple_scenes: 複数シーンのナレーション・字幕を一括設定
- set_scene_narration / set_scene_subtitle: 個別シーンのナレーション・字幕を設定
- get_pptx_notes: スライドのスピーカーノートを取得
- get_project_summary: プロジェクト全体の概要（シーン数、動画長等）を取得
- add_scene / remove_scene / move_scene: シーンの追加・削除・並べ替え
- set_scene_media: シーンの画像・動画を設定
- generate_thumbnail: サムネイル画像を生成
- generate_scene_image: AIでシーン用イラストを生成
- generate_ab_thumbnails: A/Bテスト用サムネイル2枚を生成
- add_cta_endcard: 最終シーンにCTA（行動喚起）を追加

プロジェクトの現状を踏まえて、以下の観点で「今すぐできること」を5〜10個提案してください:
- ナレーション・字幕の生成・改善・翻訳
- 構成の改善（シーン追加・削除・並べ替え）
- 品質チェック（トーン統一、長さ均一化、VOICEVOX最適化）
- サムネイル・CTR最適化
- 多言語展開（英語・中国語・韓国語字幕）

各提案には「なぜそれが有用か」を1行で添えてください。",
            PromptEn = @"You are the Insight Training Studio AI assistant.
Use get_scenes and get_project_summary to review the current project state,
then suggest specific actions the user can request immediately.

Your available tools:
- get_scenes: Get all scene titles, narration, subtitles, and media paths
- set_multiple_scenes: Batch-set narration and subtitles for multiple scenes
- set_scene_narration / set_scene_subtitle: Set individual scene narration/subtitles
- get_pptx_notes: Get slide speaker notes
- get_project_summary: Get project overview (scene count, video length, etc.)
- add_scene / remove_scene / move_scene: Add, remove, or reorder scenes
- set_scene_media: Set scene image/video
- generate_thumbnail: Generate a thumbnail image
- generate_scene_image: Generate AI illustration for a scene
- generate_ab_thumbnails: Generate 2 A/B test thumbnails
- add_cta_endcard: Add a call-to-action end card

Based on the current project state, suggest 5-10 actionable items across:
- Narration/subtitle generation, improvement, or translation
- Structure improvements (add/remove/reorder scenes)
- Quality checks (tone consistency, length normalization, TTS optimization)
- Thumbnail & CTR optimization
- Multi-language expansion (EN/ZH/KO subtitles)

Add a one-line reason why each suggestion would be valuable.",
        },

        // ========================================
        // カテゴリ13: マーケティング・YouTube
        //   動画の集客力・視聴維持率を最大化するためのプロンプト
        //   SEO/構成分析 → Opus (深い分析)
        //   テキスト生成 → Sonnet (創作力)
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "youtube_seo_optimize",
            CategoryJa = "マーケティング・YouTube",
            CategoryEn = "Marketing & YouTube",
            LabelJa = "YouTube SEO最適化（タイトル・説明・タグ）",
            LabelEn = "YouTube SEO optimization (title/desc/tags)",
            Icon = "🔍",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"get_project_summary と get_scenes でプロジェクト内容を確認し、YouTube向けに以下を生成してください:

1. タイトル案（3パターン）
   - 検索キーワードを含む（60文字以内）
   - クリックしたくなる表現（数字・疑問形・ベネフィット訴求）
2. 説明文（5,000文字以内）
   - 冒頭3行にキーワードと要約（折りたたみ前に表示される部分）
   - タイムスタンプ付き目次
   - 関連キーワードを自然に含める
   - CTA（チャンネル登録・関連動画リンク用プレースホルダー）
3. タグ（15〜30個）
   - メインキーワード + ロングテール + 関連語
4. ハッシュタグ（3個）
   - タイトル上に表示される#タグ

業界・ターゲット層に適した検索ボリュームの高いキーワードを意識してください。",
            PromptEn = @"Use get_project_summary and get_scenes to review the project, then generate YouTube SEO elements:

1. Title options (3 patterns, under 60 chars each)
   - Include search keywords, use numbers/questions/benefit-driven copy
2. Description (under 5,000 chars)
   - First 3 lines: keywords + summary (visible before 'Show more')
   - Timestamped table of contents
   - Natural keyword integration
   - CTAs (subscribe, related video placeholders)
3. Tags (15-30)
   - Main keywords + long-tail + related terms
4. Hashtags (3)

Focus on high-volume keywords relevant to the industry and target audience.",
        },
        new InsightCastPresetPrompt
        {
            Id = "shorts_repurpose",
            CategoryJa = "マーケティング・YouTube",
            CategoryEn = "Marketing & YouTube",
            LabelJa = "Shorts用に切り出しポイントを提案",
            LabelEn = "Suggest Shorts clip points",
            Icon = "📱",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"get_scenes でシーン構成を確認し、YouTube Shorts（60秒以内の縦型動画）に切り出せるポイントを提案してください。

各提案について:
- 対象シーン番号と時間範囲
- Shorts用タイトル（衝撃的・好奇心を煽る短いコピー）
- 冒頭フック（最初の3秒で視聴者を掴む一言）
- 縦型動画に適したテロップテキスト案
- 期待されるエンゲージメント（コメント誘発要素）

最低3本分のShorts案を出してください。バイラル性の高い順に並べてください。",
            PromptEn = @"Use get_scenes to review the structure and suggest YouTube Shorts (under 60 sec, vertical) extraction points.

For each suggestion:
- Target scene numbers and time range
- Shorts title (attention-grabbing short copy)
- Opening hook (first 3 seconds to capture viewers)
- Vertical video caption text
- Expected engagement (comment-provoking elements)

Suggest at least 3 Shorts ideas, ordered by viral potential.",
        },
        new InsightCastPresetPrompt
        {
            Id = "engagement_hook",
            CategoryJa = "マーケティング・YouTube",
            CategoryEn = "Marketing & YouTube",
            LabelJa = "視聴維持率を上げるフック挿入",
            LabelEn = "Add retention hooks",
            Icon = "🎯",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"get_scenes で全シーンのナレーションを確認し、視聴維持率を向上させるフックを各シーンに追加してください。

フックの種類:
- オープンループ（「この後、驚くべき結果が...」）
- 予告（「3つ目のポイントが最も重要です」）
- 疑問提起（「なぜこれが効果的なのでしょうか？」）
- データ引用（「実は〇〇%の企業が...」）

set_multiple_scenes でナレーションを更新し、各シーン冒頭または末尾にフックを自然に織り込んでください。
フックは視聴者が「続きが気になる」と感じるものにしてください。",
            PromptEn = @"Use get_scenes to review all narration and add retention hooks to each scene using set_multiple_scenes.

Hook types:
- Open loops ('What happens next will surprise you...')
- Preview ('The third point is the most important')
- Questions ('Why is this so effective?')
- Data citations ('Actually, X% of companies...')

Weave hooks naturally into the start or end of each scene's narration to keep viewers watching.",
        },
        new InsightCastPresetPrompt
        {
            Id = "video_series_plan",
            CategoryJa = "マーケティング・YouTube",
            CategoryEn = "Marketing & YouTube",
            LabelJa = "シリーズ動画の企画立案",
            LabelEn = "Plan a video series",
            Icon = "📋",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"get_project_summary でこの動画の内容を確認し、これを起点としたシリーズ動画（5〜10本）の企画を立案してください。

各回について:
1. タイトル案
2. 主要テーマ・学習目標
3. 推奨尺（分数）
4. 前回との関連性・伏線
5. CTA（次の動画への誘導文）

全体構成:
- シリーズ全体のコンセプト・タイトル
- ターゲット視聴者ペルソナ
- 配信スケジュール案（週1回推奨）
- 再生リストの説明文
- シリーズ終了後のネクストアクション（上位コース・コンサル誘導等）

マーケティングファネルとして機能するよう設計してください。",
            PromptEn = @"Use get_project_summary to understand this video's content, then plan a series (5-10 videos) building on it.

For each episode:
1. Title idea
2. Key theme / learning objectives
3. Recommended duration (minutes)
4. Connection to previous episode
5. CTA (teaser for next video)

Overall plan:
- Series concept and title
- Target viewer persona
- Publishing schedule (recommend weekly)
- Playlist description
- Post-series next action (advanced course, consultation, etc.)

Design it to function as a marketing funnel.",
        },
        new InsightCastPresetPrompt
        {
            Id = "change_mgmt_video",
            CategoryJa = "マーケティング・YouTube",
            CategoryEn = "Marketing & YouTube",
            LabelJa = "社内向けチェンジマネジメント動画化",
            LabelEn = "Internal change management video",
            Icon = "🏢",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"get_scenes で現在の内容を確認し、社内向けチェンジマネジメント動画として最適化してください。

set_multiple_scenes で以下の構成にナレーションを再構成:
1. Why（なぜ変わる必要があるのか — 危機感の醸成）
2. What（何が変わるのか — 具体的な変更点）
3. How（どう変わるのか — ステップと支援体制）
4. Benefits（変わることで何が良くなるのか — 個人レベルのメリット）
5. FAQ（よくある不安への回答）
6. Next Steps（明日からできる第一歩）

経営層の意思決定を現場に浸透させる、共感と行動を促すトーンにしてください。",
            PromptEn = @"Use get_scenes to review content and restructure as an internal change management video.

Use set_multiple_scenes to reorganize narration into:
1. Why (create urgency for change)
2. What (specific changes)
3. How (steps and support structure)
4. Benefits (individual-level advantages)
5. FAQ (address common concerns)
6. Next Steps (actionable first step)

Use a tone that builds empathy and drives action, translating leadership decisions into frontline understanding.",
        },

        // ========================================
        // カテゴリ: ペルソナ付き品質チェック
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "review_training_designer",
            CategoryJa = "品質チェック",
            CategoryEn = "Quality Check",
            LabelJa = "研修設計者チェック",
            LabelEn = "Training designer review",
            Icon = "\U0001F393",
            RecommendedPersonaId = "manabu",
            RequiresContextData = true,
            SystemPromptExtensionJa = """
                あなたは企業研修の設計者として15年以上の経験を持つインストラクショナルデザイナーです。
                ADDIEモデル・メリルの第一原理・ガニェの9教授事象に精通しています。
                以下の観点で厳しくチェックしてください：
                - 学習目標が明確か（KSA: Knowledge, Skill, Attitude の区別）
                - 導入→展開→まとめの構成が教育効果を最大化しているか
                - 1シーンの情報量が適切か（認知負荷理論に基づく7±2の原則）
                - 具体例・演習・振り返りが適切に配置されているか
                - ナレーションのペース・トーンが学習者に適しているか
                問題を発見したら、シーン番号と具体的な改善案を提示してください。
                """,
            SystemPromptExtensionEn = """
                You are a senior instructional designer with 15+ years in corporate training.
                Expert in ADDIE model, Merrill's First Principles, and Gagné's Nine Events.
                Check rigorously for:
                - Clear learning objectives (KSA distinction)
                - Intro → Development → Summary structure maximizing learning outcomes
                - Appropriate information density per scene (7±2 cognitive load principle)
                - Proper placement of examples, exercises, and reflections
                - Narration pace and tone suitable for learners
                Flag issues with scene numbers and specific improvement suggestions.
                """,
            PromptJa = "研修設計の専門家として、この動画の教育効果をチェックしてください。学習目標の明確さ、構成の適切さ、情報量のバランスを確認してください。",
            PromptEn = "As a training design expert, review this video's educational effectiveness. Check learning objectives, structure, and information balance.",
        },
        new InsightCastPresetPrompt
        {
            Id = "review_video_director",
            CategoryJa = "品質チェック",
            CategoryEn = "Quality Check",
            LabelJa = "映像ディレクターチェック",
            LabelEn = "Video director review",
            Icon = "\U0001F3AC",
            RecommendedPersonaId = "manabu",
            RequiresContextData = true,
            SystemPromptExtensionJa = """
                あなたは企業向け映像制作のディレクターとして10年以上の経験を持ちます。
                研修動画・製品紹介動画・社内コミュニケーション動画を多数制作してきました。
                以下の観点で厳しくチェックしてください：
                - オープニングのインパクト（最初の5秒で視聴者を引き込めるか）
                - シーンの切り替えテンポ（1シーンが長すぎないか、短すぎないか）
                - ナレーションと視覚情報の同期（文字と音声の一致）
                - 字幕の読みやすさ（文字数、表示時間）
                - エンディングのCTA（視聴後に何をすべきか明確か）
                - サムネイルのクリック率を上げる改善案
                問題を発見したら、シーン番号と具体的な改善案を提示してください。
                """,
            SystemPromptExtensionEn = """
                You are a corporate video director with 10+ years producing training, product, and communications videos.
                Check rigorously for:
                - Opening impact (hooks viewer in first 5 seconds?)
                - Scene pacing (too long/short per scene?)
                - Narration-visual sync (text and audio alignment)
                - Subtitle readability (character count, display duration)
                - Ending CTA clarity (clear next step for viewers?)
                - Thumbnail click-through rate improvements
                Flag issues with scene numbers and specific improvement suggestions.
                """,
            PromptJa = "映像ディレクターとして、この動画の視聴体験をチェックしてください。テンポ、演出、視聴者の離脱ポイントを重点的に確認してください。",
            PromptEn = "As a video director, review the viewing experience. Focus on pacing, production quality, and potential viewer drop-off points.",
        },
        new InsightCastPresetPrompt
        {
            Id = "review_marketing_strategist",
            CategoryJa = "品質チェック",
            CategoryEn = "Quality Check",
            LabelJa = "マーケティング戦略家チェック",
            LabelEn = "Marketing strategist review",
            Icon = "\U0001F4C8",
            RecommendedPersonaId = "manabu",
            RequiresContextData = true,
            SystemPromptExtensionJa = """
                あなたはデジタルマーケティングの戦略家として、YouTube・SNS動画のパフォーマンス最適化に精通しています。
                以下の観点で厳しくチェックしてください：
                - サムネイル＋タイトルの CTR 予測（クリックしたくなるか）
                - 冒頭15秒の視聴者維持率（離脱を防ぐフックがあるか）
                - SEO 観点でのキーワード配置（タイトル・説明文・字幕）
                - CTA の効果予測（チャンネル登録・問い合わせ・資料請求への誘導）
                - 動画尺の適切さ（ターゲット視聴者の集中力に合っているか）
                - シェアされやすい要素の有無（驚き・共感・実用性）
                問題を発見したら、具体的な改善案と期待される効果を提示してください。
                """,
            SystemPromptExtensionEn = """
                You are a digital marketing strategist specializing in YouTube/SNS video performance optimization.
                Check rigorously for:
                - Thumbnail + title CTR prediction (compelling enough to click?)
                - First 15-second retention (hook to prevent drop-off?)
                - SEO keyword placement (title, description, subtitles)
                - CTA effectiveness (subscribe, inquire, download?)
                - Video length appropriateness for target audience attention span
                - Shareability factors (surprise, empathy, practical value)
                Flag issues with specific improvements and expected impact.
                """,
            PromptJa = "マーケティング戦略家として、この動画の集客・コンバージョン効果をチェックしてください。CTR、視聴維持率、CTA の効果を重点的に確認してください。",
            PromptEn = "As a marketing strategist, review this video's audience acquisition and conversion potential. Focus on CTR, retention, and CTA effectiveness.",
        },

        // ========================================
        // カテゴリ14: PPTX生成 — 研修用
        //   スライド構成設計 → Opus
        //   テキスト生成 → Sonnet
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "pptx_training_onboarding",
            CategoryJa = "PPTX生成・研修",
            CategoryEn = "PPTX Generation (Training)",
            LabelJa = "新入社員研修スライドを自動生成",
            LabelEn = "Auto-generate onboarding training slides",
            Icon = "🎓",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"新入社員向けの研修スライド構成を設計し、各シーンにナレーションを設定してください。

手順:
1. get_scenesで現在のシーン構成を確認
2. 以下の構成でシーンが不足していればadd_sceneで追加:
   - オープニング（研修の目的・ゴール）
   - 会社概要（ビジョン・ミッション・沿革）
   - 組織体制（部門構成・キーパーソン）
   - 業務概要（主要業務フロー）
   - 社内ルール（勤怠・経費・情報セキュリティ）
   - ツール紹介（使用する社内システム）
   - Q&A・次のステップ
3. set_multiple_scenesで全シーンのナレーションを一括設定
   - 1シーン100〜150文字
   - 新入社員が安心できる親しみやすいトーン
   - 各シーン冒頭に「ここでは〜を説明します」の導入文",
            PromptEn = @"Design onboarding training slides and set narration for each scene.

Steps:
1. Check current scenes with get_scenes
2. Add missing scenes with add_scene for this structure:
   - Opening (training goals)
   - Company overview (vision, mission, history)
   - Organization structure
   - Business operations overview
   - Internal rules (attendance, expenses, security)
   - Tools introduction
   - Q&A / Next steps
3. Set all narration with set_multiple_scenes (50-80 words/scene, welcoming tone)",
        },
        new InsightCastPresetPrompt
        {
            Id = "pptx_training_compliance",
            CategoryJa = "PPTX生成・研修",
            CategoryEn = "PPTX Generation (Training)",
            LabelJa = "コンプライアンス研修スライドを生成",
            LabelEn = "Generate compliance training slides",
            Icon = "⚖️",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"コンプライアンス研修用のスライド構成を設計し、ナレーションを設定してください。

手順:
1. get_scenesで現在のシーン構成を確認
2. 以下の構成でadd_sceneで追加:
   - 導入（コンプライアンスとは何か・なぜ重要か）
   - 情報セキュリティ（パスワード管理、メール誤送信、持ち出し禁止）
   - ハラスメント防止（定義、具体例、相談窓口）
   - インサイダー取引禁止（対象者、規制内容）
   - 反社会的勢力排除（判断基準、対応フロー）
   - SNS・情報発信のルール
   - 内部通報制度
   - ケーススタディ（「こんなときどうする？」2-3事例）
   - まとめ・確認テスト
3. set_multiple_scenesでナレーションを一括設定
   - 具体的な事例を交えて説明
   - 「自分ごと」として考えさせるトーン",
            PromptEn = @"Design compliance training slides with narration.

Structure: Introduction → Information Security → Harassment Prevention → Insider Trading → Anti-Social Forces → SNS Rules → Whistleblowing → Case Studies → Summary & Quiz

Use set_multiple_scenes to batch-set narration with specific examples and a tone that makes it personally relevant.",
        },
        new InsightCastPresetPrompt
        {
            Id = "pptx_training_product",
            CategoryJa = "PPTX生成・研修",
            CategoryEn = "PPTX Generation (Training)",
            LabelJa = "製品・サービス研修スライドを生成",
            LabelEn = "Generate product/service training slides",
            Icon = "📦",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"製品・サービスの研修用スライド構成を設計してください。

手順:
1. get_pptx_notesとget_scenesで既存の情報を確認
2. 以下の構成でシーンを追加・整理:
   - 製品/サービスの全体像（ポジショニング、ターゲット）
   - 主要機能・特徴（3-5つに絞って深掘り）
   - 競合との差別化ポイント
   - ユースケース・導入事例（業種別に2-3パターン）
   - デモシナリオ（画面操作の流れ）
   - 価格体系・ライセンス
   - よくある質問（FAQ）
   - 営業トーク例（提案時の話法）
3. set_multiple_scenesでナレーション一括設定
   - 社内メンバーが顧客に説明できるレベルの内容
   - 数字・具体例を多用",
            PromptEn = @"Design product/service training slides.

Structure: Product overview → Key features (3-5 deep dives) → Competitive differentiation → Use cases (2-3 patterns) → Demo scenario → Pricing → FAQ → Sales talk examples

Set narration with set_multiple_scenes. Content should enable team members to explain to customers, heavy on numbers and examples.",
        },
        new InsightCastPresetPrompt
        {
            Id = "pptx_training_it_security",
            CategoryJa = "PPTX生成・研修",
            CategoryEn = "PPTX Generation (Training)",
            LabelJa = "ITセキュリティ研修スライドを生成",
            LabelEn = "Generate IT security training slides",
            Icon = "🔒",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"ITセキュリティ研修のスライド構成を設計してください。

手順:
1. get_scenesで現在のシーン構成を確認し、以下の構成でadd_sceneで追加:
   - なぜセキュリティが大切か（実際の被害事例と金額）
   - パスワード管理（強いパスワードの作り方、多要素認証）
   - フィッシング詐欺（見分け方、実例スクリーンショット風の説明）
   - マルウェア対策（怪しい添付ファイル、USBメモリ）
   - 公衆Wi-Fi・リモートワークのリスク
   - データの取り扱い（分類、暗号化、廃棄）
   - インシデント発生時の対応フロー
   - クイズ（3問）
   - まとめ・相談窓口
2. set_multiple_scenesでナレーション一括設定
   - 「明日から実践できる」具体的なアクションを各シーンに含める
   - 恐怖訴求しすぎず、ポジティブな行動変容を促すトーン",
            PromptEn = @"Design IT security training slides.

Structure: Why security matters (real incidents) → Password management → Phishing → Malware → Public Wi-Fi/remote risks → Data handling → Incident response → Quiz (3 questions) → Summary

Narration should include actionable tips for each scene. Positive behavior-change tone, not fear-based.",
        },

        // ========================================
        // カテゴリ15: PPTX生成 — プレゼン・マニュアル
        //   資料構成 → Opus
        //   テキスト生成 → Sonnet
        // ========================================
        new InsightCastPresetPrompt
        {
            Id = "pptx_manual_operation",
            CategoryJa = "PPTX生成・マニュアル",
            CategoryEn = "PPTX Generation (Manual)",
            LabelJa = "操作マニュアル動画のスライド生成",
            LabelEn = "Generate operation manual video slides",
            Icon = "📖",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"操作マニュアル動画のスライド構成を設計してください。

手順:
1. get_pptx_notesで既存のノート情報を確認
2. 以下の構成でadd_sceneで追加:
   - タイトル（ツール名 + 「操作ガイド」）
   - 目次（この動画で学べること、3-5項目）
   - 事前準備（必要な環境、ログイン方法）
   - 各操作手順（1操作1シーンの原則）
     - 画面名を明記
     - 「①〜をクリック → ②〜を入力 → ③〜を確認」の手順形式
   - よくあるミスと対処法
   - Tips（知っておくと便利な機能）
   - まとめ（操作フロー全体図）
3. set_multiple_scenesでナレーション一括設定
   - 「画面左上の〜ボタンをクリックしてください」のような具体的な指示
   - 初めて使う人でも迷わない丁寧さ",
            PromptEn = @"Design operation manual video slides.

Structure: Title → TOC (3-5 items) → Prerequisites → Step-by-step operations (1 operation per scene) → Common mistakes → Tips → Summary

Narration should use specific UI references ('Click the button in the top-left corner') and be beginner-friendly.",
        },
        new InsightCastPresetPrompt
        {
            Id = "pptx_manual_process",
            CategoryJa = "PPTX生成・マニュアル",
            CategoryEn = "PPTX Generation (Manual)",
            LabelJa = "業務プロセス説明動画のスライド生成",
            LabelEn = "Generate business process explanation slides",
            Icon = "🔄",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"業務プロセス説明動画のスライド構成を設計してください。

手順:
1. get_pptx_notesとget_scenesで既存情報を確認
2. 以下の構成でadd_sceneで追加:
   - プロセス全体像（フロー図の説明）
   - 各ステップの詳細（担当者・入出力・判断基準）
   - 分岐条件（「Aの場合は〜、Bの場合は〜」）
   - 例外処理（イレギュラー時の対応）
   - チェックポイント（品質確認のタイミング）
   - 関連部門との連携ポイント
   - よくある質問
3. set_multiple_scenesでナレーション一括設定
   - 「まず○○部門の担当者が〜します」のように主語を明確に
   - フローの全体と各ステップの位置関係を常に意識させる表現",
            PromptEn = @"Design business process explanation slides.

Structure: Process overview → Step-by-step details (who/input/output/criteria) → Branch conditions → Exception handling → Quality checkpoints → Cross-department coordination → FAQ

Narration should clearly state who does what, maintaining awareness of the overall flow.",
        },
        new InsightCastPresetPrompt
        {
            Id = "pptx_presentation_pitch",
            CategoryJa = "PPTX生成・マニュアル",
            CategoryEn = "PPTX Generation (Manual)",
            LabelJa = "営業ピッチ動画のスライド生成",
            LabelEn = "Generate sales pitch video slides",
            Icon = "💼",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"営業ピッチ動画のスライド構成を設計してください。

手順:
1. get_pptx_notesで製品/サービス情報を確認
2. 以下の構成でadd_sceneで追加（AIDA法則に基づく）:
   - Attention: 衝撃的な数字やデータで注意を引く（「〇〇%の企業が〜」）
   - Interest: 共感できる課題提示（「こんな経験ありませんか？」）
   - Desire: ソリューションの紹介（3つの差別化ポイント）
   - Action: 導入ステップ・CTA（「まずは無料トライアルから」）
   - 導入事例（Before/After）
   - 価格・プラン比較
   - 次のアクション（問い合わせ先、デモ申込）
3. set_multiple_scenesでナレーション一括設定
   - 視聴者の課題に共感する語りかけ口調
   - 数字と具体例で説得力を持たせる",
            PromptEn = @"Design sales pitch video slides using AIDA framework.

Structure: Attention (striking data) → Interest (relatable problem) → Desire (solution + 3 differentiators) → Action (CTA) → Case studies (Before/After) → Pricing → Next steps

Narration should empathize with viewer challenges and use numbers for persuasion.",
        },
        new InsightCastPresetPrompt
        {
            Id = "pptx_presentation_quarterly",
            CategoryJa = "PPTX生成・マニュアル",
            CategoryEn = "PPTX Generation (Manual)",
            LabelJa = "四半期報告プレゼン動画のスライド生成",
            LabelEn = "Generate quarterly report presentation slides",
            Icon = "📊",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"四半期報告プレゼン動画のスライド構成を設計してください。

手順:
1. get_pptx_notesで既存の報告データを確認
2. 以下の構成でadd_sceneで追加:
   - エグゼクティブサマリー（結論を最初に）
   - KPIハイライト（主要指標5つ、前期比付き）
   - 売上・収益の推移（グラフの説明）
   - 部門別/製品別の実績
   - 達成した主要マイルストーン
   - 課題とリスク（対策付き）
   - 来期の計画・目標
   - 質疑応答
3. set_multiple_scenesでナレーション一括設定
   - 数字を必ず含める（「前年同期比12%増の〜」）
   - 結論ファーストで簡潔に",
            PromptEn = @"Design quarterly report presentation slides.

Structure: Executive summary → KPI highlights (5 metrics, YoY) → Revenue trends → Department/product breakdown → Key milestones → Challenges & risks → Next quarter plan → Q&A

Narration must include numbers ('12% YoY increase') and be conclusion-first.",
        },
        new InsightCastPresetPrompt
        {
            Id = "pptx_from_reference",
            CategoryJa = "PPTX生成・マニュアル",
            CategoryEn = "PPTX Generation (Manual)",
            LabelJa = "参考資料からスライド構成を自動生成",
            LabelEn = "Auto-generate slides from reference materials",
            Icon = "📎",
            RecommendedPersonaId = "manabu",

            RequiresContextData = true,
            PromptJa = @"参考資料（スライドノートまたは登録済み参考資料）の内容を読み取り、動画スライド構成を自動生成してください。

手順:
1. get_pptx_notesで全スライドのスピーカーノートを取得
2. get_scenesで現在のシーン構成を確認
3. ノートの内容を分析し、最適なシーン構成を設計:
   - 関連する内容をグループ化
   - 論理的な流れに並べ替え
   - 導入・まとめシーンを追加
   - 1シーン1トピックの原則
4. 不足シーンをadd_sceneで追加
5. set_multiple_scenesで全シーンのナレーションを一括設定
   - ノートの箇条書きを自然な話し言葉に変換
   - 1シーン100〜150文字

参考資料の情報量が多い場合は、重要度に基づいて取捨選択し、動画として最適な長さ（10-15シーン）にまとめてください。",
            PromptEn = @"Read reference materials (slide notes or registered references) and auto-generate video slide structure.

Steps:
1. Get slide notes with get_pptx_notes
2. Check current scenes with get_scenes
3. Analyze and design optimal scene structure (group related content, add intro/summary)
4. Add missing scenes with add_scene
5. Set all narration with set_multiple_scenes (convert bullet points to natural speech, 50-80 words/scene)

If reference material is extensive, prioritize by importance and aim for 10-15 scenes.",
        },
    };
}
