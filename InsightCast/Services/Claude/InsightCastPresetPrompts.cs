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
    public static readonly List<PresetPrompt> All = new()
    {
        // ========================================
        // カテゴリ1: 字幕・翻訳
        //   AI がシーンのナレーション/字幕テキストを読み書きできる
        //   翻訳 → Sonnet (言語ニュアンス重要)
        //   字幕変換 → Haiku (単純なテキスト短縮)
        // ========================================
        new PresetPrompt
        {
            Id = "subtitle_from_narration",
            CategoryJa = "字幕・翻訳",
            CategoryEn = "Subtitles & Translation",
            LabelJa = "ナレーション→字幕を自動生成",
            LabelEn = "Generate subtitles from narration",
            Icon = "📝",
            RecommendedPersonaId = "shunsuke",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "各シーンのナレーションテキストを読み取り、字幕として適切な短いテキストに変換してください。字幕は1行あたり20文字以内が理想です。get_scenesツールでシーン情報を取得し、set_multiple_scenesツールで字幕を設定してください。",
            PromptEn = "Read the narration text from each scene and convert it into short, subtitle-friendly text. Ideally, each subtitle line should be within 40 characters. Use the get_scenes tool to get scene info and set_multiple_scenes to set subtitles.",
        },
        new PresetPrompt
        {
            Id = "translate_narration_en",
            CategoryJa = "字幕・翻訳",
            CategoryEn = "Subtitles & Translation",
            LabelJa = "ナレーションを英語に翻訳",
            LabelEn = "Translate narration to English",
            Icon = "🌐",
            RecommendedPersonaId = "megumi",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションテキストを取得し、英語に翻訳してください。直訳ではなく、教育動画にふさわしい自然な英語表現にしてください。翻訳結果をset_multiple_scenesツールでナレーションとして上書き設定してください。",
            PromptEn = "Use get_scenes to get all narration text and translate to English. Use natural English expressions appropriate for educational videos, not literal translations. Apply translated narration with set_multiple_scenes.",
        },
        new PresetPrompt
        {
            Id = "translate_narration_ja",
            CategoryJa = "字幕・翻訳",
            CategoryEn = "Subtitles & Translation",
            LabelJa = "ナレーションを日本語に翻訳",
            LabelEn = "Translate narration to Japanese",
            Icon = "🇯🇵",
            RecommendedPersonaId = "megumi",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションテキストを取得し、日本語に翻訳してください。教育動画にふさわしい分かりやすい日本語にしてください。翻訳結果をset_multiple_scenesツールでナレーションとして上書き設定してください。",
            PromptEn = "Use get_scenes to get all narration text and translate to Japanese. Use clear, easy-to-understand Japanese appropriate for educational videos. Apply translated narration with set_multiple_scenes.",
        },

        // ========================================
        // カテゴリ2: ナレーション生成
        //   AI がスライドノート・シーンタイトルを読んでナレーション生成
        //   校正・新規作成 → Sonnet
        //   ノート変換 → Sonnet
        // ========================================
        new PresetPrompt
        {
            Id = "narration_from_notes",
            CategoryJa = "ナレーション生成",
            CategoryEn = "Narration",
            LabelJa = "スライドノート→ナレーション生成",
            LabelEn = "Generate narration from slide notes",
            Icon = "🎙️",
            RecommendedPersonaId = "megumi",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_pptx_notesツールでスライドのノート（スピーカーノート）を取得し、各シーンのナレーションに変換してください。箇条書きのノートを自然な話し言葉に変換し、set_multiple_scenesツールで設定してください。1シーンあたり100〜200文字を目安にしてください。ノートが空のシーンはシーンタイトルから内容を推測してナレーションを作成してください。",
            PromptEn = "Use get_pptx_notes to get slide speaker notes and convert them into narration for each scene. Transform bullet-point notes into natural spoken language, aiming for 50-100 words per scene. Use set_multiple_scenes to apply. For scenes without notes, infer content from the scene title.",
        },
        new PresetPrompt
        {
            Id = "improve_narration",
            CategoryJa = "ナレーション生成",
            CategoryEn = "Narration",
            LabelJa = "ナレーション文の校正・改善",
            LabelEn = "Proofread & improve narration",
            Icon = "✨",
            RecommendedPersonaId = "megumi",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを読み取り、文法の修正、表現の改善、分かりやすさの向上を行ってください。大幅な変更は避け、自然な改善にとどめてください。改善したテキストをset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to read all narration text, then proofread for grammar, improve expressions, and enhance clarity. Avoid major changes — keep improvements natural. Apply changes with set_multiple_scenes.",
        },
        new PresetPrompt
        {
            Id = "create_narration",
            CategoryJa = "ナレーション生成",
            CategoryEn = "Narration",
            LabelJa = "シーンタイトルからナレーション新規作成",
            LabelEn = "Create narration from scene titles",
            Icon = "📖",
            RecommendedPersonaId = "megumi",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのタイトルと既存テキストを取得し、各シーンに適したナレーションを新規作成してください。導入→本題→まとめの流れを意識し、1シーンあたり100〜150文字の自然な話し言葉にしてください。set_multiple_scenesで設定してください。\n\n※AIはシーンのタイトル・ナレーション・字幕のテキスト情報のみ参照できます。画像や動画の内容は参照できません。",
            PromptEn = "Use get_scenes to get all scene titles and existing text, then create new narration for each scene. Follow an intro → body → summary flow, aiming for 50-80 words per scene in natural spoken language. Apply with set_multiple_scenes.\n\nNote: AI can only read scene titles, narration, and subtitle text. It cannot see images or video content.",
        },

        // ========================================
        // カテゴリ3: 構成・レビュー
        //   テキスト内容に基づく構成分析 → Opus
        // ========================================
        new PresetPrompt
        {
            Id = "check_structure",
            CategoryJa = "構成・レビュー",
            CategoryEn = "Structure Review",
            LabelJa = "ナレーション構成をレビュー",
            LabelEn = "Review narration structure",
            Icon = "🔍",
            RecommendedPersonaId = "manabu",
            Mode = "advice",
            RequiresContextData = true,
            PromptJa = "get_scenesツールとget_project_summaryツールでプロジェクト情報を取得し、ナレーションの構成を分析してアドバイスしてください:\n\n1. シーン間のナレーションの流れと論理構造\n2. 各シーンのナレーション量のバランス（長すぎ・短すぎ）\n3. 導入→本題→まとめの構成が適切か\n4. ナレーションが空のシーン、メディアが未設定のシーンの指摘\n5. 具体的な改善提案（シーンの追加・削除・並べ替えが必要な場合は、add_scene / remove_scene / move_scene ツールで対応可能であることも伝える）\n\n※テキスト情報とメディアパスの有無のみ分析できます。画像・動画の内容は参照できません。",
            PromptEn = "Use get_scenes and get_project_summary to analyze the narration structure and provide advice:\n\n1. Flow and logical structure between scene narrations\n2. Balance of narration length (too long/short)\n3. Whether intro → body → summary structure is appropriate\n4. Flag scenes with empty narration or missing media\n5. Specific improvement suggestions (mention that add_scene / remove_scene / move_scene tools are available if structural changes are needed)\n\nNote: Only text data and media path presence can be analyzed. Image/video content cannot be reviewed.",
        },
        new PresetPrompt
        {
            Id = "check_completeness",
            CategoryJa = "構成・レビュー",
            CategoryEn = "Structure Review",
            LabelJa = "抜け漏れチェック",
            LabelEn = "Check for missing content",
            Icon = "✅",
            RecommendedPersonaId = "shunsuke",
            Mode = "advice",
            RequiresContextData = true,
            PromptJa = "get_scenesツールとget_project_summaryツールで全シーンを確認し、以下の抜け漏れをチェックしてリスト化してください:\n\n- ナレーションが空のシーン\n- 字幕が空のシーン（ナレーションはあるのに字幕がない）\n- メディア（画像/動画）が未設定のシーン\n- ナレーションが極端に短いシーン（30文字未満）\n- ナレーションが極端に長いシーン（300文字超）\n\nシーン番号と現状を表形式で示してください。",
            PromptEn = "Use get_scenes and get_project_summary to check all scenes and list any gaps:\n\n- Scenes with empty narration\n- Scenes with narration but no subtitles\n- Scenes with no media (image/video) set\n- Scenes with very short narration (under 30 chars)\n- Scenes with very long narration (over 300 chars)\n\nList specific scene numbers and their current state in a table format.",
        },

        // ========================================
        // カテゴリ4: トーン調整
        //   全て Sonnet (言語的ニュアンス・文体制御が重要)
        // ========================================
        new PresetPrompt
        {
            Id = "tone_concise",
            CategoryJa = "トーン調整",
            CategoryEn = "Tone Adjustment",
            LabelJa = "もっと簡潔にする",
            LabelEn = "Make more concise",
            Icon = "✂️",
            RecommendedPersonaId = "megumi",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを取得し、より簡潔に書き直してください:\n\n- 冗長な表現や繰り返しを削除\n- 一文を短くし、要点を先に伝える\n- 「〜ということになります」→「〜です」等の圧縮\n- 各シーンのナレーションを現在の70〜80%の文字数に圧縮\n- 重要な情報は削らず、表現のみ簡潔にする\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to get all narration and rewrite it more concisely:\n\n- Remove redundant expressions and repetition\n- Shorten sentences, lead with key points\n- Compress to 70-80% of current word count per scene\n- Preserve important information, only simplify expression\n\nApply with set_multiple_scenes.",
        },
        new PresetPrompt
        {
            Id = "tone_formal",
            CategoryJa = "トーン調整",
            CategoryEn = "Tone Adjustment",
            LabelJa = "もっと丁寧・フォーマルにする",
            LabelEn = "Make more formal & polite",
            Icon = "👔",
            RecommendedPersonaId = "megumi",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを取得し、よりフォーマルで丁寧なトーンに調整してください:\n\n- 敬語（です・ます調）を徹底\n- 「〜してね」→「〜してください」等のカジュアル表現を修正\n- ビジネスにふさわしい語彙を使用\n- 社外向けプレゼン・経営層向け報告にも使えるトーンに\n- 過度に硬くならないよう、分かりやすさは維持\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to get all narration and adjust to a more formal, polite tone:\n\n- Use consistently professional language\n- Replace casual expressions with formal equivalents\n- Use business-appropriate vocabulary\n- Suitable for executive presentations and external audiences\n- Maintain clarity — don't make it overly stiff\n\nApply with set_multiple_scenes.",
        },
        new PresetPrompt
        {
            Id = "tone_casual",
            CategoryJa = "トーン調整",
            CategoryEn = "Tone Adjustment",
            LabelJa = "もっとカジュアル・親しみやすく",
            LabelEn = "Make more casual & friendly",
            Icon = "😊",
            RecommendedPersonaId = "megumi",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを取得し、カジュアルで親しみやすいトーンに調整してください:\n\n- 「〜ですよね」「〜しましょう」等の親しみやすい表現を使用\n- 視聴者に語りかけるような口調（「皆さん」「一緒に〜」）\n- 堅い専門用語をやさしい言葉に置き換え\n- YouTube動画やSNSコンテンツにふさわしいトーン\n- 敬語は維持しつつ、距離感を縮める\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to get all narration and adjust to a casual, friendly tone:\n\n- Use approachable expressions and conversational style\n- Address viewers directly ('you', 'let's')\n- Replace jargon with simpler words\n- Suitable for YouTube and social media content\n- Stay respectful while being warm and engaging\n\nApply with set_multiple_scenes.",
        },

        // ========================================
        // カテゴリ5: 品質チェック
        //   発音ヒント → Haiku (ルールベースの機械的変換)
        //   長さ均一化・文体統一 → Sonnet (全シーン横断分析+リライト)
        // ========================================
        new PresetPrompt
        {
            Id = "normalize_length",
            CategoryJa = "品質チェック",
            CategoryEn = "Quality Check",
            LabelJa = "ナレーション長さを均一化",
            LabelEn = "Normalize narration length",
            Icon = "⚖️",
            RecommendedPersonaId = "megumi",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを取得し、長さのバラつきを分析してください。極端に短い・長いシーンを特定し、全体のバランスが取れるよう調整してください:\n\n- 目標: 各シーン80〜150文字程度\n- 短すぎるシーン: 補足説明を追加\n- 長すぎるシーン: 要点を絞って簡潔に\n- 内容の意味は変えない\n\nset_multiple_scenesで調整結果を設定してください。",
            PromptEn = "Use get_scenes to analyze narration length variation across scenes. Identify scenes that are too short or too long, then balance them:\n\n- Target: 40-80 words per scene\n- Too short: Add supporting detail\n- Too long: Condense to key points\n- Preserve original meaning\n\nApply with set_multiple_scenes.",
        },
        new PresetPrompt
        {
            Id = "add_furigana_hints",
            CategoryJa = "品質チェック",
            CategoryEn = "Quality Check",
            LabelJa = "VOICEVOX用に発音最適化",
            LabelEn = "Optimize pronunciation for VOICEVOX",
            Icon = "🔤",
            RecommendedPersonaId = "shunsuke",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを確認し、VOICEVOX音声合成エンジンが正しく読み上げられるよう発音補正してください:\n\n- 英語略語をカタカナに変換（「API」→「エーピーアイ」「IT」→「アイティー」等）\n- 数字の読み方を明確化（「10個」→「じゅっこ」等、必要に応じてひらがなに）\n- 難読固有名詞にひらがな表記を検討\n- 記号を読み上げ可能な表現に変換（「→」→「から」等）\n- 句読点の位置を読みやすさの観点で調整\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to review all narration and optimize for VOICEVOX text-to-speech engine:\n\n- Convert abbreviations to phonetic form (e.g., 'API' → 'A-P-I')\n- Clarify number readings where ambiguous\n- Add phonetic readings for difficult proper nouns\n- Replace symbols with speakable expressions ('→' → 'to')\n- Adjust punctuation for natural reading pace\n\nApply with set_multiple_scenes.",
        },
        new PresetPrompt
        {
            Id = "tone_consistency",
            CategoryJa = "品質チェック",
            CategoryEn = "Quality Check",
            LabelJa = "トーン・文体を統一",
            LabelEn = "Unify tone & writing style",
            Icon = "🎯",
            RecommendedPersonaId = "megumi",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "get_scenesツールで全シーンのナレーションを分析し、トーンと文体を統一してください:\n\n- 敬体（です・ます調）か常体（だ・である調）を統一\n- 主語の使い方を統一（「私たち」「当社」等）\n- 接続詞の使い方を統一\n- 文末表現のバリエーションを適度に維持\n- 全体を通して一貫した印象になるよう調整\n\nset_multiple_scenesで設定してください。",
            PromptEn = "Use get_scenes to analyze all narration and unify the tone and writing style:\n\n- Standardize formality level across scenes\n- Unify subject references ('we', 'our company', etc.)\n- Standardize conjunction usage\n- Maintain appropriate variation in sentence endings\n- Ensure a consistent impression throughout\n\nApply with set_multiple_scenes.",
        },

        // ========================================
        // カテゴリ6: 全自動
        //   複雑なマルチステップ → Opus
        // ========================================
        new PresetPrompt
        {
            Id = "oneclick_full_video",
            CategoryJa = "全自動",
            CategoryEn = "Full Auto",
            LabelJa = "ナレーション＋字幕を一括生成",
            LabelEn = "Auto-generate narration + subtitles",
            Icon = "🚀",
            RecommendedPersonaId = "manabu",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "全シーンのナレーションと字幕を一括で生成してください:\n\n1. get_pptx_notesでスライドのスピーカーノートを取得\n2. get_scenesで全シーンのタイトルと現状を確認\n3. シーン数が内容に対して不足している場合は、add_sceneで必要なシーンを追加\n4. ノートの内容をもとに、各シーンの教育的なナレーションを生成（100〜150文字/シーン）\n5. ナレーションから字幕用の短縮テキストを生成（20文字以内/シーン）\n6. set_multiple_scenesで全シーンのナレーションと字幕を一括設定（1回のツール呼び出しでまとめて設定すること）\n\nノートがないシーンは、シーンのタイトルから内容を推測してナレーションを作成してください。自然な話し言葉で、導入→本題→まとめの流れを意識してください。",
            PromptEn = "Auto-generate narration and subtitles for all scenes:\n\n1. Use get_pptx_notes to get slide speaker notes\n2. Use get_scenes to check current state and titles\n3. If the scene count is insufficient for the content, add scenes with add_scene\n4. Generate educational narration for each scene based on notes (50-80 words/scene)\n5. Create shortened subtitle text from narration (under 40 chars/scene)\n6. Use set_multiple_scenes to apply ALL narration and subtitles in a single tool call\n\nFor scenes without notes, infer content from the scene title. Use natural spoken language with an intro → body → summary flow.",
        },
        new PresetPrompt
        {
            Id = "oneclick_bilingual",
            CategoryJa = "全自動",
            CategoryEn = "Full Auto",
            LabelJa = "日本語ナレーション＋英語字幕を一括生成",
            LabelEn = "Auto-generate JP narration + EN subtitles",
            Icon = "🌏",
            RecommendedPersonaId = "manabu",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "日本語ナレーション＋英語字幕を一括生成してください:\n\n1. get_scenesで全シーンのタイトルと現状を確認、get_pptx_notesでノートも取得\n2. ノートやタイトルをもとに各シーンの日本語ナレーションを生成（100〜150文字/シーン）\n3. そのナレーションを自然な英語に翻訳し、字幕として設定\n4. set_multiple_scenesで全シーンに日本語ナレーション＋英語字幕を一括設定\n\n日本語ナレーションは自然な話し言葉、英語字幕は直訳ではなく自然な表現にしてください。",
            PromptEn = "Auto-generate JP narration + EN subtitles:\n\n1. Use get_scenes and get_pptx_notes to gather info\n2. Generate Japanese narration for each scene from notes/titles (100-150 chars/scene)\n3. Translate narration to natural English for subtitles\n4. Use set_multiple_scenes to apply JP narration + EN subtitles\n\nJapanese narration should be natural spoken language. English subtitles should be natural, not literal translations.",
        },

        // ========================================
        // カテゴリ7: シーン操作
        //   シーン追加・削除・並べ替え → Opus (複雑な構造変更)
        // ========================================
        new PresetPrompt
        {
            Id = "auto_compose_scenes",
            CategoryJa = "シーン操作",
            CategoryEn = "Scene Management",
            LabelJa = "構成自動生成（タイトルからシーン作成）",
            LabelEn = "Auto-compose scenes from titles",
            Icon = "🏗️",
            RecommendedPersonaId = "manabu",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "以下の手順でシーン構成を自動生成してください:\n\n1. get_scenesとget_pptx_notesで現在のシーン構成とノートを確認\n2. 導入→本題→まとめの構成に必要なシーンを分析し、不足分をadd_sceneで追加（末尾に追加でOK）\n3. 必要に応じてmove_sceneでシーン順序を最適化\n4. 全シーンのナレーションをまとめてset_multiple_scenesで一括設定（個別のset_scene_narrationではなく、必ず一括で設定すること）\n5. 結果を報告\n\n重要: ツール呼び出し回数を節約するため、ナレーション設定は必ずset_multiple_scenesで一度にまとめて行ってください。",
            PromptEn = "Auto-compose the scene structure:\n\n1. Use get_scenes and get_pptx_notes to check current structure and notes\n2. Analyze what scenes are needed for an intro → body → summary flow, add missing ones with add_scene (append at end is fine)\n3. Optimize scene order with move_scene if needed\n4. Set narration for ALL scenes at once using set_multiple_scenes (do NOT use individual set_scene_narration calls)\n5. Report the results\n\nImportant: To conserve tool call budget, always batch narration updates into a single set_multiple_scenes call.",
        },
        new PresetPrompt
        {
            Id = "cleanup_scenes",
            CategoryJa = "シーン操作",
            CategoryEn = "Scene Management",
            LabelJa = "シーン整理（空シーン削除＋順序最適化）",
            LabelEn = "Cleanup scenes (remove empty + optimize order)",
            Icon = "🧹",
            RecommendedPersonaId = "manabu",
            Mode = "check",
            RequiresContextData = true,
            PromptJa = "シーンの整理を行ってください:\n\n1. get_scenesで全シーンを確認\n2. ナレーション・字幕・メディアがすべて空のシーンを特定し、一覧を報告\n3. 空シーンをremove_sceneで削除（インデックスが大きい方から順に削除すること。最低1シーンは残す）\n4. 削除後、get_scenesで最新状態を取得し、残ったシーンの順序を分析\n5. 論理的な流れになるようmove_sceneで並べ替えを実行\n6. 最終構成を報告\n\n重要: シーン削除時はインデックスのずれを防ぐため、必ず末尾側（大きいインデックス）から順に削除してください。",
            PromptEn = "Clean up the scene structure:\n\n1. Use get_scenes to review all scenes\n2. Identify scenes where narration, subtitles, and media are all empty, and report the list\n3. Remove empty scenes with remove_scene (delete from highest index first to avoid index shifting; keep at least 1 scene)\n4. After deletion, use get_scenes to get the updated state and analyze remaining scene order\n5. Reorder scenes with move_scene for logical flow\n6. Report the final structure\n\nImportant: Always delete scenes from highest index to lowest to prevent index shifting issues.",
        },
    };
}
